using System;

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

		public BlockSyncService(Context context, NodeConnectionService nodeConnectionService, 
			DownloadManager downloadManager, ChainSyncService chainSyncService)
		{
			this.context = context;
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.downloadManager = downloadManager; 
		}

		public void Stake()
		{
			// the block handler 
		}

		// this method will block until the whole blockchain is downloaded
		// that's called the IBD (Initial Block Download) processes
		// once the block is synced there will be a node behaviour that will 
		// listen to Inv block messages and append them to the chain
		public void DownloadOrCatchup()
		{
			this.downloadManager.SyncBlockchain();

			// in the time it took to sync the chain
			// the tip may have progressed further so at
			// this point sync the headers and the blocks again 
			this.chainSyncService.SyncChain();
			this.downloadManager.SyncBlockchain();

			this.chainSyncService.Save();
		}
	}
}
