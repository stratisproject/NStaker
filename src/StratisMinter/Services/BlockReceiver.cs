using System;
using System.Threading;
using System.Threading.Tasks;
using nStratis;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class InvalidBlockException : Exception
	{
		public InvalidBlockException()
		{
		}

		public InvalidBlockException(string message): base(message)
		{
		}
	}


	public class BlockReceiver : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainService chainSyncService;
		private readonly ChainIndex chainIndex;

		public BlockSyncHub BlockSyncHub { get; }

		public BlockReceiver(Context context, NodeConnectionService nodeConnectionService,
			BlockSyncHub blockSyncHub, ChainService chainSyncService) : base(context)
		{
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;

			this.BlockSyncHub = blockSyncHub;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				// this method blocks
				this.WaitForDownLoadMode();

				// take from the blocking collection 
				// this will block until a block is found
				var receivedBlock = this.BlockSyncHub.ReceiveBlocks.Take(this.Context.CancellationToken);
				var receivedBlockHash = receivedBlock.Block.GetHash();

				// check if the block headers where already received
				// in that case processes the block
				var receivedChainedBlock = this.chainIndex.GetBlock(receivedBlockHash);
				if (receivedChainedBlock != null)
				{
					if (receivedChainedBlock.Height <= this.chainIndex.LastIndexedBlock.Height)
					{
						// block was already processed
						continue;
					}

					// next check if the block is next in line
					if (receivedBlock.Block.Header.HashPrevBlock == this.chainIndex.LastIndexedBlock.HashBlock)
					{
						if (receivedBlock.Block.Check())
						{
							if (this.chainIndex.ValidateAndAddBlock(receivedBlock.Block))
							{
								continue;
							}
						}

						// block is not valid consider adding 
						// it to a list of invalid blocks and
						// marking the node to not valid
						continue;
					}

					// how many times have we tried to processes this block
					// if a threshold is reached just discard it
					if (receivedBlock.Attempts > 10)
						continue;

					// block is not next in line send it back to the list
					if (this.BlockSyncHub.ReceiveBlocks.TryAdd(receivedBlock))
						receivedBlock.Attempts++;

					continue;
				}

				// new block the was not received by the header
				// yet processes the block and add to headers

				// first look for the previous header
				var prevChainedBlock = this.chainIndex.Tip.FindAncestorOrSelf(receivedBlock.Block.Header.HashPrevBlock);
				if (prevChainedBlock == null)
					continue; // not found 

				// todo: reorg?
				// Add some logic to queue aside pending blocks 
				// if a longer chain mined we wont know until
				// a we have all the blocks
			

				// create a new tip
				var newtip = new ChainedBlock(receivedBlock.Block.Header, receivedBlock.Block.Header.GetHash(), prevChainedBlock);

				if (!receivedBlock.Behaviour.AttachedNode.IsTrusted)
				{
					var validated = this.chainIndex.GetBlock(newtip.HashBlock) != null || newtip.Validate(this.Context.Network);
					if (!validated)
					{
						// invalid header received 
						continue;
					}
				}

				// make sure the new chain is longer then 
				// the current chain 
				if (newtip.Height > this.chainIndex.Tip.Height)
				{
					// validate the block 
					if (this.chainIndex.ValidateAndAddBlock(receivedBlock.Block))
					{
						// if the block is valid set the new tip.
						this.chainIndex.SetTip(newtip);
					}
				}
			}

		}
	}
}
