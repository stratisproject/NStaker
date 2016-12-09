using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
		private readonly BlockingCollection<uint256> askCollection;
		private readonly CancellationTokenSource cancellation;
		private Task runningTask;

		public BlockFetcher(Context context, BlockDownloader downloader, Node node)
		{
			this.context = context;
			this.downloader = downloader;
			this.node = node;
			node.StateChanged += NodeOnStateChanged;

			this.askCollection = new BlockingCollection<uint256>(new ConcurrentQueue<uint256>());
			this.cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] {this.context.CancellationToken});

			this.runningTask = this.Processes();
		}

		private IEnumerable<uint256> TakeItems
		{
			get
			{
				while (!this.cancellation.Token.IsCancellationRequested)
				{
					// take an items from the blocking collection 
					// or block until a new items is present
					var item = this.askCollection.Take(this.cancellation.Token);
					yield return item;

					// todo: replace the check on the collection count with a time limit on the cancelation token

					// if the collection is empty break 
					// this will let whatever we already 
					// got form the collection to be fetched
					if (this.askCollection.Empty())
						yield break;
				}
			}
		}

		private Task Processes()
		{
			return Task.Factory.StartNew(() =>
			{
				try
				{
					while (!this.cancellation.Token.IsCancellationRequested)
					{
						// iterate the blocking collection
						// the internal partition manager will stop iteration 
						// when a batch is big enough if the collection is empty 
						// this will block until new items are added or cancelation is called
						foreach (var block in node.GetBlocks(this.TakeItems, context.CancellationToken))
						{
							this.downloader.PushBlock(block);
						}
					}
				}
				catch (OperationCanceledException)
				{
					// we are done here
				}

			}, this.cancellation.Token);
		}

		public void Fetch(IEnumerable<uint256> getblocks)
		{
			foreach (var getblock in getblocks)
				askCollection.TryAdd(getblock, TimeSpan.MaxValue.Milliseconds, this.cancellation.Token);
		}

		private void NodeOnStateChanged(Node nodeParam, NodeState oldState)
		{
			if (nodeParam.State != NodeState.Connected || node.State != NodeState.HandShaked)
			{
				this.downloader.Fetchers.TryRemove(this, out nodeParam);
				this.cancellation.Cancel();
				this.askCollection.Dispose();
			}
		}
	}

	public class BlockDownloader
	{
		private readonly Context context;
		private readonly CommunicationHandler comHandler;

		public ConcurrentDictionary<BlockFetcher, Node> Fetchers { get; }
		public ConcurrentDictionary<uint256, Block> ReceivedBlocks { get; }

		public BlockDownloader(Context context, CommunicationHandler comHandler)
		{
			this.context = context;
			this.comHandler = comHandler;
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

		public void AskBlocks(IEnumerable<uint256> downloadRequests)
		{
			// to support downloading from many nodes create more fetchers

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
			var node = this.comHandler.GetNode();
			var fetcher = new BlockFetcher(this.context, this, node);
			this.Fetchers.TryAdd(fetcher, node);
			return fetcher;
		}

	}
}
