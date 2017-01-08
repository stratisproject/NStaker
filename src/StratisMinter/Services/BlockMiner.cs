using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using nStratis;
using StratisMinter.Base;
using StratisMinter.Behaviour;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class MinedBlockException : Exception
	{
		public MinedBlockException()
		{
		}

		public MinedBlockException(string message) : base(message)
		{
		}
	}

	public class MinerService : WorkItem
	{
		private readonly BlockSyncHub blockSyncHub;
		public ConcurrentDictionary<ChainedBlock, Block> MinedBlocks;

		public MinerService(Context context, BlockSyncHub blockSyncHub) : base(context)
		{
			this.blockSyncHub = blockSyncHub;
			this.MinedBlocks = new ConcurrentDictionary<ChainedBlock, Block>();
		}

		public Dictionary<ChainedBlock, int> GetRquestCount()
		{
			return this.GetMiningChainedBlocks().ToDictionary(a => a, m =>
			{
				RequestCounter ret;
				this.blockSyncHub.RequestCount.TryGetValue(m.HashBlock, out ret);
				return ret?.Count ?? 0;
			});
		}

		public IEnumerable<Block> GetMiningBlocks()
		{
			return this.MinedBlocks.Where(k => k.Key.Height > this.Context.ChainIndex.Height).Select(s => s.Value);
		}

		public IEnumerable<ChainedBlock> GetMiningChainedBlocks()
		{
			return this.MinedBlocks.Where(k => k.Key.Height > this.Context.ChainIndex.Height).Select(s => s.Key);
		}

		public bool IsStaking(uint256 trxid, int outIndex)
		{
			foreach (var block in this.GetMiningBlocks())
			{
				if (block.Transactions[1].Inputs.Any(a => a.PrevOut.Hash == trxid && a.PrevOut.N == outIndex))
				{
					return true;
				}
			}

			return false;
		}
	}

	public class BlockMiner : BackgroundWorkItem
	{
		private readonly NodeConnectionService nodeConnectionService;
		private readonly ChainService chainSyncService;
		private readonly ChainIndex chainIndex;
		private readonly WalletService walletService;
		private readonly WalletWorker walletWorker;
		private readonly BlockReceiver blockReceiver;
		private readonly MinerService minerService;

		public BlockSyncHub BlockSyncHub { get; }

		private readonly int minerSleep;
		public long LastCoinStakeSearchInterval;
		public long LastCoinStakeSearchTime;

		public BlockMiner(Context context, NodeConnectionService nodeConnectionService,
			BlockSyncHub blockSyncHub, ChainService chainSyncService, WalletService walletService, WalletWorker walletWorker, 
			BlockReceiver blockReceiver, MinerService minerService) : base(context)
		{
			this.nodeConnectionService = nodeConnectionService;
			this.chainIndex = context.ChainIndex;
			this.chainSyncService = chainSyncService;
			this.walletService = walletService;
			this.walletWorker = walletWorker;
			this.blockReceiver = blockReceiver;
			this.minerService = minerService;
			this.BlockSyncHub = blockSyncHub;
			this.minerSleep = 500; // GetArg("-minersleep", 500);
			this.LastCoinStakeSearchInterval = 0;
			this.LastCoinStakeSearchTime = DateTime.UtcNow.ToUnixTimestamp();
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				if (this.Context.DownloadMode)
					this.LastCoinStakeSearchInterval = 0;

				// this method blocks
				this.WaitForDownLoadMode();

				// we need at least 3 connect5ed nodes
				while (this.nodeConnectionService.NodesGroup.ConnectedNodes.Count < this.Context.Config.ConnectedNodesToStake)
				{
					this.LastCoinStakeSearchInterval = 0;
					this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
				}

				long fee;
				var pindexPrev = this.chainIndex.Tip;

				var block = this.CreateNewBlock(pindexPrev, true, out fee);

				if (this.SignBlock(block, pindexPrev, fee))
				{
					this.CheckState(block, pindexPrev);
				}
				else
				{
					this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(this.minerSleep));
				}
				
			}
		}

		// To decrease granularity of timestamp
		// Supposed to be 2^n-1

		private bool SignBlock(Block block, ChainedBlock pindexBest, long fees)
		{
			// if we are trying to sign
			//    something except proof-of-stake block template
			if (!block.Transactions[0].Outputs[0].IsEmpty)
				return false;

			// if we are trying to sign
			//    a complete proof-of-stake block
			if (block.IsProofOfStake())
				return true;

			//this.LastCoinStakeSearchTime = DateTime.UtcNow.ToUnixTimestamp();// GetAdjustedTime(); // startup timestamp

			Key key = null;
			Transaction txCoinStake = new Transaction();

			//if (BlockValidator.IsProtocolV2(bestHeight + 1)) // we are never in V2
			int tt = (int)txCoinStake.Time;
			tt &= ~BlockValidator.STAKE_TIMESTAMP_MASK;
			txCoinStake.Time = (uint)tt;

			long searchTime = txCoinStake.Time; // search to current time

			//Console.WriteLine($"mask = {txCoinStake.Time & BlockValidator.STAKE_TIMESTAMP_MASK} searchInterval = {searchTime - this.LastCoinStakeSearchTime} searchTime = {searchTime} lastCoinStakeSearchTime = {this.LastCoinStakeSearchTime}");

			if (searchTime > this.LastCoinStakeSearchTime)
			//if (DateTime.UtcNow.Second % 16 == 0) // every 16 sedons
			{
				long searchInterval = searchTime - this.LastCoinStakeSearchTime;
				if (this.walletService.CreateCoinStake(pindexBest, block.Header.Bits, searchInterval, fees, ref txCoinStake, ref key))
				{
					if (txCoinStake.Time >= BlockValidator.GetPastTimeLimit(pindexBest) + 1)
					{
						// make sure coinstake would meet timestamp protocol
						//    as it would be the same as the block timestamp
						block.Transactions[0].Time = block.Header.Time = txCoinStake.Time;

						// we have to make sure that we have no future timestamps in
						//    our transactions set
						foreach (var transaction in block.Transactions)
							if (transaction.Time > block.Header.Time)
								block.Transactions.Remove(transaction);

						block.Transactions.Insert(1, txCoinStake);
						block.UpdateMerkleRoot();

						// append a signature to our block
						var signature = key.Sign(block.GetHash());

						block.BlockSignatur = new BlockSignature {Signature = signature.ToDER()};
						return true;
					}
				}
				this.LastCoinStakeSearchInterval = searchTime - this.LastCoinStakeSearchTime;
				this.LastCoinStakeSearchTime = searchTime;
			}

			return false;
		}

		private void CheckState(Block block, ChainedBlock pindexPrev)
		{
			uint256 hashProof = 0, hashTarget = 0;
			uint256 hashBlock = block.GetHash();

			if (!block.IsProofOfStake())
				return; // error("CheckStake() : %s is not a proof-of-stake block", hashBlock.GetHex());

			// verify hash target and signature of coinstake tx
			if (!BlockValidator.CheckProofOfStake(this.chainIndex, this.chainIndex, this.chainIndex, pindexPrev, block.Transactions[1],
					block.Header.Bits.ToCompact(), out hashProof, out hashTarget))
				return; // error("CheckStake() : proof-of-stake checking failed");

			//// debug print
			//LogPrintf("CheckStake() : new proof-of-stake block found  \n  hash: %s \nproofhash: %s  \ntarget: %s\n", hashBlock.GetHex(), proofHash.GetHex(), hashTarget.GetHex());
			//LogPrintf("%s\n", pblock->ToString());
			//LogPrintf("out %s\n", FormatMoney(pblock->vtx[1].GetValueOut()));

			// Found a solution
			if (block.Header.HashPrevBlock != this.chainIndex.Tip.HashBlock)
				return; // error("CheckStake() : generated block is stale");

			// Process this block the same as if we had received it from another node
			//this.BlockSyncHub.ReceiveBlocks.TryAdd(new HubReceiveBlockItem {Block = block});

			ChainedBlock chainedBlock;
			if (this.blockReceiver.ProcessBlock(null, block, out chainedBlock))
			{
				if (chainedBlock.ChainWork > this.chainIndex.Tip.ChainWork)
				{
					// Track how many getdata requests this block gets
					if (!this.BlockSyncHub.RequestCount.TryAdd(hashBlock, new RequestCounter()))
						throw new MinedBlockException();

					// add to alt tips
					if (!this.minerService.MinedBlocks.TryAdd(chainedBlock, block))
						throw new MinedBlockException();

					// add to wallet
					//this.walletWorker.AddBlock(block);

					// let the wallet processes the transaction
					//this.Cancellation.Token.WaitHandle.WaitOne(1000);

					// broadcast it
					this.BlockSyncHub.BroadcastBlockInventory(new[] {hashBlock});
				}
			}
		}

		private void CheckWork(Block block)
		{
			
		}

		private void IncrementExtraNonce()
		{
			
		}

		private Block CreateNewBlock(ChainedBlock pindexPrev, bool proofOfStake, out long fee)
		{
			fee = 0;

			// create the block 
			var block = new Block();

			var height = pindexPrev.Height + 1;

			// Create coinbase tx
			var txNew = new Transaction();
			txNew.AddInput(new TxIn());
			txNew.AddOutput(new TxOut(Money.Zero, Script.Empty));

			if (!proofOfStake)
			{
				throw new MinedBlockException("Only POS transactions supported");
			}
			else
			{
				// Height first in coinbase required for block.version=2
				txNew.Inputs[0].ScriptSig = new Script(BitConverter.GetBytes(height)); //(CScript() << nHeight) + COINBASE_FLAGS;
				if (!(txNew.Inputs[0].ScriptSig.Length <= 100))
					throw new MinedBlockException();
			}

			// Add our coinbase tx as first transaction
			block.AddTransaction(txNew);

			// Largest block you're willing to create:
			uint nBlockMaxSize = 1000000/2/2; //GetArg("-blockmaxsize", MAX_BLOCK_SIZE_GEN / 2);
			// Limit to betweeen 1K and MAX_BLOCK_SIZE-1K for sanity:
			nBlockMaxSize = Math.Max((uint)1000, Math.Min((uint)(1000000 - 1000), nBlockMaxSize));

			// How much of the block should be dedicated to high-priority transactions,
			// included regardless of the fees they pay
			uint nBlockPrioritySize = 27000; //GetArg("-blockprioritysize", 27000);
			nBlockPrioritySize = Math.Min(nBlockMaxSize, nBlockPrioritySize);

			// Minimum block size you want to create; block will be filled with free transactions
			// until there are no more or the block reaches this size:
			uint nBlockMinSize = 0; //GetArg("-blockminsize", 0);
			nBlockMinSize =Math.Min(nBlockMaxSize, nBlockMinSize);

			// Fee-per-kilobyte amount considered the same as "free"
			// Be careful setting this: if you set it to zero then
			// a transaction spammer can cheaply fill blocks using
			// 1-satoshi-fee transactions. It should be set above the real
			// cost to you of processing a transaction.
			long nMinTxFee = 10000; //MIN_TX_FEE;
			//if (mapArgs.count("-mintxfee"))
			//	ParseMoney(mapArgs["-mintxfee"], nMinTxFee);

			block.Header.Bits = BlockValidator.GetNextTargetRequired(pindexPrev, this.Context.Network.Consensus, proofOfStake);

			// Collect memory pool transactions into the block
			// ============================
			// todo: add transactions from the mem pool when its implemented
			// ============================

			//if (fDebug && GetBoolArg("-printpriority", false))
			//	LogPrintf("CreateNewBlock(): total size %u\n", nBlockSize);

			if (!proofOfStake)
				block.Transactions[0].Outputs[0].Value = BlockValidator.GetProofOfWorkReward(this.chainIndex, fee);

			//if (pFees)
			//	*pFees = nFees;

			// Fill in header
			block.Header.HashPrevBlock = pindexPrev.HashBlock;
			block.Header.BlockTime = pindexPrev.GetMedianTimePast() + TimeSpan.FromSeconds(1.0); //pblock->nTime = max(pindexPrev->GetPastTimeLimit() + 1, pblock->GetMaxTransactionTime());
			if (!proofOfStake)
				block.Header.UpdateTime(this.Context.Network, pindexPrev);

			block.Header.Nonce = 0;

			return block;
		}

		

	}
}