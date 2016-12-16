using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class StartupIndexModule : StartupModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;

		public StartupIndexModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<StartupIndexModule>();
		}

		public override int Priority => 11;

		public override void Execute()
		{
			// index the sore
			this.logger.LogInformation("ReIndex block store...");
			this.ChainIndex.ReIndexStore();

			// load transaction indexes
			this.logger.LogInformation("Load transaction index store...");
			this.ChainIndex.TransactionIndex.Load();
			this.logger.LogInformation("ReIndex transaction store...");
			this.ChainIndex.TransactionIndex.ReIndex(this.ChainIndex);
		}
	}
}