using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using nStratis;
using nStratis.Protocol.Behaviors;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Modules;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter
{
    public class Staker
    {
	    private Context context;
	    private IServiceProvider services;

		private void BuildServices()
	    {
			// this is just horrible replace it with some scanner

			//setup our DI
			this.services = new ServiceCollection()
				.AddLogging()
				.AddSingleton(this.context)
				// StartupModule
				.AddSingleton<ShutdownModule, ShutdowhainChainModule>()
				.AddSingleton<ShutdownModule, ShutdownAddressManagerModule>()
				.AddSingleton<ShutdownModule, ShutdownConnectionsModule>()
				// ShutdownModule
				.AddSingleton<StartupModule, StartupAddressManagerModule>()
				.AddSingleton<StartupModule, StartupBehaviouorsModule>()
				.AddSingleton<StartupModule, StartupConnectionModule>()
				.AddSingleton<StartupModule, StartupChainModule>()
				.AddSingleton<StartupModule, StartupIndexModule>()
				.AddSingleton<StartupModule, StartupWalletModule>()
				// BackgroundWorkItem
				.AddSingleton<NodeConnectionService>().AddSingleton<BackgroundWorkItem, NodeConnectionService>(provider => provider.GetService<NodeConnectionService>())
				.AddSingleton<BlockReceiver>().AddSingleton<BackgroundWorkItem, BlockReceiver>(provider => provider.GetService<BlockReceiver>())
				.AddSingleton<BlockSender>().AddSingleton<BackgroundWorkItem, BlockSender>(provider => provider.GetService<BlockSender>())
				.AddSingleton<BlockMiner>().AddSingleton<BackgroundWorkItem, BlockMiner>(provider => provider.GetService<BlockMiner>())
				.AddSingleton<Logger>().AddSingleton<BackgroundWorkItem, Logger>(provider => provider.GetService<Logger>())
				.AddSingleton<LoggerKeyReader>().AddSingleton<BackgroundWorkItem, LoggerKeyReader>(provider => provider.GetService<LoggerKeyReader>())
				.AddSingleton<ChainService>().AddSingleton<BackgroundWorkItem, ChainService>(provider => provider.GetService<ChainService>())
				.AddSingleton<WalletWorker>().AddSingleton<BackgroundWorkItem, WalletWorker>(provider => provider.GetService<WalletWorker>())
				.AddSingleton<GetDataReceiver>().AddSingleton<BackgroundWorkItem, GetDataReceiver>(provider => provider.GetService<GetDataReceiver>())
				// BlockingWorkItem
				.AddSingleton<DownloadWorker>().AddSingleton<BlockingWorkItem, DownloadWorker>(provider => provider.GetService<DownloadWorker>())
				// standalone types
				.AddSingleton<BlockSyncHub>()
				.AddSingleton<DownloadManager>()
				.AddSingleton<ChainIndex>()
				.AddSingleton<LogFilter>()
                .AddSingleton<WalletService>()
				.AddSingleton<WalletStore>()
				.AddSingleton<MinerService>()

				// build
				.BuildServiceProvider();

			this.context.Service = services;

			//configure console logging
		    this.services
			    .GetService<ILoggerFactory>()
			    .AddConsole(this.services.GetService<LogFilter>().Filter, false);
	    }

	    public static Staker Build(Config config)
	    {
			// create the context
			var staker = new Staker();
			staker.context = Context.Create(Network.Main, config);
			staker.BuildServices();
		    return staker;
	    }

		public void Run()
	    {
			//start the logger 
			this.services.GetService<Logger>().Execute();

			foreach (var service in this.services.GetServices<StartupModule>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<BlockingWorkItem>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<BackgroundWorkItem>().OrderBy(o => o.Priority))
				service.Execute();

			// block until the cancelation is signalled
		    this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpanExtention.Infinite);

			foreach (var service in this.services.GetServices<ShutdownModule>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<IDisposable>())
				service.Dispose();

		    if (this.context.GeneralException != null)
		    {
			    Console.WriteLine(this.context.GeneralException);
			    Console.ReadKey();
		    }
		}
	}
}
