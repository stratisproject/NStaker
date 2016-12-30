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
		public List<ChainedBlock> AlternateTips { get; private set; }

		public void Initialize(Context context)
		{
			this.context = context;
			this.store = new StakeBlockStore(this.context.Config.FolderLocation, this.context.Network);
			this.indexStore = new IndexedStakeBlockStore(new InMemoryNoSqlRepository(), store);
			this.TransactionIndex = new TransactionToBlockItemIndex(this.context);
			this.blockMemoryStore = new BlockMemoryStore(this.context);
			this.AlternateTips = new List<ChainedBlock>();
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

			foreach (var alternateTip in this.AlternateTips)
			{
				if (alternateTip.FindAncestorOrSelf(hash) != null)
					return true;
			}

			return false;
		}

		public bool SetLongestTip(ChainedBlock chainedBlock)
		{
			// assume for now this is only called form one thread

			// assume the tip is the longest
			if (chainedBlock.Previous.HashBlock == this.Tip.HashBlock)
			{
				this.SetTip(chainedBlock);
				return true;
			}
		
			// first add the new block to the chains
			var prev = this.AlternateTips.FirstOrDefault(t => chainedBlock.Previous.HashBlock == t.HashBlock);

			if (prev == null)
			{
				// a new chain?
				this.AlternateTips.Add(chainedBlock);
			}
			else
			{
				// append to an existing chain
				this.AlternateTips.Remove(prev);
				this.AlternateTips.Add(chainedBlock);
			}

			//return the longest block or the tip if its longer
			var longest = this.AlternateTips.OrderByDescending(o => o.Height).FirstOrDefault();
			longest = longest.Height > this.Tip.Height ? longest : this.Tip;

			//todo: use block trust instead of height
			// check and set the tip, else push to pending blocks
			if (longest.Height > this.Tip.Height)
			{
				// set new tip and persist the block
				var oldTip = this.SetTip(longest);

				if (oldTip.HashBlock != longest.Previous.HashBlock)
				{
					// push the old tip to a possible alternate chain
					if (!this.AlternateTips.Remove(chainedBlock))
						throw new InvalidBlockException(); // this should not happen
					this.AlternateTips.Add(oldTip);
				}

				return true;
			}

			return false;
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