using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using nStratis;
using nStratis.Protocol;
using StratisMinter.Base;
using StratisMinter.Store;

namespace StratisMinter.Services
{
	public class WalletTx
	{
		public Transaction Transaction { get; set; }
	}

	public class WalletStore : WorkItem
	{
		public WalletStore(Context context) : base(context)
		{
		}

		public Money GetBalance()
		{
			return Money.Zero;
		}

		public Key GetKey(PubKey pubKey)
		{
			return null;
		}
	}

	public class WalletWorker : BackgroundWorkItem
	{
		private readonly WalletStore walletStore;

		public WalletWorker(Context context, WalletStore walletStore) : base(context)
		{
			this.walletStore = walletStore;
		}

		protected override void Work()
		{
			// this will be a processes that will keep 
			// the wallet utxo up to date, when new blocks
			// are found they are sent here for scanning
		}
	}

	public class WalletService : WorkItem
    {
	    private readonly WalletWorker worker;
	    private readonly WalletStore walletStore;
	    private readonly ChainIndex chainIndex;

	    private readonly long reserveBalance;
	    public WalletService(Context context, WalletWorker worker,  WalletStore walletStore) : base(context)
	    {
		    this.worker = worker;
		    this.walletStore = walletStore;
		    this.chainIndex = context.ChainIndex;

			// Set reserve amount not participating in network protection
			// If no parameters provided current setting is printed
		    this.reserveBalance = 0;
	    }

	    private bool SelectCoinsForStaking(long nTargetValue, uint nSpendTime, out Dictionary<WalletTx,int> setCoinsRet,
		    out long nValueRet)
	    {
		    throw new NotImplementedException();
	    }

		static long GetStakeCombineThreshold() { return 100 * BlockValidator.COIN; }
		static long GetStakeSplitThreshold() { return 2 * GetStakeCombineThreshold(); }

		public bool CreateCoinStake(ChainedBlock pindexBest, uint bits, long nSearchInterval, long fees, ref Transaction txNew, ref Key key)
	    {
			var  pindexPrev = pindexBest;
		    var bnTargetPerCoinDay = new Target(bits).ToCompact();
			

			txNew.Inputs.Clear();
			txNew.Outputs.Clear();

			// Mark coin stake transaction
			txNew.Outputs.Add(new TxOut(Money.Zero, new Script()));

			// Choose coins to use
			var nBalance = this.walletStore.GetBalance().Satoshi;

			if (nBalance <= this.reserveBalance)
				return false;

			List<WalletTx> vwtxPrev = new List<WalletTx>();

			Dictionary<WalletTx, int> setCoins;
			long nValueIn = 0;

			// Select coins with suitable depth
			if (!SelectCoinsForStaking(nBalance - this.reserveBalance, txNew.Time, out setCoins, out nValueIn))
				return false;

			if (setCoins.Empty())
				return false;

			long nCredit = 0;
			Script scriptPubKeyKernel = null;

		    foreach (var coin in setCoins)
		    {
			    int maxStakeSearchInterval = 60;
			    bool fKernelFound = false;

			    for (uint n = 0; n < Math.Min(nSearchInterval, maxStakeSearchInterval) && !fKernelFound && pindexPrev == this.chainIndex.Tip; n++)
			    {
				    var prevoutStake = new OutPoint(coin.Key.Transaction.GetHash(), coin.Value);
				    long nBlockTime = 0;

				    if (BlockValidator.CheckKernel(this.chainIndex, this.chainIndex, this.chainIndex, pindexPrev, bits,
					    txNew.Time - n, prevoutStake, ref nBlockTime))
				    {
					    scriptPubKeyKernel = coin.Key.Transaction.Outputs[coin.Value].ScriptPubKey;

					    key = null;
					    // calculate the key type
					    if (PayToPubkeyTemplate.Instance.CheckScriptPubKey(scriptPubKeyKernel))
					    {
						    key = this.walletStore.GetKey(scriptPubKeyKernel.GetDestinationPublicKeys().First());
					    }
					    else if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(scriptPubKeyKernel))
					    {
						    key = this.walletStore.GetKey(scriptPubKeyKernel.GetDestinationPublicKeys().First());
					    }
					    else
					    {
						    //LogPrint("coinstake", "CreateCoinStake : no support for kernel type=%d\n", whichType);
						    break; // only support pay to public key and pay to address
					    }

					    var scriptPubKeyOut = scriptPubKeyKernel;

					    txNew.Time -= n;
					    txNew.AddInput(new TxIn(prevoutStake));
					    nCredit += coin.Key.Transaction.Outputs[coin.Value].Value;
					    vwtxPrev.Add(coin.Key);
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
			    var cointrx = coin.Key.Transaction;
			    var coinIndex = coin.Value;

				// Attempt to add more inputs
				// Only add coins of the same key/address as kernel
				if (txNew.Outputs.Count == 2 
					&& (
						 cointrx.Outputs[coinIndex].ScriptPubKey == scriptPubKeyKernel ||
						 cointrx.Outputs[coinIndex].ScriptPubKey == txNew.Outputs[1].ScriptPubKey
					   ) 
					&& cointrx.GetHash() != txNew.Inputs[0].PrevOut.Hash)
				{
					long nTimeWeight = BlockValidator.GetWeight((long)cointrx.Time, (long)txNew.Time);

					// Stop adding more inputs if already too many inputs
					if (txNew.Inputs.Count >= 100)
						break;
					// Stop adding inputs if reached reserve limit
					if (nCredit + cointrx.Outputs[coinIndex].Value > nBalance - this.reserveBalance)
						break;
					// Do not add additional significant input
					if (cointrx.Outputs[coinIndex].Value >= GetStakeCombineThreshold())
						continue;
					// Do not add input that is still too young
					if (BlockValidator.IsProtocolV3((int)txNew.Time))
					{
						// properly handled by selection function
					}
					else
					{
						if (nTimeWeight < BlockValidator.StakeMinAge)
							continue;
					}

					txNew.Inputs.Add(new TxIn(new OutPoint(cointrx.GetHash(), coinIndex)));
					
					nCredit += cointrx.Outputs[coinIndex].Value;
					vwtxPrev.Add(coin.Key);
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
				txNew.Outputs[1].Value = (nCredit / 2 / BlockValidator.CENT) * BlockValidator.CENT;
				txNew.Outputs[2].Value = nCredit - txNew.Outputs[1].Value;
			}
			else
				txNew.Outputs[1].Value = nCredit;

			// Sign
			int nIn = 0;
		    foreach (var walletTx in vwtxPrev)
		    {
				// if we got this far all is well
				// todo: sign the outputs
				throw new NotImplementedException();

				//if (!SignSignature(*this, *pcoin, txNew, nIn++))
				//	return error("CreateCoinStake : failed to sign coinstake");
			}

			// Limit size
		    int nBytes = txNew.GetSerializedSize(ProtocolVersion.PROTOCOL_VERSION, SerializationType.Network);
		    if (nBytes >= MAX_BLOCK_SIZE_GEN/5)
			    return false;// error("CreateCoinStake : exceeded coinstake size limit");

			// Successfully generated coinstake
			return true;
		}
		
		/** The maximum allowed size for a serialized block, in bytes (network rule) */
		public const int MAX_BLOCK_SIZE = 1000000;
		/** The maximum size for mined blocks */
		public const int MAX_BLOCK_SIZE_GEN = MAX_BLOCK_SIZE / 2;

	}
}
