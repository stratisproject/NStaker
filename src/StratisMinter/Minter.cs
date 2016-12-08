using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol.Behaviors;
using StratisMinter.Handlers;

namespace StratisMinter
{
    public class Minter
    {
	    private Context context;
	    private BlockHandler blockHandler;
		private ChainHandler chainHandler;

		public void Run(Config config)
	    {
			// create the context
			this.context = Context.Create(Network.Main, config);
			this.blockHandler = new BlockHandler(context);
			this.chainHandler = new ChainHandler(this.context);

			Logger.Create(context).Run();

			// load network addresses from file or from network
			this.context.LoadAddressManager();

			// load headers
			this.chainHandler.LoadHeaders();
			
			// sync the blockchain
			this.blockHandler.DownloadChain();

			// connect to some nodes 

			// start mineing 

			this.context.CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(12));
		    this.context.CancellationTokenSource.Token.WaitHandle.WaitOne(100000);
	    }

	    private static void CreateBehaviours(Context context)
	    {
		    context.ConnectionParameters.TemplateBehaviors.Add(new ChainBehavior(context.ChainIndex));
	    }
	}
}
