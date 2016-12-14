using System;
using System.IO;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;
using StratisMinter.Store;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace StratisMinter.Services
{

	public class ChainIndex : ConcurrentChain , IBlockRepository, 
		IBlockTransactionMapStore, ITransactionRepository
	{
		private BlockStore store;
		private IndexedBlockStore indexStore;

		public TransactionToBlockItemIndex TransactionIndex { get; private set; }
		public ChainedBlock LastIndexedBlock { get; private set; }

		public void Initialize(Context context)
		{
			// todo: create a repository that persists index data to file
			this.store = new BlockStore(context.Config.FolderLocation, context.Network);
			this.indexStore = new IndexedBlockStore(new InMemoryNoSqlRepository(), store);
			this.TransactionIndex = new TransactionToBlockItemIndex(context);
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
			while (pindex.Previous != null && pindex.Previous.Previous != null && !pindex.Header.PosParameters.IsSet())
				pindex = pindex.Previous;

			while (pindex != null)
			{
				var block = this.GetFullBlock(pindex.HashBlock);
				if (block == null)
					break;
				if (!this.ValidateBlock(block))
					throw new InvalidBlockException();
				pindex = this.GetBlock(pindex.Height + 1);
			}
		}

		public bool ValidateBlock(Block block)
		{
			var chainedBlock = this.GetBlock(block.GetHash());
			// todo: add a check in the validator to make sure posparams are set

			if (!block.Header.PosParameters.IsSet())
			{
				block.SetPosParams();
			}

			return nStratis.temp.BlockValidator.CheckAndComputeStake(this, this, this, this, chainedBlock, block);
		}

		public void AddBlock(Block block)
		{
			var chainedBlock = this.GetBlock(block.GetHash());

			if (!chainedBlock.Header.PosParameters.IsSet())
				throw new InvalidBlockException("POS params must be set");
			
			this.indexStore.Put(block);
			this.LastIndexedBlock = chainedBlock;
			foreach (var trx in block.Transactions)
				this.TransactionIndex.TryAdd(trx.GetHash().AsBitcoinSerializable(), block.GetHash().AsBitcoinSerializable());
		}

		public Block GetFullBlock(uint256 blockId)
		{
			return this.indexStore.Get(blockId);
		}

		private ChainedBlock FindLastIndexedBlock()
		{
			var current = this.Tip;

			while (current != this.Genesis)
			{
				if (indexStore.Get(current.HashBlock) != null)
					return current;
				current = current.Previous;
			}

			return this.Genesis;
		}

		public Task<Block> GetBlockAsync(uint256 blockId)
		{
			return Task.FromResult(this.GetFullBlock(blockId));
		}

		public uint256 GetBlockHash(uint256 trxHash)
		{
			return this.TransactionIndex.Find(trxHash.AsBitcoinSerializable()).Value;
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
	}

	public class ChainSyncService : IStopable, IDiskStore
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
			this.logger.LogInformation("Save transaction store...");
			this.ChainIndex.TransactionIndex.SaveToDisk();

			// recalculate any pos parameters that
			// may have bene missed
			this.logger.LogInformation("POS Catchup...");
			this.ChainIndex.PosCatchup();

			// sync the headers and save to disk
			this.logger.LogInformation("Save Chain...");
			this.SaveToDisk();

			// enable sync on the behaviours 
			this.nodeConnectionService.EnableHeaderSyncing();

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
