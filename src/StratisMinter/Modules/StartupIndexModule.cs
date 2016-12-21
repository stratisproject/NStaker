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
		private readonly LogFilter logFilter;

		public StartupIndexModule(Context context,ILoggerFactory loggerFactory, LogFilter logFilter) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.logFilter = logFilter;
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

			this.logFilter.Log = false;
		}
	}
}