using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using StratisMinter.Services;

namespace StratisMinter.Store
{
	public class TransactionToBlockItemIndex :  IDiskStore
	{
		protected readonly Dictionary<uint256, uint256> Table;
		protected readonly object LockObj = new object();
		private readonly Context context;

		public uint256 LastBlockId { get; private set; }

		public TransactionToBlockItemIndex(Context context)
		{
			this.context = context;
			this.Table = new Dictionary<uint256, uint256>(); ;
		}

		public uint256 Find(uint256 trxid)
		{
			uint256 blockid;
			this.Table.TryGetValue(trxid, out blockid);
			return blockid;
		}

		public void Add(Block block)
		{
			lock (LockObj)
			{
				bool added = false;
				var blockHash = block.GetHash();
				foreach (var transaction in block.Transactions)
					added = this.Table.TryAdd(transaction.GetHash(), blockHash);

				if (added)
				{
					if (this.LastBlockId != blockHash)
						this.LastBlockId = blockHash;
				}
			}
		}

		public void Save()
		{
			lock (LockObj)
			{
				using (var file = File.OpenWrite(this.context.Config.File("trxindex.dat")) )
				{
					var stream = new BitcoinStream(file, true);
						
					foreach (var persistableItem in this.Table)
					{
						stream.ReadWrite(persistableItem.Key.AsBitcoinSerializable());
						stream.ReadWrite(persistableItem.Value.AsBitcoinSerializable());
					}
				}
			}
		}

		public void Load()
		{
			if (File.Exists(this.context.Config.File("trxindex.dat")))
			{
				lock (LockObj)
				{
					if(Table.Any())
						return;

					var bytes = File.ReadAllBytes(this.context.Config.File("trxindex.dat"));
					using (var mem = new MemoryStream(bytes))
					{
						var stream = new BitcoinStream(mem, false);

						try
						{
							this.Table.Clear();
							while (true)
							{
								uint256.MutableUint256 key = null;
								uint256.MutableUint256 value = null;
								stream.ReadWrite(ref key);
								stream.ReadWrite(ref value);
								this.Table.TryAdd(key.Value, value.Value);
							}
						}
						catch (EndOfStreamException)
						{
						}
					}

					if (this.Table.Any())
					{
						this.LastBlockId = this.Table.Last().Value;
					}
				}
			}
		}

		public void SaveToDisk()
		{
			this.Save();
		}

		public void ReIndex(ChainIndex chainIndex)
		{
			lock (LockObj)
			{
				// bring transaction indexes to the same level 
				// as the indexed blocks, in case of a bad shutdown
				while (chainIndex.LastIndexedBlock.HashBlock != this.LastBlockId)
				{
					var findblock = chainIndex.GetBlock(this.LastBlockId ?? chainIndex.Genesis.HashBlock);
					var next = chainIndex.GetBlock(findblock.Height + 1);
					if (next == null)
						break;
					var block = chainIndex.GetFullBlock(next.HashBlock);
					if (block == null)
						break;
					this.Add(block);
				}
			}
		}
	}
}
