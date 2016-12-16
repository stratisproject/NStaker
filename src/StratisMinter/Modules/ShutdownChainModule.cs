using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class ShutdowhainChainModule : ShutdownModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;

		public ShutdowhainChainModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ShutdowhainChainModule>();
		}

		public override int Priority => 10;

		public override void Execute()
		{
			this.chainSyncService.SaveToDisk();
		}
	}
}