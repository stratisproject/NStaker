using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using nStratis.Protocol;
using nStratis.Protocol.Payloads;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class GetDataReceiver : BackgroundWorkItem
	{
		private readonly MinerService minerService;
		public BlockSyncHub BlockSyncHub { get; }
		private readonly ChainIndex chainIndex;

		public GetDataReceiver(Context context, BlockSyncHub blockSyncHub, MinerService minerService) : base(context)
		{
			this.minerService = minerService;
			this.BlockSyncHub = blockSyncHub;
			this.chainIndex = context.ChainIndex;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				// this method blocks
				this.WaitForDownLoadMode();

				// take from the blocking collection 
				var broadcastItem = this.BlockSyncHub.GetDataItems.Take(this.Context.CancellationToken);

				// only processes block types
				foreach (var source in broadcastItem.Payload.Inventory.Where(inv => inv.Type == InventoryType.MSG_BLOCK))
				{
					var block = this.chainIndex.GetFullBlock(source.Hash);

					// check if the mined blocs have been requested
					if (block == null)
						block =this.minerService.MinedBlocks.Where(k => k.Key.HashBlock == source.Hash).Select(s => s.Value).FirstOrDefault();

					if (block != null)
					{
						// previous versions could accept sigs with high s
						if (!BlockValidator.IsCanonicalBlockSignature(block, true))
							if (!BlockValidator.EnsureLowS(block.BlockSignatur))
								throw new HubException();

						broadcastItem.Node.SendMessage(new BlockPayload(block));

						//// Trigger them to send a getblocks request for the next batch of inventory
						//if (broadcastItem.Payload.hash == pfrom->hashContinue)
						//{
						//	// Bypass PushInventory, this must send even if redundant,
						//	// and we want it right after the last block so they don't
						//	// wait for other stuff first.
						//	vector<CInv> vInv;
						//	vInv.push_back(CInv(MSG_BLOCK, hashBestChain));
						//	pfrom->PushMessage("inv", vInv);
						//	pfrom->hashContinue = 0;
						//}
					}
				}
			}
		}
	}
}