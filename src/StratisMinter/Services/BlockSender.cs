using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class BlockSender : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainService chainSyncService;
		private readonly DownloadWorker downloadWorker;
		private readonly ChainIndex chainIndex;
		private readonly ILogger logger;

		public BlockSyncHub BlockSyncHub { get; }

		public BlockSender(Context context, NodeConnectionService nodeConnectionService,
			BlockSyncHub blockSyncHub, ChainService chainSyncService, DownloadWorker downloadWorker, ILoggerFactory loggerFactory) : base(context)
		{
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.downloadWorker = downloadWorker;
			this.logger = loggerFactory.CreateLogger<BlockSender>();

			this.BlockSyncHub = blockSyncHub;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				// this method blocks
				this.WaitForDownLoadMode();

				if (this.BlockSyncHub.Behaviours.Any())
				{
					var bloksToAsk = this.chainIndex.EnumerateAfter(this.chainIndex.LastIndexedBlock).ToArray();

					if (bloksToAsk.Count() > 50)
					{
						// use the download worker to catch up
						this.downloadWorker.Execute();
					}
					else
					{
						this.BlockSyncHub.AskBlocks(bloksToAsk.Select(s => s.HashBlock));
					}
				}

				// wait for the chain index to signal a new tip
				// or check after an interval has passed
				this.chainIndex.TipRecetEvent.WaitOne(TimeSpan.FromMinutes(10));
				//this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
			}
		}
	}
}