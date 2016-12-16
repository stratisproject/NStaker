using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class StartupCalcPosModule : StartupModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;

		public StartupCalcPosModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<StartupCalcPosModule>();
		}

		public override int Priority => 12;

		public override void Execute()
		{
			// recalculate any pos parameters that
			// may have bene missed
			this.logger.LogInformation("Catching up with POS calculations...");
			this.ChainIndex.PosCatchup();

			// sync the headers and save to disk
			this.logger.LogInformation("Save ChainHeaders to disk...");
			this.chainSyncService.SaveToDisk();
		}
	}
}