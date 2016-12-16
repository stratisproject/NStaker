using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;
using StratisMinter.Store;
using System.Linq;
using Microsoft.Extensions.Logging;
using nStratis.Protocol.Behaviors;

namespace StratisMinter.Services
{

	public class ChainIndex : ConcurrentChain , IBlockRepository, 
		IBlockTransactionMapStore, ITransactionRepository
	{
		private BlockStore store;
		private IndexedBlockStore indexStore;
		private BlockMemoryStore blockMemoryStore;

		public TransactionToBlockItemIndex TransactionIndex { get; private set; }
		public ChainedBlock LastIndexedBlock { get; private set; }

		public void Initialize(Context context)
		{
			// todo: create a repository that persists index data to file
			this.store = new BlockStore(context.Config.FolderLocation, context.Network);
			this.indexStore = new IndexedBlockStore(new InMemoryNoSqlRepository(), store);
			this.TransactionIndex = new TransactionToBlockItemIndex(context);
			this.blockMemoryStore = new BlockMemoryStore(context);
		}

		public void ReIndexStore()
		{
			this.indexStore.ReIndex();
			this.LastIndexedBlock = this.FindLastIndexedBlock();
		}

		public void PosCatchup()
		{
			// ensure chain headers POS params are at the 
			// same level of the chain index
			var pindex = this.Tip;
			var stack = new Stack<ChainedBlock>();
			while (pindex.Previous != null && pindex.Previous.Previous != null && !pindex.Header.PosParameters.IsSet())
			{
				stack.Push(pindex);
				pindex = pindex.Previous;
			}
			while (stack.Any())
			{
				pindex = stack.Pop();

				var block = this.GetFullBlock(pindex.HashBlock);
				if (block == null)
					break;

				if (!this.ValidateBlock(block))
					throw new InvalidBlockException();
			}
		}

		public bool ValidateBlock(Block block)
		{
			ChainedBlock chainedBlock;
			return this.ValidateBlock(block, out chainedBlock);
		}

		public bool ValidateBlock(Block block, out ChainedBlock chainedBlock)
		{
			chainedBlock = this.GetBlock(block.GetHash());
			if (chainedBlock == null)
				return false;

			if (!block.Header.PosParameters.IsSet())
				chainedBlock.Header.PosParameters = block.SetPosParams();

			return nStratis.temp.BlockValidator.CheckAndComputeStake(this, this, this, this, chainedBlock, block);
		}

		public bool ValidateAndAddBlock(Block block)
		{
			// before adding a block it must be validated
			// so  it makes sense to group the 
			// functionality together in one place

			ChainedBlock chainedBlock;
			if (!this.ValidateBlock(block, out chainedBlock))
				return false;
			
			this.indexStore.Put(block);
			this.blockMemoryStore.Add(block, chainedBlock.HashBlock);
			this.LastIndexedBlock = chainedBlock;
			this.TransactionIndex.Add(block);

			return true;
		}

		public Block GetFullBlock(uint256 blockId)
		{
			return this.blockMemoryStore.Get(blockId, () => this.indexStore.Get(blockId));
		}

		public ChainedBlock FindLastIndexedBlock()
		{
			return this.EnumerateToLastIndexedBlock().Last();
		}

		public IEnumerable<ChainedBlock> EnumerateToLastIndexedBlock()
		{
			var current = this.Tip;

			while (current != this.Genesis)
			{
				if (indexStore.Get(current.HashBlock) != null)
				{
					// we found the last block stop searching
					yield return current;
					yield break;
				}

				yield return current;
				current = current.Previous;
			}

			yield return this.Genesis;
		}

		public Task<Block> GetBlockAsync(uint256 blockId)
		{
			return Task.FromResult(this.GetFullBlock(blockId));
		}

		public uint256 GetBlockHash(uint256 trxHash)
		{
			return this.TransactionIndex.Find(trxHash);
		}

		public Task<Transaction> GetAsync(uint256 txId)
		{
			var blockId = this.GetBlockHash(txId);
			var block = this.GetFullBlock(blockId);
			return Task.FromResult(block.Transactions.First(trx => trx.GetHash() == txId));
		}

		public Task PutAsync(uint256 txId, Transaction tx)
		{
			throw new NotImplementedException();
		}

		public override ChainedBlock SetTip(ChainedBlock block)
		{
			var oldTip = base.SetTip(block);

			if (this.LastIndexedBlock != null)
			{
				var tipdindex = this.LastIndexedBlock;
				var pindex = this.Tip.FindAncestorOrSelf(this.LastIndexedBlock.HashBlock);

				while (!pindex.Header.PosParameters.IsSet())
				{
					pindex.Header.PosParameters = tipdindex.Header.PosParameters;
					pindex = pindex.Previous;
					tipdindex = tipdindex.Previous;
				}
			}

			return oldTip;
		}
	}

	public class ChainSyncService : IStoppable, IDiskStore
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ILogger logger;

		public ChainIndex ChainIndex { get; }

		public ChainSyncService(Context context, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory)
		{
			this.context = context;
			this.ChainIndex = this.context.ChainIndex;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ChainSyncService>();
		}

		public ChainSyncService LoadHeaders()
		{

			// load headers form file (or genesis)
			if (File.Exists(this.context.Config.File("headers.dat")))
			{
				this.logger.LogInformation("Loading headers form disk...");
				this.ChainIndex.Load(File.ReadAllBytes(this.context.Config.File("headers.dat")));
			}
			else
			{
				this.logger.LogInformation("Loading headers no file found...");
				var genesis = this.context.Network.GetGenesis();
				this.ChainIndex.SetTip(new ChainedBlock(genesis.Header, 0));
				// validate the block to generate the pos params
				this.ChainIndex.ValidateBlock(genesis);
			}

			// load the index chain this will 
			// add each block index to memory for fast lookup
			this.ChainIndex.Initialize(this.context);

			// sync the headers chain
			this.logger.LogInformation("Sync chain headers with network...");
			this.SyncChain();

			// index the sore
			this.logger.LogInformation("ReIndex block store...");
			this.ChainIndex.ReIndexStore();

			// load transaction indexes
			this.logger.LogInformation("Load transaction index store...");
			this.ChainIndex.TransactionIndex.Load();
			this.logger.LogInformation("ReIndex transaction store...");
			this.ChainIndex.TransactionIndex.ReIndex(this.ChainIndex);

			// recalculate any pos parameters that
			// may have bene missed
			this.logger.LogInformation("Catching up with POS calculations...");
			this.ChainIndex.PosCatchup();

			// sync the headers and save to disk
			this.logger.LogInformation("Save ChainHeaders to disk...");
			this.SaveToDisk();

			return this;
		}

		public void SyncChain()
		{
			// download all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			var node = this.nodeConnectionService.GetNode(true);
			node.SynchronizeChain(ChainIndex, null, context.CancellationToken);
		}

		private readonly LockObject saveLock = new LockObject();
		private long savedHeight = 0;

		// this method is thread safe
		// it should be called periodically by a behaviour  
		// that is in charge of keeping the chin in sync
		public void SaveToDisk()
		{
			saveLock.Lock(() => this.ChainIndex.Tip.Height > savedHeight, () =>
			{
				using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
				{
					this.ChainIndex.WriteTo(file);
				}

				this.savedHeight = this.ChainIndex.Tip.Height;
			});
		}

		public void OnStop()
		{
			// stop the syncing behaviour

			// save the current header chain to disk
			this.SaveToDisk();
		}
	}
}
