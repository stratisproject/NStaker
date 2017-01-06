using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using nStratis.Protocol;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class BlockSender : BackgroundWorkItem
	{
		public BlockSyncHub BlockSyncHub { get; }

		public BlockSender(Context context, BlockSyncHub blockSyncHub) : base(context)
		{
			this.BlockSyncHub = blockSyncHub;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				// this method blocks
				this.WaitForDownLoadMode();

				// take from the blocking collection 
				var broadcastItem = this.BlockSyncHub.BroadcastItems.Take(this.Cancellation.Token);

				// if no behaviours are found we wait for behaviours
				// this is so we don't lose the block
				while (this.BlockSyncHub.Behaviours.IsEmpty)
					this.Context.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));

				// check if the behaviour is not the one that 
				// queue the block, in that case we don't broadcast back. 
				foreach (var behaviour in this.BlockSyncHub.Behaviours)
				{
					if (!(behaviour.Value.State == NodeState.HandShaked || behaviour.Value.State == NodeState.Connected))
						continue;

					// node exists and is equal to current don't broadcast to it
					if (broadcastItem.Node?.Equals(behaviour.Value) ?? false)
						continue;

					behaviour.Value.SendMessage(broadcastItem.Payload, this.Cancellation.Token);
				}
			}
		}
	}
}