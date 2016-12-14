using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol.Behaviors;
using StratisMinter.Services;

namespace StratisMinter
{
    public class Staker: IDisposable
    {
	    private Context context;
	    private IServiceProvider services;

		private void BuildServices()
	    {
			//setup our DI
			this.services = new ServiceCollection()
				.AddLogging()
				.AddSingleton(this.context).AddSingleton<IStopable, Context>()
				.AddSingleton<NodeConnectionService>().AddSingleton<IStopable, NodeConnectionService>()
				.AddSingleton<ChainSyncService>().AddSingleton<IStopable, ChainSyncService>()
				.AddSingleton<DownloadManager>().AddSingleton<IStopable, DownloadManager>()
				.AddSingleton<BlockSyncService>()
				.AddSingleton<Logger>()
				.BuildServiceProvider();

		    this.context.Service = services;

			//configure console logging
			this.services
				.GetService<ILoggerFactory>()
				.AddConsole(LogLevel.Information);
		}

		public void Run(Config config)
	    {
			// create the context
			this.context = Context.Create(Network.Main, config);
			this.BuildServices();

			this.services.GetService<NodeConnectionService>().CreateBehaviours();

			// load network addresses from file or from network
			this.context.LoadAddressManager();

			// load headers
			this.services.GetService<ChainSyncService>().LoadHeaders();

			this.services.GetService<Logger>().Run();

			// sync the blockchain
			this.services.GetService<BlockSyncService>().DownloadOrCatchup();

			// connect to some nodes 
			this.services.GetService<NodeConnectionService>().StartConnecting();

			// start mining 
			this.services.GetService<BlockSyncService>().Stake();

			this.context.CancellationTokenSource.CancelAfter(TimeSpan.FromDays(1));
		    while (!this.context.CancellationTokenSource.IsCancellationRequested)
		    {
				this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
			}
			
			this.Dispose();
	    }

		public void Dispose()
		{
			// call every service to dispose itself
			foreach (var terminate in this.services.GetServices<IStopable>())
				terminate.OnStop();
		}
	}
}
