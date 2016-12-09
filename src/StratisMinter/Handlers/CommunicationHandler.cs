using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;

namespace StratisMinter.Handlers
{
	public class CommunicationHandlerException : Exception
	{
		public CommunicationHandlerException()
		{
		}

		public CommunicationHandlerException(string message) : base(message)
		{
		}
	}

	public class CommunicationHandler : Handler
	{
		private readonly Context context;
	    private readonly NodesCollection trustedNodes;

		public CommunicationHandler(Context context)
		{
			this.context = context;
			this.trustedNodes = new NodesCollection();
		}

	    public Node GetNode(bool trusetd = false)
	    {
			// this will later be replaced with a look up in the NodesGroup
			// the nodes group is in charge of maintaining open connections

			if (this.trustedNodes.Any())
		    {
			    return this.trustedNodes.First();
		    }

			var used = new List<IPEndPoint>();
			while (true)
			{
				context.CancellationToken.ThrowIfCancellationRequested();

				try
				{
					IPEndPoint endpoint = null;
					// if we have trusted nodes use one of those, else
					// select a random address from the address manager
					if (this.context.Config.TrustedNodes?.Any() ?? false)
					{
						endpoint = this.context.Config.TrustedNodes.FirstOrDefault(n => !used.Contains(n));
					}

					if (trusetd && endpoint == null)
						throw new CommunicationHandlerException("no more trusted nodes");

					if (endpoint == null)
						endpoint = this.context.AddressManager.Select().Endpoint;
					
					if (used.Contains(endpoint))
						continue;

					used.Add(endpoint);

					var node = Node.Connect(this.context.Network, endpoint);
					node.VersionHandshake(null, context.CancellationToken);
					this.trustedNodes.Add(node);
					return node;
				}
				catch (OperationCanceledException tokenCanceledException)
				{
					tokenCanceledException.CancellationToken.ThrowIfCancellationRequested();
				}
				catch (ProtocolException protocol)
				{
					// continue to try with another node
				}
				catch (Exception ex)
				{
					// try another node
					ex.ThrowIfCritical();
				}
			}
		}

	}
}
