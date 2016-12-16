using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using StratisMinter.Behaviour;

namespace StratisMinter.Services
{
	public class InvalidBlockException : Exception
	{
		public InvalidBlockException()
		{
		}

		public InvalidBlockException(string message): base(message)
		{
		}
	}
		
    public class BlockSyncService
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly DownloadManager downloadManager;
		private readonly ChainSyncService chainSyncService;
		private readonly ChainIndex chainIndex;

		public BlockSyncHub BlockSyncHub { get; }

		public BlockSyncService(Context context, NodeConnectionService nodeConnectionService, 
			DownloadManager downloadManager, ChainSyncService chainSyncService)
		{
			this.context = context;
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.downloadManager = downloadManager; 

			this.BlockSyncHub = new BlockSyncHub(context);

		}

		public void Stake()
		{
			// the block handler 
		}

		public void StartReceiving()
		{
			var task = Task.Factory.StartNew(() =>
			{
				try
				{
					while (!this.context.CancellationToken.IsCancellationRequested)
					{
						// todo: move the cancelation and IBD checks to the context for all syncing processes
						// create a child WorkerItem items that will override this logic
						// and be used by any long running processes

						while (this.context.DownloadMode)
							this.context.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));

						// take from the blocking collection 
						// this will block until a block is found
						var receivedBlock = this.BlockSyncHub.ReceiveBlocks.Take(this.context.CancellationToken);
						var receivedBlockHash = receivedBlock.Block.GetHash();

						// check if the block headers where already received
						// in that case processes the block
						var receivedChainedBlock = this.chainIndex.GetBlock(receivedBlockHash);
						if (receivedChainedBlock != null)
						{
							if (receivedChainedBlock.Height <= this.chainIndex.LastIndexedBlock.Height)
							{
								// block was already processed
								continue;
							}

							// next check if the block is next in line
							if (receivedBlock.Block.Header.HashPrevBlock == this.chainIndex.LastIndexedBlock.HashBlock)
							{
								if (receivedBlock.Block.Check())
								{
									if (this.chainIndex.ValidateAndAddBlock(receivedBlock.Block))
									{
										continue;
									}
								}

								// block is not valid consider adding 
								// it to a list of invalid blocks and
								// marking the node to not valid
								continue;
							}

							// how many times have we tried to processes this block
							// if a threshold is reached just discard it
							if (receivedBlock.Attempts > 10)
								continue;

							// block is not next in line send it back to the list
							if (this.BlockSyncHub.ReceiveBlocks.TryAdd(receivedBlock))
								receivedBlock.Attempts++;

							continue;
						}

						// new block the was not received by the header
						// yet processes the block and add to headers

						// first look for the previous header
						var prevChainedBlock = this.chainIndex.Tip.FindAncestorOrSelf(receivedBlock.Block.Header.HashPrevBlock);
						if (prevChainedBlock == null)
							continue; // not found 

						// create a new tip
						var newtip = new ChainedBlock(receivedBlock.Block.Header, receivedBlock.Block.Header.GetHash(), prevChainedBlock);

						if (!receivedBlock.Behaviour.AttachedNode.IsTrusted)
						{
							var validated = this.chainIndex.GetBlock(newtip.HashBlock) != null || newtip.Validate(this.context.Network);
							if (!validated)
							{
								// invalid header received 
								continue;
							}
						}

						// make sure the new chain is longer then 
						// the current chain 
						if (newtip.Height > this.chainIndex.Tip.Height)
						{
							// validate the block 
							if (this.chainIndex.ValidateAndAddBlock(receivedBlock.Block))
							{
								// if the block is valid set the new tip.
								this.chainIndex.SetTip(newtip);
							}
						}
					}
				}
				catch (OperationCanceledException)
				{
					// we are done here
				}

				// the hub is global so it listens to the 
				// global cancelation token 
			}, this.context.CancellationToken, TaskCreationOptions.LongRunning, this.context.TaskScheduler);
		}

		private void CatchupBlocksWithHeaders(object state)
		{
			var bloksToAsk = this.chainIndex.EnumerateAfter(this.chainIndex.LastIndexedBlock).ToArray();

			if (bloksToAsk.Count() > 100)
			{
				// we need to kick the DownloadOrCatchup() method
			}

			this.BlockSyncHub.AskBlocks(bloksToAsk.Select(s => s.HashBlock));
		}

		// this method will block until the whole blockchain is downloaded
		// that's called the IBD (Initial Block Download) processes
		// once the block is synced there will be a node behaviour that will 
		// listen to Inv block messages and append them to the chain
		public void DownloadOrCatchup()
		{
			// enter in to download mode
			this.context.DownloadMode = true;

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
			this.context.DownloadMode = false;

			// for now use a time
			timer = new Timer(this.CatchupBlocksWithHeaders, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
		}

		private Timer timer;
	}
}
