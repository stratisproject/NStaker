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
	public class HubBroadcastItem
	{
		public BlockSyncBehaviour Behaviour { get; set; }
		public Block Block { get; set; }
		public int Attempts { get; set; }
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
		public BlockingCollection<HubBroadcastItem> BroadcastBlocks { get; }
		public BlockingCollection<HubBroadcastItem> ReceiveBlocks { get; }

		private readonly List<Task> runningTasks;
		public ChainIndex ChainIndex { get; }

		public BlockSyncHub(Context context, ILoggerFactory loggerFactory) 
		{
			this.Context = context;
			this.ChainIndex = context.ChainIndex;
			this.runningTasks = new List<Task>();
			this.Behaviours = new ConcurrentDictionary<BlockSyncBehaviour, Node>();
			this.BroadcastBlocks = new BlockingCollection<HubBroadcastItem>(new ConcurrentQueue<HubBroadcastItem>());
			this.ReceiveBlocks = new BlockingCollection<HubBroadcastItem>(new ConcurrentQueue<HubBroadcastItem>());
			this.Logger = loggerFactory.CreateLogger<BlockSyncHub>();

		}

		public Context Context { get; set; }

		public BlockSyncHub StartBroadcasting()
		{
			// move this to be its own background worker

			var task = Task.Factory.StartNew(() =>
			{
				try
				{
					while (!this.Context.DownloadMode && !this.Context.CancellationToken.IsCancellationRequested)
					{
						// take from the blocking collection 
						var broadcastItem = this.BroadcastBlocks.Take(this.Context.CancellationToken);

						// if no behaviours are found we wait for behaviours
						// this is so we don't lose the block
						while (this.Behaviours.Empty())
							this.Context.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));

						foreach (var behaviour in this.Behaviours)
						{
							// check if the behaviour is not the one that 
							// queue the block, in that case we don't broadcast back. 
							if (!behaviour.Key.Equals(broadcastItem.Behaviour))
								behaviour.Key.Broadcast(broadcastItem.Block);
						}
					}
				}
				catch (OperationCanceledException)
				{
					// we are done here
				}

				// the hub is global so it listens to the 
				// global cancelation token 
			}, this.Context.CancellationToken, TaskCreationOptions.LongRunning, this.Context.TaskScheduler);


			this.runningTasks.Add(task);
			return this;
		}

		public void AskBlocks(IEnumerable<uint256> blockids)
		{
			var invs = blockids.Select(blockid => new InventoryVector()
			{
				Type = InventoryType.MSG_BLOCK,
				Hash = blockid
			});

			var message = new GetDataPayload(invs.ToArray());

			// for now try to ask 3 nodes for blocks
			foreach (var behaviour in Behaviours.Values.Take(3))
				behaviour?.SendMessage(message);
		}
	}
}