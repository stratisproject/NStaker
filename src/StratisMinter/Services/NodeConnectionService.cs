using System;
using System.Linq;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;
using StratisMinter.Base;
using StratisMinter.Behaviour;

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

	public class NodeConnectionService : BackgroundWorkItem
	{
		// the nodes group maintains open connections with other
		// nodes in the network up to a count, if nodes get disconnected 
		// the NodesGroup will try search and connect to new nodes
		// when a node is found a behaviour is created that is in charge 
		// of notifying the parent (NodesGroup) when the node got disconnected 
		public NodesGroup NodesGroup { get; }

		public NodeConnectionService(Context context) : base(context)
		{
			this.NodesGroup = new NodesGroup(this.Context.Network);

			// some config settings
			this.NodesGroup.MaximumNodeConnection = this.Context.Config.MaximumNodeConnection;

			// set the connection parameters
			this.NodesGroup.NodeConnectionParameters = this.Context.ConnectionParameters;
		}

		public void OnStop()
		{
			this.NodesGroup.Disconnect();
		}

		public void EnableSyncing()
		{
			// enable sync on the chain behaviours
			// this will keep the chain headers in 
			// sync with the network 

			foreach (var behavior in this.Context.ConnectionParameters.TemplateBehaviors.OfType<ChainBehavior>())
				behavior.CanSync = true;
			
			foreach (var node in this.NodesGroup.ConnectedNodes)
				foreach (var behavior in node.Behaviors.OfType<ChainBehavior>())
					behavior.CanSync = true;

			foreach (var behavior in this.Context.ConnectionParameters.TemplateBehaviors.OfType<BlockSyncBehaviour>())
			{
				behavior.CanRespondToBlockPayload = true;
				behavior.CanRespondToInvPayload = true;
			}

			foreach (var node in this.NodesGroup.ConnectedNodes)
				foreach (var behavior in node.Behaviors.OfType<BlockSyncBehaviour>())
				{
					behavior.CanRespondToBlockPayload = true;
					behavior.CanRespondToInvPayload = true;
				}
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

			if (this.Context.Config.TrustedNodes.Empty())
				throw new NoConnectedNodesException("no trusted nodes");

			foreach (var endPoint in this.Context.Config.TrustedNodes)
			{
				var node = this.NodesGroup.TryConnectNode(endPoint);
				if (node != null)
					return node;
			}

			throw new NoConnectedNodesException("no trusted nodes");
		}

		protected override void Work()
		{
			this.EnableSyncing();
			this.StartConnecting();
			this.Cancellation.Token.WaitHandle.WaitOne(TimeSpanExtention.Infinite);
		}
	}
}
