using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using nStratis;
using nStratis.Protocol;
using nStratis.Protocol.Payloads;
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

		public InvalidBlockException(string message) : base(message)
		{
		}
	}

	public class OrphanBlock
	{
		public uint256 BlockHash { get; set; }
		public uint256 PreviousHash { get; set; }
		public Block Block { get; set; }
		public Tuple<OutPoint, ulong> Stake { get; set; }
	}

	public class BlockReceiver : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainService chainSyncService;
		private readonly WalletWorker walletWorker;
		private readonly ChainIndex chainIndex;
		private readonly ILogger logger;
		private readonly Dictionary<uint256, OrphanBlock> orphanBlocks;

		public BlockSyncHub BlockSyncHub { get; }

		public BlockReceiver(Context context, NodeConnectionService nodeConnectionService,
			BlockSyncHub blockSyncHub, ChainService chainSyncService, ILoggerFactory loggerFactory, WalletWorker walletWorker)
			: base(context)
		{
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.walletWorker = walletWorker;
			this.logger = loggerFactory.CreateLogger<BlockReceiver>();
			this.orphanBlocks = new Dictionary<uint256, OrphanBlock>();
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
				var receivedBlock = this.BlockSyncHub.ReceiveBlocks.Take(this.Cancellation.Token);

				// check duplicates
				var blockHash = receivedBlock.Block.GetHash();
				if (this.chainIndex.InAnyTip(blockHash) || orphanBlocks.ContainsKey(blockHash))
					continue;

				if(!this.CheckPrevHash(receivedBlock.Behaviour?.AttachedNode, receivedBlock.Block))
					continue;
				
				ChainedBlock chainedBlock;
				if (this.ProcessBlock(receivedBlock.Behaviour?.AttachedNode, receivedBlock.Block, out chainedBlock))
				{
					// if the block is valid persist to store
					this.chainIndex.AddToBlockStore(receivedBlock.Block, chainedBlock);

					// try to set a new tip if a different chain is found
					// it will be added to the alternate chain list. in case 
					// it becomes the longer chain it will override the current tip
					if (this.chainIndex.SetLongestTip(chainedBlock))
					{
						this.logger.LogInformation($"Added block - height: {this.chainIndex.Tip.Height} hash: {this.chainIndex.Tip.HashBlock}");

						// notify the wallet of the new block
						this.walletWorker.AddBlock(receivedBlock.Block);
					}

					// check if this block has any orphans depending on it
					// if yes remove from the orphan then queue to be reprocessed
					var orphan = this.orphanBlocks.Values.FirstOrDefault(orphanBlock => orphanBlock.PreviousHash == chainedBlock.HashBlock);
					if (orphan != null)
					{
						this.orphanBlocks.Remove(orphan.BlockHash);
						this.BlockSyncHub.ReceiveBlocks.TryAdd(new HubReceiveBlockItem { Block = orphan.Block });
					}

					// remove it form orphan blocks if its there
					if (this.orphanBlocks.ContainsKey(blockHash))
						this.orphanBlocks.Remove(blockHash);

					// Relay inventory, but don't relay old inventory during initial block download
					if (!this.Context.DownloadMode)
						this.BlockSyncHub.BroadcastBlockInventory(new[] {blockHash});

					continue;
				}

				// block is not valid 
				this.logger.LogInformation($"Invalid block : {receivedBlock.Block.GetHash()}");
			}
		}

		// reject blocks with non-canonical signatures starting from this version
		static int CANONICAL_BLOCK_SIG_VERSION = 60016;
		static int CANONICAL_BLOCK_SIG_LOW_S_VERSION = 60018;

		private bool CheckPrevHash(Node node, Block block)
		{
			// If we don't already have its previous block, shunt it off to holding area until we get it
			if (!this.chainIndex.InAnyTip(block.Header.HashPrevBlock))
			{
				var blockHash = block.GetHash();

				//LogPrintf("ProcessBlock: ORPHAN BLOCK %lu, prev=%s\n", (unsigned long)mapOrphanBlocks.size(), pblock->hashPrevBlock.ToString());

				// Accept orphans as long as there is a node to request its parents from
				if (node != null)
				{
					// ppcoin: check proof-of-stake
					if (block.IsProofOfStake())
					{
						// Limited duplicity on stake: prevents block flood attack
						// Duplicate stake allowed only when there is orphan child block
						//if (setStakeSeenOrphan.count(pblock->GetProofOfStake()) && !mapOrphanBlocksByPrev.count(hash))
						//	return error("ProcessBlock() : duplicate proof-of-stake (%s, %d) for orphan block %s",
						//		pblock->GetProofOfStake().first.ToString(), pblock->GetProofOfStake().second, hash.ToString());
					}
				}
				var orphan = new OrphanBlock
				{
					BlockHash = blockHash,
					PreviousHash = block.Header.HashPrevBlock,
					Block = block,
				};

				if (block.IsProofOfStake())
					orphan.Stake = block.GetProofOfStake();

				if (this.orphanBlocks.TryAdd(blockHash, orphan))
				{
					if (!this.Context.DownloadMode)
					{
						if (node != null)
						{
							// call get blocks 
							var message = new GetBlocksPayload()
							{
								BlockLocators = this.chainIndex.Tip.GetLocator(),
								HashStop = this.GetOrphanRoot(blockHash)
							};
							node.SendMessage(message);

							// ppcoin: getblocks may not obtain the ancestor block rejected
							// earlier by duplicate-stake check so we ask for it again directly
							//node.SendMessage(new InvPayload(InventoryType.MSG_BLOCK, block.Header.HashPrevBlock));
						}
					}
				}
				return false;
			}

			return true;
		}

		private uint256 GetOrphanRoot(uint256 hash)
		{
			var selector = this.orphanBlocks.TryGet(hash);
			if (selector == null)
				return hash;

			OrphanBlock prev = null;
			while (selector != null)
			{
				prev = selector;
				selector = this.orphanBlocks.TryGet(selector.PreviousHash);
			}

			return prev.BlockHash;
		}

		public bool ProcessBlock(Node node, Block block, out ChainedBlock chainedBlock)
		{
			chainedBlock = null;
			var blockHash = block.GetHash();


			// ppcoin: check proof-of-stake
			// Limited duplicity on stake: prevents block flood attack
			// Duplicate stake allowed only when there is orphan child block
			//if (!fReindex && !fImporting && pblock->IsProofOfStake() && setStakeSeen.count(pblock->GetProofOfStake()) && !mapOrphanBlocksByPrev.count(hash))
			//	return error("ProcessBlock() : duplicate proof-of-stake (%s, %d) for block %s", pblock->GetProofOfStake().first.ToString(), pblock->GetProofOfStake().second, hash.ToString());

			if (block.Header.HashPrevBlock != this.chainIndex.Tip.HashBlock)
			{
				// Extra checks to prevent "fill up memory by spamming with bogus blocks"
				//const CBlockIndex* pcheckpoint = Checkpoints::AutoSelectSyncCheckpoint();
				//var deltaTime = pblock->GetBlockTime() - pcheckpoint->nTime;
				//if (deltaTime < 0)
				//{
				//	if (pfrom)
				//		pfrom->Misbehaving(1);
				//	return error("ProcessBlock() : block with timestamp before last checkpoint");
				//}
			}

			if (!BlockValidator.IsCanonicalBlockSignature(block, false))
			{
				//if (node != null && (int)node.Version >= CANONICAL_BLOCK_SIG_VERSION)
				//node.Misbehaving(100);

				//return false; //error("ProcessBlock(): bad block signature encoding");
			}

			if (!BlockValidator.IsCanonicalBlockSignature(block, true))
			{
				//if (pfrom && pfrom->nVersion >= CANONICAL_BLOCK_SIG_LOW_S_VERSION)
				//{
				//	pfrom->Misbehaving(100);
				//	return error("ProcessBlock(): bad block signature encoding (low-s)");
				//}

				if (!BlockValidator.EnsureLowS(block.BlockSignatur))
					return false; // error("ProcessBlock(): EnsureLowS failed");
			}

			// Preliminary checks
			if (!BlockValidator.CheckBlock(block))
				return false; //error("ProcessBlock() : CheckBlock FAILED");

			var prevChainedBlock = this.chainIndex.GetAnyTip(block.Header.HashPrevBlock);
			if (prevChainedBlock == null)
				throw new InvalidBlockException();

			chainedBlock = new ChainedBlock(block.Header, blockHash, prevChainedBlock);

			if (!block.Header.PosParameters.IsSet())
				chainedBlock.Header.PosParameters = block.SetPosParams();

			// ensure the previous chainedBlock has
			// the POS parameters set if not load its 
			// block and set the pos params
			if (!prevChainedBlock.Header.PosParameters.IsSet())
			{
				var prevBlock = this.chainIndex.GetFullBlock(prevChainedBlock.HashBlock);
				prevChainedBlock.Header.PosParameters = prevBlock.Header.PosParameters;
			}

			// do some checks
			if (!chainedBlock.Validate(this.Context.Network))
				return false;

			// todo: implement this checks

			//if (IsProtocolV2(nHeight) && nVersion < 7)
			//	return DoS(100, error("AcceptBlock() : reject too old nVersion = %d", nVersion));
			//else if (!IsProtocolV2(nHeight) && nVersion > 6)
			//	return DoS(100, error("AcceptBlock() : reject too new nVersion = %d", nVersion));

			//if (IsProofOfWork() && nHeight > Params().LastPOWBlock())
			//	return DoS(100, error("AcceptBlock() : reject proof-of-work at height %d", nHeight));

			//// Check coinbase timestamp
			//if (GetBlockTime() > FutureDrift((int64_t)vtx[0].nTime, nHeight))
			//	return DoS(50, error("AcceptBlock() : coinbase timestamp is too early"));

			// Check coinstake timestamp
			if (block.IsProofOfStake() && !BlockValidator.CheckCoinStakeTimestamp(chainedBlock.Height, block.Header.Time, block.Transactions[1].Time))
				return false; //DoS(50, error("AcceptBlock() : coinstake timestamp violation nTimeBlock=%d nTimeTx=%u", GetBlockTime(), vtx[1].nTime));

			//// Check proof-of-work or proof-of-stake
			//if (nBits != GetNextTargetRequired(pindexPrev, IsProofOfStake()))
			//	return DoS(100, error("AcceptBlock() : incorrect %s", IsProofOfWork() ? "proof-of-work" : "proof-of-stake"));

			//// Check timestamp against prev
			//if (GetBlockTime() <= pindexPrev->GetPastTimeLimit() || FutureDrift(GetBlockTime(), nHeight) < pindexPrev->GetBlockTime())
			//	return error("AcceptBlock() : block's timestamp is too early");

			//// Check that all transactions are finalized
			//BOOST_FOREACH(const CTransaction&tx, vtx)
			//     if (!IsFinalTx(tx, nHeight, GetBlockTime()))
			//	return DoS(10, error("AcceptBlock() : contains a non-final transaction"));

			//// Check that the block chain matches the known block chain up to a checkpoint
			//if (!Checkpoints::CheckHardened(nHeight, hash))
			//	return DoS(100, error("AcceptBlock() : rejected by hardened checkpoint lock-in at %d", nHeight));

		

			if (!BlockValidator.CheckAndComputeStake(this.chainIndex, this.chainIndex, this.chainIndex, this.chainIndex, chainedBlock, block))
				return false;

			// all validations passed
			return true;
		}
	}
}
