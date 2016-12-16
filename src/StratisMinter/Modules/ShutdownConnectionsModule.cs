using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class ShutdownConnectionsModule : ShutdownModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;

		public ShutdownConnectionsModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ShutdownConnectionsModule>();
		}

		public override int Priority => 10;

		public override void Execute()
		{
			this.nodeConnectionService.OnStop();
		}
	}
}