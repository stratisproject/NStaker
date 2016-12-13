using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol.Behaviors;
using StratisMinter.Handlers;

namespace StratisMinter
{
    public class Staker
    {
	    private Context context;
	    private BlockHandler blockHandler;
		private ChainHandler chainHandler;
		private ConnectionHandler connectionHandler;
		private DownloadHandler downloadHandler;

		private void CreateHandlers()
	    {
			this.connectionHandler = new ConnectionHandler(this.context);
			this.chainHandler = new ChainHandler(this.context, this.connectionHandler);
			this.downloadHandler = new DownloadHandler(context, this.connectionHandler, this.chainHandler);
			this.blockHandler = new BlockHandler(this.context, this.connectionHandler, this.downloadHandler, this.chainHandler);
			
			// add the handlers to the handler collection
			// this will allow to abstract away the functionality 
			// in handlers for example when terminating each handler 
			// will be called to manage its own termination work
			context.Hanldlers.Add(this.connectionHandler);
			context.Hanldlers.Add(this.blockHandler);
			context.Hanldlers.Add(this.chainHandler);
			context.Hanldlers.Add(this.downloadHandler);
		}

		public void Run(Config config)
	    {
			// create the context
			this.context = Context.Create(Network.Main, config);
			this.CreateHandlers();
			this.connectionHandler.CreateBehaviours();

			Logger.Create(context).Run();

			// load network addresses from file or from network
			this.context.LoadAddressManager();

			// load headers
			this.chainHandler.LoadHeaders();
			
			// sync the blockchain
			this.downloadHandler.DownloadOrCatchup();

			// connect to some nodes 
			this.connectionHandler.StartConnecting();

			// start mining 
			this.blockHandler.Stake();

			this.context.CancellationTokenSource.CancelAfter(TimeSpan.FromDays(1));
		    while (!this.context.CancellationTokenSource.IsCancellationRequested)
		    {
				this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
			}

			// call every handler to dispose itself
			this.connectionHandler.Dispose();
	    }
	}
}
