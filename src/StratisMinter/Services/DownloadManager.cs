using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;

namespace StratisMinter.Services
{
	public class BlockFetcher
	{
		private readonly DownloadManager downloadManager;
		private readonly Node node;
		private readonly Context context;
		private readonly BlockingCollection<uint256> askCollection;
		private readonly CancellationTokenSource cancellation;
		private Task runningTask;

		public BlockFetcher(Context context, DownloadManager downloadManager, Node node)
		{
			this.context = context;
			this.downloadManager = downloadManager;
			this.node = node;
			node.StateChanged += NodeOnStateChanged;

			this.askCollection = new BlockingCollection<uint256>(new ConcurrentQueue<uint256>());
			this.cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] {this.context.CancellationToken});
		}

		private IEnumerable<uint256> EnumerateItems()
		{
			while (!this.cancellation.Token.IsCancellationRequested)
			{
				// take an items from the blocking collection 
				// or block until a new items is present
				var item = this.askCollection.Take(this.cancellation.Token);
				yield return item;

				// if the collection is empty break, this will allow any items already
				// iterated to be submitted to the node. the method GetBlocks()
				// has its own partition logic to calculate how many blocks to 
				// request so here we only serve items and wait for new items 
				if (this.askCollection.Empty())
					yield break;
			}
		}

		public BlockFetcher Processes()
		{
			this.runningTask = Task.Factory.StartNew(() =>
			{
				try
				{
					while (!this.cancellation.Token.IsCancellationRequested)
					{
						// iterate the blocking collection
						// the internal partition manager will stop iteration 
						// when a batch is big enough if the collection is empty 
						// this will block until new items are added or cancelation is called
						foreach (var block in node.GetBlocks(this.EnumerateItems(), this.cancellation.Token))
						{
							this.downloadManager.PushBlock(block);
						}
					}
				}
				catch (OperationCanceledException)
				{
					// we are done here
				}

			}, this.cancellation.Token);

			return this;
		}

		public void Fetch(IEnumerable<uint256> getblocks)
		{
			foreach (var getblock in getblocks)
				askCollection.TryAdd(getblock, TimeSpan.MaxValue.Milliseconds, this.cancellation.Token);
		}

		public void Kill()
		{
			Node nodeInner;
			this.downloadManager.Fetchers.TryRemove(this,out nodeInner);
			this.cancellation.Cancel();
			this.askCollection.Dispose();
		}

		private void NodeOnStateChanged(Node nodeParam, NodeState oldState)
		{
			if (nodeParam.State != NodeState.Connected || node.State != NodeState.HandShaked)
			{
				this.Kill();
			}
		}
	}

	public class DownloadManager : ITerminate
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainIndex chainIndex;
		private readonly ChainSyncService chainSyncService;

		public ConcurrentDictionary<BlockFetcher, Node> Fetchers { get; }
		public ConcurrentDictionary<uint256, Block> ReceivedBlocks { get; }

		public DownloadManager(Context context, NodeConnectionService nodeConnectionService, ChainSyncService chainSyncService)
		{
			this.context = context;
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.ReceivedBlocks = new ConcurrentDictionary<uint256, Block>();
			this.Fetchers = new ConcurrentDictionary<BlockFetcher, Node>();
		}

		public int DownloadedBlocks => this.ReceivedBlocks.Count;

		public void PushBlock(Block block)
		{
			this.ReceivedBlocks.TryAdd(block.GetHash(), block);
		}

		private void Deplete()
		{
			this.ReceivedBlocks.Clear();
			this.Fetchers.Clear();
		}

		public void OnStop()
		{
			foreach (var fetcher in Fetchers)
				fetcher.Key.Kill();
		}

		private void AskBlocks(IEnumerable<uint256> downloadRequests)
		{
			// to support downloading from many nodes create more fetchers

			if (this.Fetchers.IsEmpty)
				this.CreateFetcher();

			this.Fetchers.Keys.First().Fetch(downloadRequests);
		}

		private Block GetBlock(uint256 blockid)
		{
			Block block = null;
			this.ReceivedBlocks.TryRemove(blockid, out block);
			this.context.Counter.SetPendingBlocks(this.ReceivedBlocks.Count);

			return block;
		}

		private BlockFetcher CreateFetcher()
		{
			var node = this.nodeConnectionService.GetNode(true);
			var fetcher = new BlockFetcher(this.context, this, node).Processes();
			this.Fetchers.TryAdd(fetcher, node);
			return fetcher;
		}

		public void SyncBlockchain()
		{
			// find the last downloaded block
			// we only continue the sync from this point
			// note we can consider triggering the IBD also to 
			// catch up in case the connection was dropped
			var currentBlock = this.chainIndex.LastIndexedBlock;

			// are we bellow the current tip
			var currentChain = this.chainIndex.GetBlock(currentBlock.HashBlock);
			if (this.chainIndex.Height == currentChain.Height)
				return;

			this.Deplete();
			var askBlockId = currentBlock.HashBlock;
			var blockCountToAsk = 100;

			while (true)
			{
				// check how many blocks are waiting in the downloadManager 
				if (askBlockId != null && this.DownloadedBlocks < blockCountToAsk)
				{
					var askMore = this.chainIndex.EnumerateAfter(askBlockId).Take(blockCountToAsk).ToArray();
					askBlockId = askMore.LastOrDefault()?.HashBlock;
					if (askMore.Any())
					{
						// ask the downloadManager for next x blocks
						this.AskBlocks(askMore.Select(s => s.HashBlock));
					}
				}

				var next = this.chainIndex.GetBlock(currentBlock.Height + 1);
				if (next == null)
					return;

				// get the next block to validate
				var nextBlock = this.GetBlock(next.HashBlock);
				if (nextBlock != null)
				{
					// for each block validate it
					// its probably better to move 
					// this logic on the ChainIndex
					if (!nextBlock.Check())
						throw new InvalidBlockException();

					// validate the block
					if (!this.chainIndex.ValidaBlock(nextBlock))
						throw new InvalidBlockException();

					// add the block to the chain index
					this.chainIndex.AddBlock(nextBlock);
					this.context.Counter.SetBlockCount(next.Height);
					this.context.Counter.AddBlocksCount(1);

					// update current block
					currentBlock = next;
				}
				else
				{
					// wait a bit
					this.context.CancellationToken.WaitHandle.WaitOne(1000);
				}
			}
		}
	}
}
