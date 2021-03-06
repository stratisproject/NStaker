﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using StratisMinter.Base;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class WalletException : Exception
	{
		public WalletException()
		{
		}

		public WalletException(string message) : base(message)
		{
		}
	}

	public class Output
	{
		public WalletTx WalletTx;
		public int Depth;
	}

	public class WalletTx : IBitcoinSerializable
	{
		public uint256 Blokckid;
		public uint256 Transactionid;
		public TxOut TxOut;
		public uint256 SpentTransactionid;
		public int OutputIndex;
		public Transaction Transaction;
		public PubKey PubKey;

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref this.Blokckid);
			stream.ReadWrite(ref this.Transactionid);
			stream.ReadWrite(ref this.TxOut);
			stream.ReadWrite(ref this.SpentTransactionid);
			stream.ReadWrite(ref this.OutputIndex);

			var bytes = stream.Serializing ? this.PubKey.ToBytes() : new byte[0];
			stream.ReadWriteAsVarString(ref bytes);
			if (!stream.Serializing)
				this.PubKey = new PubKey(bytes);
		}
	}

	public class Wallet : IBitcoinSerializable
	{
		public ConcurrentDictionary<uint256, WalletTx> WalletsList;
		public uint256 LastIndexBlock;

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref this.LastIndexBlock);
			var list = WalletsList?.Values.ToList() ?? new List<WalletTx>();
			stream.ReadWrite(ref list);
			if (!stream.Serializing)
				this.WalletsList = new ConcurrentDictionary<uint256, WalletTx>(list.Select(l => new KeyValuePair<uint256, WalletTx>(l.Blokckid, l)));
		}
	}

	public class KeyBag
	{
		public List<Key> Keys;
	}

	public class WalletStore : WorkItem
	{
		public Wallet Wallet { get; set; }
		protected readonly object LockObj = new object();
		public KeyBag KeyBag { get; set; }

		public WalletStore(Context context) : base(context)
		{
			this.Wallet = new Wallet {WalletsList = new ConcurrentDictionary<uint256, WalletTx>()};
			this.KeyBag = new KeyBag {Keys = new List<Key>()};
		}

		public Key GetKey(BitcoinAddress pubKey)
		{
			return this.KeyBag.Keys.Single(k => k.PubKey.GetAddress(this.Context.Network) == pubKey);
		}

		public bool CreateAndSaveKeyBag(string password, Key key)
		{
			// only support one key for now

			lock (LockObj)
			{
				if (File.Exists(this.Context.Config.File("walletkeys.dat")))
					return false;

				var encrypted = key.GetEncryptedBitcoinSecret(password, this.Context.Network);
				File.WriteAllText(this.Context.Config.File("walletkeys.dat"), encrypted.ToString());
			}

			return true;
		}

		public bool BagFound()
		{
			return File.Exists(this.Context.Config.File("walletkeys.dat"));
		}

		public bool LoadKeyBag(string password)
		{
			lock (LockObj)
			{
				if (!File.Exists(this.Context.Config.File("walletkeys.dat")))
					return false;

				// assume keys where already loaded
				if (this.KeyBag.Keys.Any())
					return true;

				var encrypted = File.ReadAllText(this.Context.Config.File("walletkeys.dat"));
				var key = Key.Parse(encrypted, password);
				this.KeyBag.Keys.Add(key);
			}

			return true;
		}

		public void Save()
		{
			lock (LockObj)
			{
				using (var file = File.OpenWrite(this.Context.Config.File("walletinfo.dat")))
				{
					var stream = new BitcoinStream(file, true);
					stream.ReadWrite(this.Wallet);
				}
			}
		}

		public void Load()
		{
			if (File.Exists(this.Context.Config.File("walletinfo.dat")))
			{
				lock (LockObj)
				{
					var bytes = File.ReadAllBytes(this.Context.Config.File("walletinfo.dat"));
					using (var mem = new MemoryStream(bytes))
					{
						var stream = new BitcoinStream(mem, false);

						try
						{
							Wallet wallet = null;
							stream.ReadWrite(ref wallet);
							this.Wallet = wallet;
						}
						catch (EndOfStreamException)
						{
						}
					}
				}

				this.LoadTransactions();
			}
		}

		public void LoadTransactions()
		{
			foreach (var walletTx in this.Wallet.WalletsList.Values.Where(t => t.Transaction == null))
			{
				var trx = this.Context.ChainIndex.Get(walletTx.Transactionid);

				if (trx == null)
					throw new WalletException();

				walletTx.Transaction = trx;
			}
		}
	}

	public class WalletWorker : BackgroundWorkItem
	{
		private readonly WalletStore walletStore;
		private readonly WalletService walletService;
		private readonly MinerService minerService;
		private readonly BlockingCollection<Block> blocksToCheck;

		public WalletWorker(Context context, WalletStore walletStore, WalletService walletService, MinerService minerService) : base(context)
		{
			this.walletStore = walletStore;
			this.walletService = walletService;
			this.minerService = minerService;
			this.blocksToCheck = new BlockingCollection<Block>(new ConcurrentQueue<Block>());
			this.Pubkeys = new Lazy<Dictionary<PubKey, BitcoinPubKeyAddress>>(GetPubKeys);
		}

		private Dictionary<PubKey, BitcoinPubKeyAddress> GetPubKeys()
		{
			var dictionary = new Dictionary<PubKey, BitcoinPubKeyAddress>();

			foreach (var walletTx in this.walletStore.Wallet.WalletsList.Values)
				dictionary.TryAdd(walletTx.PubKey, walletTx.PubKey.GetAddress(this.Context.Network));

			foreach (var key in this.walletStore.KeyBag.Keys)
				dictionary.TryAdd(key.PubKey, key.PubKey.GetAddress(this.Context.Network));

			return dictionary;
		}

		private Lazy<Dictionary<PubKey, BitcoinPubKeyAddress>> Pubkeys { get; set; }

		protected override void Work()
		{			
			if(this.Pubkeys.Value.Empty())
				return;

			// this will be a processes that will keep 
			// the wallet utxo up to date, when new blocks
			// are found they are sent here for scanning

			while (this.NotCanceled())
			{
				var block = this.blocksToCheck.Take(this.Cancellation.Token);

				this.ProcessesBlock(block);

				// ride the new block added event
				if (!this.Context.DownloadMode)
					this.ValidateBalnce();
			}
		}

		public void ValidateBalnce()
		{
			// validate the balance in the wallet is still correct
			// a block we mined may have become orphaned and the
			// wallet balance needs to be updated accordingly
			var blocks = this.walletStore.Wallet.WalletsList.Keys;

			foreach (var blockid in blocks)
			{
				if (this.Context.ChainIndex.GetBlock(blockid) == null)
				{
					WalletTx walletTx;
					if (this.walletStore.Wallet.WalletsList.TryRemove(blockid, out walletTx))
					{
						// mark the previous output as unspent
						var unspent = this.walletStore.Wallet.WalletsList.Values.FirstOrDefault(f => f.SpentTransactionid == walletTx.Transactionid);
						if (unspent != null)
							unspent.SpentTransactionid = uint256.Zero;
					}
				}
			}
		}

		public void AddBlock(Block block)
		{
			if (this.Pubkeys.Value.Empty())
				return;

			this.blocksToCheck.Add(block);
		}

		public void ProcessesBlock(Block block)
		{
			var pubKeys = this.Pubkeys.Value;

			bool found = false;
			foreach (var trx in block.Transactions)
			{
				// check all inputs
				foreach (var trxInput in trx.Inputs)
				{
					var item = this.walletStore.Wallet.WalletsList.Values.FirstOrDefault(w =>
					w.Transactionid == trxInput.PrevOut.Hash &&
					w.OutputIndex == trxInput.PrevOut.N);

					if (item != null)
					{
						item.SpentTransactionid = trx.GetHash();
						found = true;
					}
				}

				// check all outputs
				var index = 0;
				foreach (var output in trx.Outputs)
				{
					var outPubKey = output.ScriptPubKey.GetDestinationAddress(this.Context.Network);
					var pubKey = pubKeys.Where(p => p.Value == outPubKey).Select(s => s.Key).FirstOrDefault();

					if (pubKey != null)
					{
						var trxhash = trx.GetHash();
						var blockhash = block.GetHash();

						// add idempotent behaviour to this logic
						if (this.walletStore.Wallet.WalletsList.Values.Any(w => w.Transactionid == trxhash))
							continue;
						
						this.walletStore.Wallet.WalletsList.TryAdd(blockhash, new WalletTx
						{
							Blokckid = blockhash,
							Transaction = trx,
							TxOut = output,
							Transactionid = trxhash,
							SpentTransactionid = uint256.Zero,
							OutputIndex = index,
							PubKey = pubKey // for now deal with a single pub key
						});

						found = true;
					}

					index++;
				}
			}

			this.walletStore.Wallet.LastIndexBlock = block.GetHash();

			if (found)
				this.walletStore.Save();
		}
	}

	public class WalletService : WorkItem
	{
		private readonly WalletStore walletStore;
		private readonly MinerService minerService;
		private readonly ChainIndex chainIndex;

		private readonly long reserveBalance;
		private readonly int coinbaseMaturity;
		private readonly int minimumInputValue;

		public WalletService(Context context, WalletStore walletStore, MinerService minerService) : base(context)
		{
			this.walletStore = walletStore;
			this.minerService = minerService;
			this.chainIndex = context.ChainIndex;

			// Set reserve amount not participating in network protection
			// If no parameters provided current setting is printed
			this.reserveBalance = 0;
			this.coinbaseMaturity = 50;
			this.minimumInputValue = 0;
		}

		public Money GetBalance()
		{
			var money = new Money(0);
			foreach (var walletTx in this.walletStore.Wallet.WalletsList.Values)
			{
				// Must wait until coinbase is safely deep enough in the chain before valuing it
				if ((walletTx.Transaction.IsCoinBase || walletTx.Transaction.IsCoinStake) && this.GetBlocksToMaturity(walletTx) > 0)
					continue;

				if (walletTx.SpentTransactionid == uint256.Zero)
					money += walletTx.TxOut.Value;
			}

			return money;
		}

		public Money GetStake()
		{
			var money = new Money(0);
			foreach (var walletTx in this.walletStore.Wallet.WalletsList.Values)
			{
				// Must wait until coinbase is safely deep enough in the chain before valuing it
				if (walletTx.SpentTransactionid == uint256.Zero)
				{
					if (walletTx.Transaction.IsCoinStake && this.GetBlocksToMaturity(walletTx) > 0 &&
					    this.GetDepthInMainChain(walletTx) > 0)
					{
						 money += walletTx.TxOut.Value;
					}
				}
			}

			return money;
		}

		public Money GetStakeingBalance()
		{
			var money = new Money(0);
			foreach (var walletTx in this.walletStore.Wallet.WalletsList.Values)
			{
				// Must wait until coinbase is safely deep enough in the chain before valuing it
				if (walletTx.SpentTransactionid == uint256.Zero)
				{
					if (this.minerService.IsStaking(walletTx.Transactionid, walletTx.OutputIndex))
						money += walletTx.TxOut.Value;
				}
			}

			return money;
		}

		public ChainedBlock GetAheadMinedBlock(uint256 blockid)
		{
			// only if the mined block is ahead of main chain 
			// assume its going to get included in the main chain
			var chainedBlock = this.minerService.MinedBlocks.Where(k => k.Key.HashBlock == blockid).Select(s => s.Key).FirstOrDefault();
			chainedBlock = (chainedBlock?.Height ?? 0) > this.chainIndex.Height ? chainedBlock : null;
			return chainedBlock;
		}

		private int GetDepthInMainChain(WalletTx walletTx)
		{
			var chainedBlock = this.chainIndex.GetBlock(walletTx.Blokckid) ?? this.GetAheadMinedBlock(walletTx.Blokckid);

			if (chainedBlock == null)
				return 0;

			return this.chainIndex.Tip.Height - chainedBlock.Height + 1;
		}

		private int GetBlocksToMaturity(WalletTx walletTx)
		{
			if (!(walletTx.Transaction.IsCoinBase || walletTx.Transaction.IsCoinStake))
				return 0;

			return Math.Max(0, (this.coinbaseMaturity + 1) - this.GetDepthInMainChain(walletTx));
		}

		private List<Output> AvailableCoinsForStaking(uint nSpendTime)
		{
			var vCoins = new List<Output>();

			foreach (var pcoin in this.walletStore.Wallet.WalletsList.Values)
			{
				int nDepth = this.GetDepthInMainChain(pcoin);
				if (nDepth < 1)
					continue;

				if (BlockValidator.IsProtocolV3((int) nSpendTime))
				{
					if (nDepth < BlockValidator.StakeMinConfirmations)
						continue;
				}
				else
				{
					// Filtering by tx timestamp instead of block timestamp may give false positives but never false negatives
					if (pcoin.Transaction.Time + BlockValidator.StakeMinAge > nSpendTime)
						continue;
				}

				if (this.GetBlocksToMaturity(pcoin) > 0)
					continue;

				//for (int i = 0; i < pcoin.Transaction.Outputs.Count; i++)
					if (pcoin.SpentTransactionid == uint256.Zero && pcoin.TxOut.Value >= this.minimumInputValue)
					{
						// check if the coin is already staking
						//if (!this.minerService.IsStaking(pcoin.Transactionid, pcoin.OutputIndex))
							vCoins.Add(new Output {Depth = nDepth, WalletTx = pcoin});
					}
			}

			return vCoins;
		}

		private bool SelectCoinsForStaking(long nTargetValue, uint nSpendTime, out List<WalletTx> setCoinsRet,
			out long nValueRet)
		{
			var coins = this.AvailableCoinsForStaking(nSpendTime);
			setCoinsRet = new List<WalletTx>();
			nValueRet = 0;

			foreach (var output in coins)
			{
				var pcoin = output.WalletTx;
				//int i = output.Index;

				// Stop if we've chosen enough inputs
				if (nValueRet >= nTargetValue)
					break;

				var n = pcoin.TxOut.Value;

				if (n >= nTargetValue)
				{
					// If input value is greater or equal to target then simply insert
					//    it into the current subset and exit
					setCoinsRet.Add(pcoin);
					nValueRet += n;
					break;
				}
				else if (n < nTargetValue + BlockValidator.CENT)
				{
					setCoinsRet.Add(pcoin);
					nValueRet += n;
				}
			}

			return true;
		}

		static long GetStakeCombineThreshold()
		{
			return 100*BlockValidator.COIN;
		}

		static long GetStakeSplitThreshold()
		{
			return 2*GetStakeCombineThreshold();
		}

		public bool CreateCoinStake(ChainedBlock pindexBest, uint bits, long nSearchInterval, long fees, ref Transaction txNew,
			ref Key key)
		{
			var pindexPrev = pindexBest;
			var bnTargetPerCoinDay = new Target(bits).ToCompact();

			txNew.Inputs.Clear();
			txNew.Outputs.Clear();

			// Mark coin stake transaction
			txNew.Outputs.Add(new TxOut(Money.Zero, new Script()));

			// Choose coins to use
			var nBalance = this.GetBalance().Satoshi;

			if (nBalance <= this.reserveBalance)
				return false;

			List<WalletTx> vwtxPrev = new List<WalletTx>();

			List<WalletTx> setCoins;
			long nValueIn = 0;

			// Select coins with suitable depth
			if (!SelectCoinsForStaking(nBalance - this.reserveBalance, txNew.Time, out setCoins, out nValueIn))
				return false;

			// check if coins are already staking
			// this is different from the c++ implementation
			// which pushes the new block to the main chain
			// and removes it when a longer chain is found
			foreach (var walletTx in setCoins.ToList())
				if (this.minerService.IsStaking(walletTx.Transactionid, walletTx.OutputIndex))
					setCoins.Remove(walletTx);

			if (setCoins.Empty())
				return false;

			long nCredit = 0;
			Script scriptPubKeyKernel = null;

			foreach (var coin in setCoins)
			{
				int maxStakeSearchInterval = 60;
				bool fKernelFound = false;

				for (uint n = 0;
					n < Math.Min(nSearchInterval, maxStakeSearchInterval) && !fKernelFound && pindexPrev == this.chainIndex.Tip;
					n++)
				{
					var prevoutStake = new OutPoint(coin.Transactionid, coin.OutputIndex);
					long nBlockTime = 0;

					if (BlockValidator.CheckKernel(this.chainIndex, this.chainIndex, this.chainIndex, pindexPrev, bits,
						txNew.Time - n, prevoutStake, ref nBlockTime))
					{
						scriptPubKeyKernel = coin.TxOut.ScriptPubKey;

						key = null;
						// calculate the key type
						if (PayToPubkeyTemplate.Instance.CheckScriptPubKey(scriptPubKeyKernel))
						{
							var outPubKey = scriptPubKeyKernel.GetDestinationAddress(this.Context.Network);
							key = this.walletStore.GetKey(outPubKey);
						}
						else if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(scriptPubKeyKernel))
						{
							var outPubKey = scriptPubKeyKernel.GetDestinationAddress(this.Context.Network);
							key = this.walletStore.GetKey(outPubKey);
						}
						else
						{
							//LogPrint("coinstake", "CreateCoinStake : no support for kernel type=%d\n", whichType);
							break; // only support pay to public key and pay to address
						}

						// create a pubkey script form the current script
						var scriptPubKeyOut = PayToPubkeyTemplate.Instance.GenerateScriptPubKey(key.PubKey); //scriptPubKeyKernel;

						txNew.Time -= n;
						txNew.AddInput(new TxIn(prevoutStake));
						nCredit += coin.TxOut.Value;
						vwtxPrev.Add(coin);
						txNew.Outputs.Add(new TxOut(0, scriptPubKeyOut));

						//LogPrint("coinstake", "CreateCoinStake : added kernel type=%d\n", whichType);
						fKernelFound = true;
						break;
					}
				}

				if (fKernelFound)
					break; // if kernel is found stop searching
			}

			if (nCredit == 0 || nCredit > nBalance - this.reserveBalance)
				return false;

			foreach (var coin in setCoins)
			{
				var cointrx = coin;
				//var coinIndex = coin.Value;

				// Attempt to add more inputs
				// Only add coins of the same key/address as kernel
				if (txNew.Outputs.Count == 2
				    && (
					    cointrx.TxOut.ScriptPubKey == scriptPubKeyKernel ||
					    cointrx.TxOut.ScriptPubKey == txNew.Outputs[1].ScriptPubKey
				    )
				    && cointrx.Transactionid != txNew.Inputs[0].PrevOut.Hash)
				{
					long nTimeWeight = BlockValidator.GetWeight((long) cointrx.Transaction.Time, (long) txNew.Time);

					// Stop adding more inputs if already too many inputs
					if (txNew.Inputs.Count >= 100)
						break;
					// Stop adding inputs if reached reserve limit
					if (nCredit + cointrx.TxOut.Value > nBalance - this.reserveBalance)
						break;
					// Do not add additional significant input
					if (cointrx.TxOut.Value >= GetStakeCombineThreshold())
						continue;
					// Do not add input that is still too young
					if (BlockValidator.IsProtocolV3((int) txNew.Time))
					{
						// properly handled by selection function
					}
					else
					{
						if (nTimeWeight < BlockValidator.StakeMinAge)
							continue;
					}

					txNew.Inputs.Add(new TxIn(new OutPoint(cointrx.Transactionid, cointrx.OutputIndex)));

					nCredit += cointrx.TxOut.Value;
					vwtxPrev.Add(coin);
				}
			}

			// Calculate coin age reward
			ulong nCoinAge;
			if (!BlockValidator.GetCoinAge(this.chainIndex, this.chainIndex, this.chainIndex, txNew, pindexPrev, out nCoinAge))
				return false; //error("CreateCoinStake : failed to calculate coin age");

			long nReward = BlockValidator.GetProofOfStakeReward(pindexPrev, nCoinAge, fees);
			if (nReward <= 0)
				return false;

			nCredit += nReward;


			if (nCredit >= GetStakeSplitThreshold())
				txNew.Outputs.Add(new TxOut(0, txNew.Outputs[1].ScriptPubKey)); //split stake

			// Set output amount
			if (txNew.Outputs.Count == 3)
			{
				txNew.Outputs[1].Value = (nCredit/2/BlockValidator.CENT)*BlockValidator.CENT;
				txNew.Outputs[2].Value = nCredit - txNew.Outputs[1].Value;
			}
			else
				txNew.Outputs[1].Value = nCredit;

			// Sign
			int nIn = 0;
			foreach (var walletTx in vwtxPrev)
			{
				if (!SignSignature(new[] {key}, walletTx.Transaction, txNew, nIn++))
					return false; // error("CreateCoinStake : failed to sign coinstake");
			}

			// Limit size
			int nBytes = txNew.GetSerializedSize(ProtocolVersion.PROTOCOL_VERSION, SerializationType.Network);
			if (nBytes >= MAX_BLOCK_SIZE_GEN/5)
				return false; // error("CreateCoinStake : exceeded coinstake size limit");

			// Successfully generated coinstake
			return true;
		}

		private bool SignSignature(Key[] keys, Transaction txFrom, Transaction txTo, int n, params Script[] knownRedeems)
		{
			try
			{
				new TransactionBuilder()
					.AddKeys(keys)
					.AddKnownRedeems(knownRedeems)
					.AddCoins(txFrom)
					.SignTransactionInPlace(txTo);
			}
			catch (Exception)
			{
				return false;
			}

			return true;
		}

		/** The maximum allowed size for a serialized block, in bytes (network rule) */
		public const int MAX_BLOCK_SIZE = 1000000;
		/** The maximum size for mined blocks */
		public const int MAX_BLOCK_SIZE_GEN = MAX_BLOCK_SIZE / 2;

		public ulong GetStakeWeight()
		{
			// Choose coins to use
			var nBalance = this.GetBalance();

			if (nBalance <= this.reserveBalance)
				return 0;

			List<WalletTx> vwtxPrev = new List<WalletTx>();

			List<WalletTx> setCoins;
			long nValueIn = 0;

			if (!SelectCoinsForStaking(nBalance - this.reserveBalance, (uint) DateTime.UtcNow.ToUnixTimestamp(), out setCoins, out nValueIn))
				return 0;

			if (setCoins.Empty())
				return 0;

			ulong nWeight = 0;

			var nCurrentTime = (uint) DateTime.UtcNow.ToUnixTimestamp();

			foreach (var pcoin in setCoins)
			{
				if (BlockValidator.IsProtocolV3((int) nCurrentTime))
				{
					if (this.GetDepthInMainChain(pcoin) >= BlockValidator.StakeMinConfirmations)
					nWeight += pcoin.TxOut.Value;
				}
				else
				{
					if (nCurrentTime - pcoin.Transaction.Time > BlockValidator.StakeMinAge)
						nWeight += pcoin.TxOut.Value;
				}
			}

			return nWeight;
		}

	}
}
