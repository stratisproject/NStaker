using NBitcoin.Protocol.Behaviors;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Services;

namespace StratisMinter.Modules
{
	public class StartupBehaviouorsModule : StartupModule
	{
		private readonly BlockReceiver blockReceiver;

		public StartupBehaviouorsModule(Context context, BlockReceiver blockReceiver) : base(context)
		{
			this.blockReceiver = blockReceiver;
		}

		public override int Priority => 8;

		public override void Execute()
		{
			// register a behaviour, the ChainBehavior maintains 
			// the chain of headers in sync with the network
			// before we loaded the headers don't sync the chain
			var chainBehavior = new ChainBehavior(this.Context.ChainIndex) { CanSync = false, CanRespondToGetHeaders = true};
			this.Context.ConnectionParameters.TemplateBehaviors.Add(chainBehavior);

			var blockSyncBehaviour = new BlockSyncBehaviour(this.blockReceiver.BlockSyncHub)
			{
				CanRespondToBlockPayload = false,
				CanRespondToGetBlocksPayload = false,
				CanRespondToInvPayload = false,
				CanRespondToHeadersPayload = false
			};
			this.Context.ConnectionParameters.TemplateBehaviors.Add(blockSyncBehaviour);
		}
	}
}