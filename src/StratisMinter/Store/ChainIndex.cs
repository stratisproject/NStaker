using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;
using StratisMinter.Services;

namespace StratisMinter.Store
{
	public class ChainIndex : ConcurrentChain , IBlockRepository, 
		IBlockTransactionMapStore, ITransactionRepository
	{
		private StakeBlockStore store;
		private IndexedStakeBlockStore indexStore;
		private BlockMemoryStore blockMemoryStore;
		private Context context;

		public TransactionToBlockItemIndex TransactionIndex { get; private set; }
		public ChainedBlock LastIndexedBlock { get; private set; }
		public Dictionary<uint256, ChainedBlock> AlternateTips { get; private set; }

		public void Initialize(Context context)
		{
			this.context = context;
			this.store = new StakeBlockStore(this.context.Config.FolderLocation, this.context.Network);
			this.indexStore = new IndexedStakeBlockStore(new InMemoryNoSqlRepository(), store);
			this.TransactionIndex = new TransactionToBlockItemIndex(this.context);
			this.blockMemoryStore = new BlockMemoryStore(this.context);
			this.AlternateTips = new Dictionary<uint256, ChainedBlock>();
		}

		public void ReIndexStore()
		{
			this.indexStore.ReIndex();
			this.LastIndexedBlock = this.FindLastIndexedBlock();
		}

		public bool InAnyTip(uint256 hash)
		{
			// check if a block hash exists is any of the tips
			if (this.Contains(hash))
				return true;

			if (this.AlternateTips.ContainsKey(hash))
				return true;

			return false;
		}

		public ChainedBlock GetAnyTip(uint256 hash)
		{
			// check if a block hash exists is any of the tips
			var chainedBlock = this.GetBlock(hash);
			if (chainedBlock != null)
				return chainedBlock;

			if (this.AlternateTips.TryGetValue(hash, out chainedBlock))
				return chainedBlock;

			return null;
		}

		public bool SetLongestTip(ChainedBlock chainedBlock)
		{
			// assume for now this is only called form one thread

			var tipChanged = false;

			// check and set the tip, else push to pending blocks
			if (chainedBlock.ChainWork > this.Tip.ChainWork)
			{
				// set new tip
				var oldTip = this.SetTip(chainedBlock);

				if (chainedBlock.Previous.HashBlock != oldTip.HashBlock)
				{
					// a different chain with more trust is found
					// add the old chain to alt tips.
					this.AlternateTips.TryAdd(oldTip.HashBlock, oldTip);
				}

				tipChanged = true;
			}
			else
			{
				// add the new block to the optional alternative tips
				if (!this.AlternateTips.TryAdd(chainedBlock.HashBlock, chainedBlock))
					throw new InvalidBlockException(); //this should never happen
			}
			
			// remove old tips
			foreach (var alternateTip in this.AlternateTips.Values)
				if (this.Height - alternateTip.Height > 200)
					this.AlternateTips.Remove(alternateTip.HashBlock);
			
			return tipChanged;
		}

		public void AddToBlockStore(Block block, ChainedBlock chainedBlock)
		{
			this.TransactionIndex.Add(block);
			this.indexStore.Put(new StakeBlock {Block = block, Stake = block.Header.PosParameters});
			this.blockMemoryStore.Add(block, chainedBlock.HashBlock);
			this.LastIndexedBlock = chainedBlock;
		}

		public Block GetFullBlock(uint256 blockId)
		{
			if (blockId == null)
				throw new ArgumentException(nameof(blockId));

			return this.blockMemoryStore.Get(blockId, () => this.indexStore.Get(blockId)?.Block);
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
			if (blockId == null)
				return Task.FromResult((Transaction)null);
			var block = this.GetFullBlock(blockId);
			return Task.FromResult(block.Transactions.First(trx => trx.GetHash() == txId));
		}

		public Task PutAsync(uint256 txId, Transaction tx)
		{
			throw new NotImplementedException();
		}

		private readonly object setTipLocker = new object();
		public AutoResetEvent TipChangedSignal = new AutoResetEvent(false);

		public override ChainedBlock SetTip(ChainedBlock block)
		{
			// a double lock on the SetTip method is not great but
			// is needed if the tip is set by more then one node (thread)
			lock (setTipLocker)
			{
				var oldTip = base.SetTip(block);

				if (this.LastIndexedBlock != null)
				{
					var pindex = this.Tip.FindAncestorOrSelf(this.LastIndexedBlock.HashBlock);
					if (pindex == null)
					{
						// a reorg may have occurred we need to find
						// and set the last index tip 
						this.LastIndexedBlock = this.FindLastIndexedBlock();
					}
				}

				// signal to the block syncer that a new
				// tip was set so the blocks will update
				TipChangedSignal.Set();

				return oldTip;
			}
		}
	}
}