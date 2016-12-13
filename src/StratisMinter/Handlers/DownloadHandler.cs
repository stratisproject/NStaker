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
		private readonly DownloadHandler downloadHandler;
		private readonly Node node;
		private readonly Context context;
		private readonly BlockingCollection<uint256> askCollection;
		private readonly CancellationTokenSource cancellation;
		private Task runningTask;

		public BlockFetcher(Context context, DownloadHandler downloadHandler, Node node)
		{
			this.context = context;
			this.downloadHandler = downloadHandler;
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
							this.downloadHandler.PushBlock(block);
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
			this.downloadHandler.Fetchers.TryRemove(this,out nodeInner);
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

	public class DownloadHandler : Handler
	{
		private readonly Context context;
		private readonly ConnectionHandler connectionHandler;
		private readonly ChainIndex chainIndex;
		private readonly ChainHandler chainHandler;

		public ConcurrentDictionary<BlockFetcher, Node> Fetchers { get; }
		public ConcurrentDictionary<uint256, Block> ReceivedBlocks { get; }

		public DownloadHandler(Context context, ConnectionHandler connectionHandler, ChainHandler chainHandler)
		{
			this.context = context;
			this.connectionHandler = connectionHandler;
			this.chainIndex = context.ChainIndex;
			this.chainHandler = chainHandler;
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

		public void Dispose()
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
			var node = this.connectionHandler.GetNode(true);
			var fetcher = new BlockFetcher(this.context, this, node).Processes();
			this.Fetchers.TryAdd(fetcher, node);
			return fetcher;
		}

		// this method will block until the whole blockchain is downloaded
		// that's called the IBD (Initial Block Download) processes
		// once the block is synced there will be a node behaviour that will 
		// listen to Inv block messages and append them to the chain
		public void DownloadOrCatchup()
		{
			this.SyncBlockchain();

			// in the time it took to sync the chain
			// the tip may have progressed further so at
			// this point sync the headers and the blocks again 
			this.chainHandler.SyncChain();
			this.SyncBlockchain();
		}

		private void SyncBlockchain()
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
				// check how many blocks are waiting in the downloadHandler 
				if (askBlockId != null && this.DownloadedBlocks < blockCountToAsk)
				{
					var askMore = this.chainIndex.EnumerateAfter(askBlockId).Take(blockCountToAsk).ToArray();
					askBlockId = askMore.LastOrDefault()?.HashBlock;
					if (askMore.Any())
					{
						// ask the downloadHandler for next x blocks
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

					//BlockValidator.CheckBlock()

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
