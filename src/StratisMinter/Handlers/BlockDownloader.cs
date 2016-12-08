using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;

namespace StratisMinter.Handlers
{
	public class BlockFetcher
	{
		private readonly BlockDownloader downloader;
		private readonly Node node;
		private readonly Context context;

		public BlockFetcher(Context context, BlockDownloader downloader, Node node)
		{
			this.context = context;
			this.downloader = downloader;
			this.node = node;
			node.StateChanged += NodeOnStateChanged;
		}

		public void Fetch(uint256[] getblocks)
		{
			// todo: creating tasks randonly is not a good idea
			// this needs to change where a sort of queue using a 
			// single task reading form the queue and getting blocks
			Task.Run(() =>
			{
				foreach (var block in node.GetBlocks(getblocks, context.CancellationToken))
				{
					this.downloader.PushBlock(block);
				}

			}, context.CancellationToken);
		}

		private void NodeOnStateChanged(Node nodeParam, NodeState oldState)
		{
			if (nodeParam.State != NodeState.Connected || node.State != NodeState.HandShaked)
			{
				this.downloader.Fetchers.TryRemove(this, out nodeParam);
			}
		}
	}

	public class BlockDownloader
	{
		private readonly Context context;
		public ConcurrentDictionary<BlockFetcher, Node> Fetchers { get; }
		public ConcurrentDictionary<uint256, Block> ReceivedBlocks { get; }

		public BlockDownloader(Context context)
		{
			this.context = context;
			this.ReceivedBlocks = new ConcurrentDictionary<uint256, Block>();
			this.Fetchers = new ConcurrentDictionary<BlockFetcher, Node>();
		}

		public int DownloadedBlocks => this.ReceivedBlocks.Count;

		public void PushBlock(Block block)
		{
			this.ReceivedBlocks.TryAdd(block.GetHash(), block);
		}

		public void Deplete()
		{
			this.ReceivedBlocks.Clear();
			this.Fetchers.Clear();
		}

		public void AskBlocks(uint256[] downloadRequests)
		{
			// to support downloading from many nodes create more featchers

			if (this.Fetchers.IsEmpty)
				this.CreateFetcher();

			this.Fetchers.Keys.First().Fetch(downloadRequests);
		}

		public Block GetBlock(uint256 blockid)
		{
			Block block = null;
			this.ReceivedBlocks.TryRemove(blockid, out block);
			this.context.Counter.SetPendingBlocks(this.ReceivedBlocks.Count);

			return block;
		}

		private BlockFetcher CreateFetcher()
		{
			while (true)
			{
				try
				{
					// if we have trusted nodes use one of those, else
					// select a random address from the address manager
					// then try to synchrnoize blockchin headers
					var endpoint = this.context.Config.TrustedNodes?.Any() ?? false ? this.context.Config.TrustedNodes.First() : this.context.AddressManager.Select().Endpoint;
					if (this.Fetchers.Values.Any(n => Equals(n.RemoteSocketAddress, endpoint.Address)))
						continue;

					var node = Node.Connect(this.context.Network, endpoint, new NodeConnectionParameters());
					node.VersionHandshake();
					var fetcher = new BlockFetcher(this.context, this, node);
					this.Fetchers.TryAdd(fetcher, node);
					return fetcher;

				}
				catch (OperationCanceledException tokenCanceledException)
				{
					tokenCanceledException.CancellationToken.ThrowIfCancellationRequested();
				}
				catch (ProtocolException)
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
