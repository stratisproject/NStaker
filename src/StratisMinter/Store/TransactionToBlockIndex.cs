using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using StratisMinter.Services;

namespace StratisMinter.Store
{
	public class PersistableItem<TKey, TValue> : IBitcoinSerializable 
		where TKey : IBitcoinSerializable 
		where TValue : IBitcoinSerializable
	{
		public TKey Key;
		public TValue Value;
		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref Key);
			stream.ReadWrite(ref Value);
		}
	}

	public abstract class PersistableItemIndex<TKey, TValue> : PersistableItem<TKey, TValue> 
		where TKey : IBitcoinSerializable 
		where TValue : IBitcoinSerializable
	{
	    protected readonly Dictionary<TKey, PersistableItem<TKey, TValue>> Table;

		protected PersistableItemIndex()
		{
			this.Table = new Dictionary<TKey, PersistableItem<TKey, TValue>>(); ;
		}

		public virtual bool TryAdd(TKey key, TValue value)
		{
			lock (locker)
			{
				return this.Table.TryAdd(key, new PersistableItem<TKey, TValue>() {Key = key, Value = value});
			}
		}

		public virtual TValue Find(TKey key)
		{
			PersistableItem<TKey, TValue> ret;
			this.Table.TryGetValue(key, out ret);
			if (ret != null) return ret.Value;
			return default(TValue);
		}

		protected void Load(byte[] chain)
		{
			Load(new MemoryStream(chain));
		}

		protected void Load(Stream stream)
		{
			Load(new BitcoinStream(stream, false));
		}

		private readonly object locker = new object();

		protected void Load(BitcoinStream stream)
		{
			lock (locker)
			{
				try
				{
					this.Table.Clear();

					while (true)
					{
						PersistableItem<TKey, TValue> item = null;
						stream.ReadWrite<PersistableItem<TKey, TValue>>(ref item);
						this.Table.TryAdd(item.Key, item);
					}
				}
				catch (EndOfStreamException)
				{
				}
			}
		}

		protected void WriteTo(Stream stream)
		{
			WriteTo(new BitcoinStream(stream, true));
		}

		protected void WriteTo(BitcoinStream stream)
		{
			lock (locker)
			{
				foreach (var persistableItem in this.Table)
				{
					var item = persistableItem.Value;
					stream.ReadWrite(ref item);
				}
			}
		}
	}

	public class TransactionToBlockItemIndex : PersistableItemIndex<uint256.MutableUint256, uint256.MutableUint256>, IDiskStore
	{
		private readonly Context context;

		public uint256 LastBlockId { get; private set; }

		public TransactionToBlockItemIndex(Context context)
		{
			this.context = context;
		}

		public override bool TryAdd(uint256.MutableUint256 key, uint256.MutableUint256 value)
		{
			if (base.TryAdd(key, value))
			{
				if (this.LastBlockId != value.Value)
					this.LastBlockId = value.Value;
			}

			return false;
		}

		public void Load()
		{
			if (File.Exists(this.context.Config.File("trxindex.dat")))
			{
				this.Load(File.ReadAllBytes(this.context.Config.File("trxindex.dat")));
				if (this.Table.Any())
				{
					this.LastBlockId = this.Table.Last().Value.Value.Value;
				}
			}
		}

		public void SaveToDisk()
		{
			using (var file = File.OpenWrite(this.context.Config.File("trxindex.dat")))
			{
				this.WriteTo(file);
			}
		}

		private readonly object lockerObj = new object();

		public void ReIndex(ChainIndex chainIndex)
		{
			lock (lockerObj)
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
					foreach (var trx in block.Transactions)
						this.TryAdd(trx.GetHash().AsBitcoinSerializable(), block.GetHash().AsBitcoinSerializable());
				}
			}
		}
	}
}
