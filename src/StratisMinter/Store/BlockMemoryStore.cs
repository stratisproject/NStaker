using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace StratisMinter.Store
{
	/// <summary>
	/// Store blocks in memory to a limit of a count
	/// clearing older blocks first
	/// </summary>
	public class BlockMemoryStore
	{
		private readonly ConcurrentDictionary<uint256, Block> table;
		private readonly ConcurrentQueue<uint256> queue;
		private readonly Context context;

		public BlockMemoryStore(Context context)
		{
			this.context = context;
			this.table = new ConcurrentDictionary<uint256, Block>();
			this.queue = new ConcurrentQueue<uint256>();
		}

		public Block Get(uint256 blockid, Func<Block> func)
		{
			Block block = null;
			if (this.table.TryGetValue(blockid, out block))
			{
				return block;
			}

			block = func();
			if (block != null)
			{
				// ideally keep the latest blocks in memory those are most likely 
				// to be hit, so if we are in IBD mode don't add to memory
				if (!this.context.DownloadMode)
				{
					this.Add(block, blockid);
				}
			}

			return block;
		}

		public void Add(Block block, uint256 blockid = null)
		{
			if (this.table.Count > this.context.Config.MaxBlocksInMemory)
				this.RemoveOne();

			if (this.table.TryAdd(blockid ?? block.GetHash(), block))
				this.queue.Enqueue(blockid);
		}

		private void RemoveOne()
		{
			uint256 blockid; Block block;
			if (this.queue.TryDequeue(out blockid))
				this.table.TryRemove(blockid, out block);
		}
	}
}
