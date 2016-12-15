using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nStratis;

namespace StratisMinter.Store
{
	/// <summary>
	/// Store blocks in memory to a limit of a count
	/// clearing older blocks first
	/// </summary>
	public class BlockMemoryStore
	{
		private readonly Dictionary<uint256, Block> table;
		private readonly Queue<uint256> queue;
		private readonly Context context;

		public BlockMemoryStore(Context context)
		{
			this.context = context;
			this.table = new Dictionary<uint256, Block>();
			this.queue = new Queue<uint256>();
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
				if (this.table.Count > this.context.Config.MaxBlocksInMemory)
					this.RemoveOne();

				if (this.table.TryAdd(blockid, block))
					this.queue.Enqueue(blockid);
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
			var remove = this.queue.Dequeue();
			this.table.Remove(remove);
		}
	}
}
