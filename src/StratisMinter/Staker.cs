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
				.AddSingleton<StartupModule, StartupCalcPosModule>()
				.AddSingleton<StartupModule, StartupChainModule>()
				.AddSingleton<StartupModule, StartupIndexModule>()
				// BackgroundWorkItem
				.AddSingleton<NodeConnectionService>().AddSingleton<BackgroundWorkItem, NodeConnectionService>(provider => provider.GetService<NodeConnectionService>())
				.AddSingleton<BlockReceiver>().AddSingleton<BackgroundWorkItem, BlockReceiver>(provider => provider.GetService<BlockReceiver>())
				.AddSingleton<BlockSender>().AddSingleton<BackgroundWorkItem, BlockSender>(provider => provider.GetService<BlockSender>())
				.AddSingleton<BlockMiner>().AddSingleton<BackgroundWorkItem, BlockMiner>(provider => provider.GetService<BlockMiner>())
				.AddSingleton<Logger>().AddSingleton<BackgroundWorkItem, Logger>(provider => provider.GetService<Logger>())
				.AddSingleton<LoggerKeyReader>().AddSingleton<BackgroundWorkItem, LoggerKeyReader>(provider => provider.GetService<LoggerKeyReader>())
				// BlockingWorkItem
				.AddSingleton<DownloadWorker>().AddSingleton<BlockingWorkItem, DownloadWorker>(provider => provider.GetService<DownloadWorker>())
				// standalone types
				.AddSingleton<ChainService>()
				.AddSingleton<BlockSyncHub>()
				.AddSingleton<DownloadManager>()
				.AddSingleton<ChainIndex>()
				.AddSingleton<LogFilter>()
				// build
				.BuildServiceProvider();

			this.context.Service = services;

			//configure console logging
		    this.services
			    .GetService<ILoggerFactory>()
			    .AddConsole(this.services.GetService<LogFilter>().Filter, false);
	    }

		public void Run(Config config)
	    {
			// create the context
			this.context = Context.Create(Network.Main, config);
			this.BuildServices();
			
		    foreach (var service in this.services.GetServices<StartupModule>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<BlockingWorkItem>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<BackgroundWorkItem>().OrderBy(o => o.Priority))
				service.Execute();

			this.context.CancellationTokenSource.CancelAfter(TimeSpanExtention.Infinite);
		    while (!this.context.CancellationTokenSource.IsCancellationRequested)
		    {
				this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
			}

			foreach (var service in this.services.GetServices<ShutdownModule>().OrderBy(o => o.Priority))
				service.Execute();

			foreach (var service in this.services.GetServices<IDisposable>())
				service.Dispose();
		}
	}
}
