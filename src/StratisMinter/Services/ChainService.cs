using System.IO;
using Microsoft.Extensions.Logging;
using nStratis.Protocol.Behaviors;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class ChainService 
	{
		private readonly Context context;
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ILogger logger;

		public ChainIndex ChainIndex { get; }

		public ChainService(Context context, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory)
		{
			this.context = context;
			this.ChainIndex = this.context.ChainIndex;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ChainService>();
		}

		public void SyncChain()
		{
			// download all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			var node = this.nodeConnectionService.GetNode(true);
			node.SynchronizeChain(ChainIndex, null, context.CancellationToken);
		}

		private readonly LockObject saveLock = new LockObject();
		private long savedHeight = 0;

		// this method is thread safe
		// it should be called periodically by a behaviour  
		// that is in charge of keeping the chin in sync
		public void SaveToDisk(bool force = false)
		{
			saveLock.Lock(() => force || this.ChainIndex.Tip.Height > savedHeight, () =>
			{
				using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
				{
					this.ChainIndex.WriteTo(file);
				}

				this.savedHeight = this.ChainIndex.Tip.Height;
			});
		}

	}
}
