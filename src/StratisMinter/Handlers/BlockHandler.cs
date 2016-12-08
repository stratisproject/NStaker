using System;
using System.Linq;
using nStratis;

namespace StratisMinter.Handlers
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
		
    public class BlockHandler
    {
		private readonly Context context;
	    private readonly BlockDownloader downloader;
	    private readonly ChainIndex chainIndex;
	    private ChainedBlock currentBlock;
		private uint256 askBlockId;

		public BlockHandler(Context context)
		{
			this.context = context;
			this.downloader = new BlockDownloader(context);
			this.chainIndex = context.ChainIndex;
		}

		// this method will block until the whole blockchain is downloaded
		// that's called the IBD (Initial Block Download) processes
		// once the block is synced there will be a node behaviour that will 
		// listen to Inv block messages and append them to the chain
		public void DownloadChain()
		{
			// find the last downloaded block
			// we only continue the syn from this point
			// note we can consider triggering the IBD also to 
			// catch up in case the coonnection was dropped
			this.currentBlock = this.chainIndex.FindLastIndexedBlock();

			// are we bellow the cuttent tip
			var currentChain = this.chainIndex.GetBlock(this.currentBlock.HashBlock);
			if(this.chainIndex.Height == currentChain.Height)
				return;

			this.downloader.Deplete();
			askBlockId = currentBlock.HashBlock;
			var blockCountToAsk = 50;

			while (true)
			{
				// check how many blocks are waiting in the downloader 
				if (askBlockId != null && this.downloader.DownloadedBlocks < blockCountToAsk)
				{
					var askMore = this.chainIndex.EnumerateAfter(askBlockId).Take(blockCountToAsk).ToArray();
					askBlockId = askMore.LastOrDefault()?.HashBlock;
					if (askMore.Any())
					{
						this.downloader.AskBlocks(askMore.Select(s => s.HashBlock).ToArray());
					}
				}

				var next = this.chainIndex.GetBlock(currentBlock.Height + 1);
				if (next == null)
					return;

				// ask the downloader for next x blocks
				var nextBlock = this.downloader.GetBlock(next.HashBlock);
				if (nextBlock != null)
				{
					// foreach block validate it
					if (!nextBlock.Check())
						throw new InvalidBlockException();

					//BlockValidator.CheckBlock()

					// add the block to the index
					this.chainIndex.AddBlock(nextBlock);
					this.context.Counter.SetBlockCount(next.Height);
					this.context.Counter.AddBlocksCount(1);

					// update current block
					this.currentBlock = next;
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
