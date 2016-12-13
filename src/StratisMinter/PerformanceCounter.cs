using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StratisMinter
{
	public class PerformanceCounter
	{
		public PerformanceCounter()
		{
			start = DateTime.UtcNow;
		}

		DateTime start;
		public DateTime Start
		{
			get
			{
				return start;
			}
		}
		public TimeSpan Elapsed
		{
			get
			{
				return DateTime.UtcNow - Start;
			}
		}

		public void SetBlockCount(long count)
		{
			Interlocked.Exchange(ref blockCount, count);
		}
		private long blockCount;
		public long BlockCount => blockCount;

		public void SetPendingBlocks(long count)
		{
			Interlocked.Exchange(ref pendingBlocks, count);
		}
		private long pendingBlocks;
		public long PendingBlocks => pendingBlocks;

		public void AddBlocksCount(long count)
		{
			Interlocked.Add(ref blocksCount, count);
		}
		private long blocksCount;
		public long BlocksCount => blocksCount;

		public void SetConnectedNodes(long count)
		{
			Interlocked.Exchange(ref connectedNodes, count);
		}
		private long connectedNodes;
		public long ConnectedNodes => connectedNodes;
	}

}
