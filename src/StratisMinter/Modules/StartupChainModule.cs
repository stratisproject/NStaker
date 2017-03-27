using System.IO;
using Microsoft.Extensions.Logging;
using NBitcoin;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class StartupChainModule : StartupModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly ChainService chainSyncService;
		private readonly NodeConnectionService nodeConnectionService;

		public StartupChainModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<StartupChainModule>();
		}

		public override int Priority => 10;

		public override void Execute()
		{
			// load headers form file (or genesis)
			if (File.Exists(this.Context.Config.File("headers.dat")))
			{
				this.logger.LogInformation("Loading headers form disk...");
				this.ChainIndex.Load(File.ReadAllBytes(this.Context.Config.File("headers.dat")));
			}
			else
			{
				this.logger.LogInformation("Loading headers no file found...");
				var genesis = this.Context.Network.GetGenesis();
				this.ChainIndex.SetTip(new ChainedBlock(genesis.Header, 0));
				// validate the block to generate the pos params
				BlockValidator.CheckAndComputeStake(this.ChainIndex, this.ChainIndex, this.ChainIndex, this.ChainIndex,
					this.ChainIndex.Tip, genesis);
				//this.ChainIndex.ValidateBlock(genesis);
			}

			// load the index chain this will 
			// add each block index to memory for fast lookup
			this.ChainIndex.Initialize(this.Context);

			// sync the headers chain
			this.logger.LogInformation("Sync chain headers with network...");
			this.chainSyncService.SyncChain();

			// persist the chain
			this.logger.LogInformation("Persist headers...");
			this.chainSyncService.SaveToDisk();
		}
	}
}