using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Payloads;
using StratisMinter.Store;

namespace StratisMinter.Behaviour
{
	public class HubException : Exception
	{
		public HubException()
		{
		}

		public HubException(string message) : base(message)
		{
		}
	}

	public abstract class HubItem<T> where T : Payload
	{
		public Node Node { get; set; }
		public BlockSyncBehaviour Behaviour { get; set; }
		public T Payload { get; set; }
		public int Attempts { get; set; }
	}

	public class HubGetDataItem : HubItem<GetDataPayload>
	{
	}

	public class HubBroadcastItem : HubItem<Payload>
	{
	}

	public class HubReceiveBlockItem : HubItem<BlockPayload>
	{
		public Block Block { get; set; }
	}

	public class RequestCounter
	{
		public int Count;
	}

	/// <summary>
	/// The hub is responsible for collecting block messages from nodes
	/// It can also broadcast blocks to connected nodes
	/// The hub is shared between all behaviours
	/// </summary>
	public class BlockSyncHub
	{
		public ILogger Logger { get; }

		public ConcurrentDictionary<BlockSyncBehaviour, Node> Behaviours { get; }
		public BlockingCollection<HubBroadcastItem> BroadcastItems { get; }
		public BlockingCollection<HubGetDataItem> GetDataItems { get; }
		public BlockingCollection<HubReceiveBlockItem> ReceiveBlocks { get; }
		public ConcurrentDictionary<uint256, RequestCounter> RequestCount { get; }
		public ChainIndex ChainIndex { get; }
		public Context Context { get; set; }

		public BlockSyncHub(Context context, ILoggerFactory loggerFactory) 
		{
			this.Context = context;
			this.ChainIndex = context.ChainIndex;
			this.Behaviours = new ConcurrentDictionary<BlockSyncBehaviour, Node>();
			this.BroadcastItems = new BlockingCollection<HubBroadcastItem>(new ConcurrentQueue<HubBroadcastItem>());
			this.ReceiveBlocks = new BlockingCollection<HubReceiveBlockItem>(new ConcurrentQueue<HubReceiveBlockItem>());
			this.GetDataItems = new BlockingCollection<HubGetDataItem>(new ConcurrentQueue<HubGetDataItem>());
			this.Logger = loggerFactory.CreateLogger<BlockSyncHub>();
			this.RequestCount = new ConcurrentDictionary<uint256, RequestCounter>();
		}


		public void BroadcastBlockInventory(IEnumerable<uint256> hash)
		{
			var invs = hash.Select(innerHash => new InventoryVector()
			{
				Type = InventoryType.MSG_BLOCK,
				Hash = innerHash
			});

			foreach (var inv in invs)
				this.BroadcastItems.TryAdd(new HubBroadcastItem {Payload = new InvPayload(inv)});
		}
	}
}