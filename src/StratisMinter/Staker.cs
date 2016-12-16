using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol.Behaviors;
using StratisMinter.Behaviour;
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
				.AddSingleton(this.context).AddSingleton<IStoppable, Context>()
				.AddSingleton<NodeConnectionService>().AddSingleton<IStoppable, NodeConnectionService>()
				.AddSingleton<ChainSyncService>().AddSingleton<IStoppable, ChainSyncService>()
				.AddSingleton<DownloadManager>().AddSingleton<IStoppable, DownloadManager>()
				.AddSingleton<BlockSyncService>()
				.AddSingleton<Logger>()
				.BuildServiceProvider();

		    this.context.Service = services;

			//configure console logging
			this.services
				.GetService<ILoggerFactory>()
				.AddConsole(LogLevel.Information);
		}

	    private void CreateBehaviours()
	    {
			// register a behaviour, the ChainBehavior maintains 
			// the chain of headers in sync with the network
			// before we loaded the headers don't sync the chain
			var chainBehavior = new ChainBehavior(this.context.ChainIndex) { CanSync = false };
			this.context.ConnectionParameters.TemplateBehaviors.Add(chainBehavior);

			var blockSyncBehaviour = new BlockSyncBehaviour(this.services.GetService<BlockSyncService>().BlockSyncHub)
			{
				CanRespondToBlockPayload = false,
				CanRespondToGetBlocksPayload = false
			};
			this.context.ConnectionParameters.TemplateBehaviors.Add(blockSyncBehaviour);
		}

		public void Run(Config config)
	    {
			// todo: this entire processes will be replaced with LoadModuls and WorkTasks

			// create the context
			this.context = Context.Create(Network.Main, config);
			this.BuildServices();
			
			// load network addresses from file or from network
			this.context.LoadAddressManager();

			this.CreateBehaviours();

			// load headers
			this.services.GetService<ChainSyncService>().LoadHeaders();

			this.services.GetService<Logger>().Run();

			// sync the blockchain
			this.services.GetService<BlockSyncService>().DownloadOrCatchup();

			// connect to some nodes 
			this.services.GetService<NodeConnectionService>().StartConnecting();

			// enable sync on the behaviours 
			this.services.GetService<NodeConnectionService>().EnableSyncing();

			this.services.GetService<BlockSyncService>().StartReceiving();

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
			foreach (var terminate in this.services.GetServices<IStoppable>())
				terminate.OnStop();
		}
	}
}
