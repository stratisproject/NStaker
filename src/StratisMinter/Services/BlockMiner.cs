using System;
using nStratis;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
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

		private Block CreateNewBlock()
		{
			return null;
		}
	}
}