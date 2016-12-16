using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;
using StratisMinter.Services;

namespace StratisMinter.Store
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
			while (pindex.Previous != null && !pindex.Header.PosParameters.IsSet())
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

			return BlockValidator.CheckAndComputeStake(this, this, this, this, chainedBlock, block);
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
			// its probably wise to lock this operation
			// in case another thread kicks in while copying POS params
			var oldTip = base.SetTip(block);

			// when many nodes are modifying the chain
			// the POS parameters are getting overridden
			// that because the headers don't have the 
			// POS parameters from the network but they are
			// calculated when validating a block

			if (this.LastIndexedBlock != null)
			{
				var tipdindex = this.LastIndexedBlock;
				var pindex = this.Tip.FindAncestorOrSelf(this.LastIndexedBlock.HashBlock);

				// iterate over old POS params and copy
				//  over to the new instances on the chain
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
}