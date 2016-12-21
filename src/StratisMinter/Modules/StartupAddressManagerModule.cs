using System.IO;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;
using StratisMinter.Base;

namespace StratisMinter.Modules
{
	public class StartupAddressManagerModule : StartupModule
	{
		private ILogger logger;

		public StartupAddressManagerModule(Context context, ILoggerFactory loggerFactory) : base(context)
		{
			this.logger = loggerFactory.CreateLogger<StartupAddressManagerModule>();

		}

		public override int Priority => 7;

		public override void Execute()
		{
			if (File.Exists(this.Context.Config.File("peers.dat")))
			{
				this.logger.LogInformation("Loading address manager from disk...");
				this.Context.AddressManager = AddressManager.LoadPeerFile(this.Context.Config.File("peers.dat"), this.Context.Network);
				return;
			}

			this.logger.LogInformation("Downloading addresses from peers...");

			// the ppers file is empty so we load new peers
			// peers are then saved to peer.dat file so next time load is faster
			this.Context.AddressManager = new AddressManager();
			NodeConnectionParameters parameters = new NodeConnectionParameters();
			parameters.TemplateBehaviors.Add(new AddressManagerBehavior(this.Context.AddressManager));

			// when the node connects new addresses are discovered
			using (var node = Node.Connect(Network.Main, parameters))
			{
				node.VersionHandshake(this.Context.CancellationToken);
			}

			this.Context.AddressManager.SavePeerFile(this.Context.Config.File("peers.dat"), this.Context.Network);
		}
	}
}