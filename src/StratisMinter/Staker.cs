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
		private CommunicationHandler comHandler;

	    private void CreateHandlers()
	    {
			this.comHandler = new CommunicationHandler(this.context);
			this.chainHandler = new ChainHandler(this.context, this.comHandler);
			this.blockHandler = new BlockHandler(this.context, this.comHandler, this.chainHandler);

			// add the handlers to the handler collection
			// this will allow to abstract away the functionality 
			// in handlers for example when terminating each handler 
			// will be called to manage its own termination work
			context.Hanldlers.Add(this.comHandler);
			context.Hanldlers.Add(this.blockHandler);
			context.Hanldlers.Add(this.chainHandler);
		}

		public void Run(Config config)
	    {
			// create the context
			this.context = Context.Create(Network.Main, config);
			this.CreateHandlers();

			Logger.Create(context).Run();

			// load network addresses from file or from network
			this.context.LoadAddressManager();

			// load headers
			this.chainHandler.LoadHeaders();
			
			// sync the blockchain
			this.blockHandler.DownloadChain();

			// connect to some nodes 

			// start mining 

			this.context.CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(12));
		    this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromDays(1));
	    }

	    private static void CreateBehaviours(Context context)
	    {
		    context.ConnectionParameters.TemplateBehaviors.Add(new ChainBehavior(context.ChainIndex));
	    }
	}
}
