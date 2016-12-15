using System;
using System.Linq;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;

namespace StratisMinter.Services
{
	public class NoConnectedNodesException : Exception
	{
		public NoConnectedNodesException()
		{
		}

		public NoConnectedNodesException(string message) : base(message)
		{
		}
	}

	public class NodeConnectionService : IStoppable
	{
		private readonly Context context;

		// the nodes group maintains open connections with other
		// nodes in the network up to a count, if nodes get disconnected 
		// the NodesGroup will try search and connect to new nodes
		// when a node is found a behaviour is created that is in charge 
		// of notifying the parent (NodesGroup) when the node got disconnected 
		public NodesGroup NodesGroup { get; }

		public NodeConnectionService(Context context)
		{
			this.context = context;
			this.NodesGroup = new NodesGroup(this.context.Network);

			// some config settings
			this.NodesGroup.MaximumNodeConnection = this.context.Config.MaximumNodeConnection;
		}

		public void OnStop()
		{
			this.NodesGroup.Disconnect();
		}

		public NodeConnectionService CreateBehaviours()
		{
			// register a behaviour, the ChainBehavior maintains 
			// the chain of headers in sync with the network
			// before we loaded the headers don't sync the chain
			var behaviour = new ChainBehavior(this.context.ChainIndex) { CanSync = false };
			this.context.ConnectionParameters.TemplateBehaviors.Add(behaviour);

			// set the connection parameters
			this.NodesGroup.NodeConnectionParameters = this.context.ConnectionParameters;

			return this;
		}

		public void EnableHeaderSyncing()
		{
			// enable sync on the chain behaviours
			// this will keep the chain headers in 
			// sync with the network 

			foreach (var behavior in this.context.ConnectionParameters.TemplateBehaviors.OfType<ChainBehavior>())
				behavior.CanSync = true;
			
			foreach (var node in this.NodesGroup.ConnectedNodes)
				foreach (var behavior in node.Behaviors.OfType<ChainBehavior>())
					behavior.CanSync = true;
		}

		public void StartConnecting()
		{
			this.NodesGroup.Connect();
		}

		public Node GetNode(bool trusetd = false)
		{
			if (!trusetd)
			{
				// todo: add some randomness 
				var node  = this.NodesGroup.ConnectedNodes.FirstOrDefault();

				if (node == null)
					throw new NoConnectedNodesException("no connected nodes");
			}

			if (this.context.Config.TrustedNodes.Empty())
				throw new NoConnectedNodesException("no trusted nodes");

			foreach (var endPoint in this.context.Config.TrustedNodes)
			{
				var node = this.NodesGroup.TryConnectNode(endPoint);
				if (node != null)
					return node;
			}

			throw new NoConnectedNodesException("no trusted nodes");
		}
	}
}
