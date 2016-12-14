using System;
using System.IO;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;
using StratisMinter.Store;
using System.Linq;

namespace StratisMinter.Services
{

	public class ChainIndex : ConcurrentChain , IBlockRepository, IBlockTransactionMapStore, ITransactionRepository
	{
		private BlockStore store;
		private IndexedBlockStore indexStore;
		private TransactionToBlockItemIndex transactionIndex;

		public ChainedBlock LastIndexedBlock { get; private set; }

		public void Load(Context context)
		{
			// todo: create a repository that persists index data to file
			this.store = new BlockStore(context.Config.FolderLocation, context.Network);
			this.indexStore = new IndexedBlockStore(new InMemoryNoSqlRepository(), store);
			this.indexStore.ReIndex();
			this.LastIndexedBlock = this.FindLastIndexedBlock();
			this.transactionIndex = new TransactionToBlockItemIndex(context);

			// load transaction indexes
			this.transactionIndex.Load();

			// bring transaction indexes to the same level 
			// as the indexed blocks, in case of a bad shutdown
			while (this.LastIndexedBlock.HashBlock != this.transactionIndex.LastBlockId)
			{
				var findblock = this.GetBlock(this.transactionIndex.LastBlockId ?? this.Genesis.HashBlock);
				var next = base.GetBlock(findblock.Height + 1);
				if (next == null)
					break;
				var block = this.GetFullBlock(next.HashBlock);
				if (block == null)
					break;
				foreach (var trx in block.Transactions)
					this.transactionIndex.TryAdd(trx.GetHash().AsBitcoinSerializable(), block.GetHash().AsBitcoinSerializable());
			}

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
				this.ValidaBlock(block);
				pindex = this.GetBlock(pindex.Height + 1);
			}

			this.Save();
		}

		public void Save()
		{
			this.transactionIndex.Save();
		}

		public bool ValidaBlock(Block block)
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
				this.transactionIndex.TryAdd(trx.GetHash().AsBitcoinSerializable(), block.GetHash().AsBitcoinSerializable());
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
			return this.transactionIndex.Find(trxHash.AsBitcoinSerializable()).Value;
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

	public class ChainSyncService : ITerminate
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;

		public ChainIndex ChainIndex { get; }

		public ChainSyncService(Context context, NodeConnectionService nodeConnectionService)
		{
			this.context = context;
			this.ChainIndex = this.context.ChainIndex;
			this.nodeConnectionService = nodeConnectionService;
		}

		public ChainSyncService LoadHeaders()
		{
			// load headers form file (or genesis)
			if (File.Exists(this.context.Config.File("headers.dat")))
			{
				this.ChainIndex.Load(File.ReadAllBytes(this.context.Config.File("headers.dat")));
			}
			else
			{
				var genesis = this.context.Network.GetGenesis();
				this.ChainIndex.SetTip(new ChainedBlock(genesis.Header, 0));
				// validate the block to generate the pos params
				this.ChainIndex.ValidaBlock(genesis);
			}

			// load the index chain this will 
			// add each block index to memory for fast lookup
			this.ChainIndex.Load(this.context);


			// sync the headers and save to disk
			this.SyncChain(true);

			// enable sync on the behaviours 
			this.nodeConnectionService.EnableHeaderSyncing();

			return this;
		}

		public void SyncChain(bool saveToDisk = false)
		{
			// download all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			var node = this.nodeConnectionService.GetNode(true);
			node.SynchronizeChain(ChainIndex, null, context.CancellationToken);

			if(saveToDisk)
				this.SaveChainToDisk();
		}

		private LockObject saveLock = new LockObject();
		private long savedHeight = 0;

		// this method is thread safe
		// it should be called periodically by a behaviour  
		// that is in charge of keeping the chin in sync
		public void SaveChainToDisk()
		{
			saveLock.Lock(() => this.ChainIndex.Tip.Height > savedHeight, () =>
				{
					using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
					{
						this.ChainIndex.WriteTo(file);
					}

					this.savedHeight = this.ChainIndex.Tip.Height;
				});

			this.ChainIndex.Save();
		}

		public void Save()
		{
			this.SaveChainToDisk();
			this.ChainIndex.Save();
		}

		public void OnStop()
		{
			// stop the syncing behaviour

			// save the current header chain to disk
			this.SaveChainToDisk();
		}
	}
}
