using System;
using System.Linq;
using nStratis;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class MinedBlockException : Exception
	{
		public MinedBlockException()
		{
		}

		public MinedBlockException(string message) : base(message)
		{
		}
	}

	public class BlockMiner : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainService chainSyncService;
		private readonly ChainIndex chainIndex;

		public BlockSyncHub BlockSyncHub { get; }

		public BlockMiner(Context context, NodeConnectionService nodeConnectionService,
			BlockSyncHub blockSyncHub, ChainService chainSyncService) : base(context)
		{
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.BlockSyncHub = blockSyncHub;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
			}
		}

		private void CheckState(Block block)
		{
			
		}

		private void CheckWork(Block block)
		{
			
		}

		private void IncrementExtraNonce()
		{
			
		}

		private Block CreateNewBlock(BitcoinSecret key, bool proofOfStake, long fee)
		{
			// create the block 
			var block = new Block();

			var pindexPrev = this.chainIndex.Tip;
			var height = pindexPrev.Height + 1;

			// Create coinbase tx
			var txNew = new Transaction();
			txNew.AddInput(new TxIn());
			txNew.AddOutput(new TxOut());

			if (!proofOfStake)
			{
				throw new MinedBlockException("Only POS transactions supported");
			}
			else
			{
				// Height first in coinbase required for block.version=2
				txNew.Inputs[0].ScriptSig = new Script(); //(CScript() << nHeight) + COINBASE_FLAGS;
				if (!(txNew.Inputs[0].ScriptSig.Length <= 100))
					throw new MinedBlockException();
			}

			// Add our coinbase tx as first transaction
			block.AddTransaction(txNew);

			return null;
		}
	}
}