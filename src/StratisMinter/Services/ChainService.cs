﻿using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using nStratis.Protocol.Behaviors;
using StratisMinter.Base;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class ChainService : BackgroundWorkItem 
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ILogger logger;

		public ChainIndex ChainIndex { get; }

		public ChainService(Context context, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory) : base(context)
		{
			this.ChainIndex = this.Context.ChainIndex;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ChainService>();
			this.Disposables.Add(this.ChainIndex.TipRecetEvent);
		}

		public void SyncChain()
		{
			// download all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			var node = this.nodeConnectionService.GetNode(this.Context.Config.TrustedNodes.Any());
			node.SynchronizeChain(ChainIndex, null, this.Context.CancellationToken);
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
				using (var file = File.OpenWrite(this.Context.Config.File("headers.dat")))
				{
					this.ChainIndex.WriteTo(file);
				}

				this.savedHeight = this.ChainIndex.Tip.Height;
			});
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				this.WaitForDownLoadMode();

				this.SaveToDisk();

				this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(5));
			}
		}
	}
}
