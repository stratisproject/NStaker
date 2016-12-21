using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class StartupConnectionModule : StartupModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly LogFilter logFilter;

		public StartupConnectionModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory, LogFilter logFilter) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logFilter = logFilter;
			this.logger = loggerFactory.CreateLogger<StartupConnectionModule>();
		}

		public override int Priority => 9;

		public override void Execute()
		{
			// sync the headers and save to disk
			this.logger.LogInformation("Start connecting to peers...");
			this.nodeConnectionService.StartConnecting();
		}
	}
}