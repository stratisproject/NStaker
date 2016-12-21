using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol;
using StratisMinter.Base;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class DownloadWorker : BlockingWorkItem
	{
		private readonly ChainService chainSyncService;
		private readonly Logger logger;
		private readonly DownloadManager downloadManager;

		public DownloadWorker(Context context, DownloadManager downloadManager, ChainService chainSyncService, Logger logger) : base(context)
		{
			this.chainSyncService = chainSyncService;
			this.logger = logger;
			this.downloadManager = downloadManager;
		}

		// this method will block until the whole blockchain is downloaded
		// that's called the IBD (Initial Block Download) processes
		// once the block is synced there will be a node behaviour that will 
		// listen to Inv block messages and append them to the chain
		public override void Execute()
		{
			// enter in to download mode
			this.Context.DownloadMode = true;

			this.downloadManager.SyncBlockchain();

			// in the time it took to sync the chain
			// the tip may have progressed further so at
			// this point sync the headers and the blocks again 
			this.chainSyncService.SyncChain();
			this.downloadManager.SyncBlockchain();

			// the chin may have chanced
			// update the disk files
			this.chainSyncService.SaveToDisk();

			// exit download mode
			this.Context.DownloadMode = false;
		}
	}

	public class DownloadFetcher
	{
		private readonly DownloadManager downloadManager;
		private readonly Node node;
		private readonly BlockingCollection<uint256> askCollection;
		private readonly CancellationTokenSource cancellation;
		private Task runningTask;

		public DownloadFetcher(Context context, DownloadManager downloadManager, Node node)
		{
			this.downloadManager = downloadManager;
			this.node = node;
			node.StateChanged += NodeOnStateChanged;

			this.askCollection = new BlockingCollection<uint256>(new ConcurrentQueue<uint256>());
			this.cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] {context.CancellationToken});
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

		public DownloadFetcher Processes()
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

	public class DownloadManager 
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainIndex chainIndex;
		private readonly ChainService chainSyncService;
		private readonly ILogger logger;

		public ConcurrentDictionary<DownloadFetcher, Node> Fetchers { get; }
		public ConcurrentDictionary<uint256, Block> ReceivedBlocks { get; }

		public DownloadManager(Context context, NodeConnectionService nodeConnectionService, ChainService chainSyncService, ILoggerFactory loggerFactory)
		{
			this.context = context;
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.ReceivedBlocks = new ConcurrentDictionary<uint256, Block>();
			this.Fetchers = new ConcurrentDictionary<DownloadFetcher, Node>();
			this.logger = loggerFactory.CreateLogger<DownloadManager>();
		}

		public int DownloadedBlocks => this.ReceivedBlocks.Count;

		public void PushBlock(Block block)
		{
			this.ReceivedBlocks.TryAdd(block.GetHash(), block);
		}
		
		public void Deplete()
		{
			this.ReceivedBlocks.Clear();
			foreach (var fetcher in this.Fetchers)
				fetcher.Key.Kill();
			this.Fetchers.Clear();
		}

		private void AskBlocks(IEnumerable<uint256> downloadRequests)
		{
			// to support downloading from many nodes create more fetchers

			if (this.Fetchers.IsEmpty)
				this.CreateFetcher();

			this.Fetchers.Keys.First().Fetch(downloadRequests);
		}

		private Block GetNextPendingBlock(uint256 blockid)
		{
			Block block = null;
			this.ReceivedBlocks.TryRemove(blockid, out block);
			this.context.Counter.SetPendingBlocks(this.ReceivedBlocks.Count);

			return block;
		}

		private DownloadFetcher CreateFetcher()
		{
			var node = this.nodeConnectionService.GetNode(this.context.Config.TrustedNodes.Any());
			var fetcher = new DownloadFetcher(this.context, this, node).Processes();
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

			this.logger.LogInformation($"Download starting at {currentBlock.Height}");
			this.context.Counter.IBDStart();
			this.Deplete();
			var askBlockId = currentBlock.HashBlock;
			var blockCountToAsk = 100;
			var attempts = 0;

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
					break;

				// get the next block to validate
				var nextBlock = this.GetNextPendingBlock(next.HashBlock);
				if (nextBlock != null)
				{
					// for each block validate it
					// its probably better to move 
					// this logic on the ChainIndex
					if (!nextBlock.Check())
						throw new InvalidBlockException();

					// validate and add the block to the chain index
					if(!this.chainIndex.ValidateAndAddBlock(nextBlock))
						throw new InvalidBlockException();

					this.logger.LogInformation($"Added block {next.Height} hash {next.HashBlock}");

					this.context.Counter.SetBlockCount(next.Height);
					this.context.Counter.AddBlocksCount(1);

					// update current block
					currentBlock = next;
				}
				else
				{
					attempts++;
					if (attempts == 100)
					{
						// after an attempt interval reset the ask index
						// in case a node got disconnected we start from 
						// the last indexed block
						askBlockId = currentBlock.HashBlock;
						attempts = 0;
						continue;
					}

					// wait a bit
					this.context.CancellationToken.WaitHandle.WaitOne(1000);
				}
			}

			this.logger.LogInformation($"Download Complete");
			this.Deplete();
		}
	}
}
