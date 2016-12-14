using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;
using StratisMinter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StratisMinter
{
	public class Context : IStopable
	{
		public static Context Create(Network network, Config config)
		{
			var cancellationTokenSource = new CancellationTokenSource();
			var context = new Context()
			{
				Network = network,
				Config = config,
				CancellationTokenSource = cancellationTokenSource,
				ConnectionParameters = new NodeConnectionParameters(),
				ChainIndex = new ChainIndex(),
				Counter = new PerformanceCounter(),
			};

			// override the connection cancelation token
			context.ConnectionParameters.ConnectCancellation = context.CancellationToken;
			//context.ChainIndex.Load(context);
			return context;
		}

		public Network Network { get; private set; }
		public Config Config { get; private set; }
		public AddressManager AddressManager { get; private set; }
		public CancellationToken CancellationToken => this.CancellationTokenSource.Token;
		public CancellationTokenSource CancellationTokenSource { get; private set; }
		public NodeConnectionParameters ConnectionParameters { get; private set; }
		public ChainIndex ChainIndex { get; private set; }
		public PerformanceCounter Counter { get; private set; }
		public IServiceProvider Service { get; set; }

		public override string ToString()
		{
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("==== Perf ====");
			builder.AppendLine($"Elapsed = \t\t {Counter.Elapsed:c}");
			builder.AppendLine($"CurrentBlock = \t\t {Counter.BlockCount}");
			builder.AppendLine($"PendingBlocks = \t\t {Counter.PendingBlocks}");
			builder.AppendLine($"Blocks = \t\t {(Counter.Elapsed.TotalMilliseconds/Counter.BlockCount):0.0000} ms/block");
			builder.AppendLine("==== Stats ====");
			builder.AppendLine($"ConnectedNodes = \t\t {this.Service.GetService<NodeConnectionService>().NodesGroup.ConnectedNodes.Count}");
			builder.AppendLine($"HeaderTip = \t\t {this.ChainIndex?.Tip?.Height}");
			builder.AppendLine($"IndexedBlock = \t\t {this.ChainIndex?.LastIndexedBlock?.Height}");
			return builder.ToString();
		}

	

		public void OnStop()
		{
			this.AddressManager.SavePeerFile(this.Config.File("peers.dat"), this.Network);
		}

		public void LoadAddressManager()
		{
			if (File.Exists(this.Config.File("peers.dat")))
			{
				this.AddressManager = AddressManager.LoadPeerFile(this.Config.File("peers.dat"), this.Network);
				return;
			}

			// the ppers file is empty so we load new peers
			// peers are then saved to peer.dat file so next time load is faster
			this.AddressManager = new AddressManager();
			NodeConnectionParameters parameters = new NodeConnectionParameters();
			parameters.TemplateBehaviors.Add(new AddressManagerBehavior(this.AddressManager));

			// when the node connects new addresses are discovered
			using (var node = Node.Connect(Network.Main, parameters))
			{
				node.VersionHandshake(this.CancellationToken);
			}

			this.AddressManager.SavePeerFile(this.Config.File("peers.dat"), this.Network);
		}
	}
}
