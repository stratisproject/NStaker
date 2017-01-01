using System.Linq;
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
		private readonly WalletStore walletStore;

		public ShutdowhainChainModule(Context context, ChainService chainSyncService, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory,WalletStore walletStore) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.nodeConnectionService = nodeConnectionService;
			this.walletStore = walletStore;
			this.logger = loggerFactory.CreateLogger<ShutdowhainChainModule>();
		}

		public override int Priority => 10;

		public override void Execute()
		{
			// temp don't save to disk on shout down
			//this.chainSyncService.SaveToDisk();

			if (this.walletStore.Wallet.WalletsList.Any())
				this.walletStore.Save();
		}
	}
}