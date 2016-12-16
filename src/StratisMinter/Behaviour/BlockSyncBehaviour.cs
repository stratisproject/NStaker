using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;
using nStratis.Protocol.Payloads;
using StratisMinter.Base;
using StratisMinter.Services;
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
		public ConcurrentDictionary<BlockSyncBehaviour, Node> Behaviours { get; }
		public BlockingCollection<HubBroadcastItem> BroadcastBlocks { get; }
		public BlockingCollection<HubBroadcastItem> ReceiveBlocks { get; }

		private readonly List<Task> runningTasks;
		private readonly ChainIndex chainIndex;

		public BlockSyncHub(Context context) 
		{
			this.Context = context;
			this.chainIndex = context.ChainIndex;
			this.runningTasks = new List<Task>();
			this.Behaviours = new ConcurrentDictionary<BlockSyncBehaviour, Node>();
			this.BroadcastBlocks = new BlockingCollection<HubBroadcastItem>(new ConcurrentQueue<HubBroadcastItem>());
			this.ReceiveBlocks = new BlockingCollection<HubBroadcastItem>(new ConcurrentQueue<HubBroadcastItem>());
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

			// for now only ask from one node
			this.Behaviours.Values.First().SendMessage(message);
		}
	}

	/// <summary>
	/// The block sync behaviour is responsible for listening to blocks on the network 
	/// And serving any required functionality to the hub
	/// It will be able to broadcast blocks to the network
	/// </summary>
	public class BlockSyncBehaviour : NodeBehavior
	{
		private readonly BlockSyncHub blockSyncHub;
		private readonly CancellationTokenSource cancellation;

		/// <summary>
		/// Respond to 'getblocks' messages (Default : true)
		/// </summary>
		public bool CanRespondToGetBlocksPayload { get; set; }

		public bool CanRespondToBlockPayload { get; set; }

		public BlockSyncBehaviour(BlockSyncHub hub)
		{
			this.blockSyncHub = hub;
			this.cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] { this.blockSyncHub.Context.CancellationToken });
			this.CanRespondToGetBlocksPayload = true;
			this.CanRespondToGetBlocksPayload = true;
		}

		protected override void AttachCore()
	    {
			// listen to both state changed events 
			// and message received events
			this.AttachedNode.StateChanged += AttachedNode_StateChanged;
			this.AttachedNode.MessageReceived += AttachedNode_MessageReceived;
	    }

		private void AttachedNode_MessageReceived(Node node, IncomingMessage message)
		{
			var getBlocksPayload = message.Message.Payload as GetBlocksPayload;
			if (this.CanRespondToGetBlocksPayload && getBlocksPayload != null)
				this.RespondToGetBlocksPayload(node, getBlocksPayload);

			var blockPayload = message.Message.Payload as BlockPayload;
			if (this.CanRespondToBlockPayload && blockPayload != null)
				this.RespondToBlockPayload(node, blockPayload);
		}

		private void RespondToGetBlocksPayload(Node node, GetBlocksPayload getBlocksPayload)
		{
			// ideally this would go on in a queue running in its own thread
			// and serves getblock requests this can also be throttled 
			// if our node is too busy we just send a reject message

			// push the GetBlocksPayload to the hug for processing
		}

		private void RespondToBlockPayload(Node node, BlockPayload blockPayload)
		{
			this.blockSyncHub.ReceiveBlocks.Add(new HubBroadcastItem {Block = blockPayload.Object, Behaviour = this});
		}

		public void Broadcast(Block block)
		{
			// broadcast a block to the network
			this.AttachedNode.SendMessage(new BlockPayload(block), this.cancellation.Token);
		}

		private void AttachedNode_StateChanged(Node node, NodeState oldState)
		{
			switch (node.State)
			{
				case NodeState.HandShaked:
				{
					// add the behaviour to the hub
					this.blockSyncHub.Behaviours.TryAdd(this, this.AttachedNode);
					break;
				}
				case NodeState.Failed:
				case NodeState.Disconnecting:
				case NodeState.Offline:
				{
					// remove the behaviour
					Node outnode;
					this.blockSyncHub.Behaviours.TryRemove(this, out outnode);
					break;
				}
			}
		}

		protected override void DetachCore()
	    {
			this.AttachedNode.StateChanged -= AttachedNode_StateChanged;
		}

		public override object Clone()
		{
			return new BlockSyncBehaviour(this.blockSyncHub)
			{
				CanRespondToBlockPayload = this.CanRespondToBlockPayload,
				CanRespondToGetBlocksPayload = this.CanRespondToGetBlocksPayload
			};
		}
    }
}
