using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol.Behaviors;
using StratisMinter.Base;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class ChainService : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ILogger logger;

		public ChainIndex ChainIndex { get; }

		public ChainService(Context context, NodeConnectionService nodeConnectionService, ILoggerFactory loggerFactory)
			: base(context)
		{
			this.ChainIndex = this.Context.ChainIndex;
			this.nodeConnectionService = nodeConnectionService;
			this.logger = loggerFactory.CreateLogger<ChainService>();
			this.Disposables.Add(this.ChainIndex.TipChangedSignal);
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

				//this.SaveToDisk();

				this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(5));
			}
		}

		public double GetPoSKernelPS()
		{
			int nPoSInterval = 72;
			double dStakeKernelsTriedAvg = 0;
			int nStakesHandled = 0, nStakesTime = 0;

			ChainedBlock pindex = this.ChainIndex.Tip;
			ChainedBlock pindexPrevStake = null;

			while (pindex != null && nStakesHandled < nPoSInterval)
			{
				// todo: verify this does not require to be set by the block
				if (pindex.Header.PosParameters.IsProofOfStake())
				{
					if (pindexPrevStake != null)
					{
						dStakeKernelsTriedAvg += this.GetDifficulty(pindexPrevStake)*4294967296.0;
						nStakesTime += (int) pindexPrevStake.Header.Time - (int) pindex.Header.Time;
						nStakesHandled++;
					}
					pindexPrevStake = pindex;
				}

				pindex = pindex.Previous;
			}

			double result = 0;

			if (nStakesTime > 0)
				result = dStakeKernelsTriedAvg/nStakesTime;

			if (BlockValidator.IsProtocolV2(this.ChainIndex.Height))
				result *= BlockValidator.STAKE_TIMESTAMP_MASK + 1;

			return result;
		}

		public double GetDifficulty(ChainedBlock blockindex)
		{
			// Floating point number that is a multiple of the minimum difficulty,
			// minimum difficulty = 1.0.
			if (blockindex == null)
			{
				if (this.ChainIndex.Tip == null)
					return 1.0;
				else
					blockindex = BlockValidator.GetLastBlockIndex(this.ChainIndex.Tip, false);
			}

			var nShift = (blockindex.Header.Bits >> 24) & 0xff;

			double dDiff =
				(double) 0x0000ffff/(double) (blockindex.Header.Bits & 0x00ffffff);

			while (nShift < 29)
			{
				dDiff *= 256.0;
				nShift++;
			}
			while (nShift > 29)
			{
				dDiff /= 256.0;
				nShift--;
			}

			return dDiff;
		}
	}
}
