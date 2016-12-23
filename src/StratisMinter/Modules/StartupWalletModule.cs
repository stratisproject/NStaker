using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using StratisMinter.Base;
using StratisMinter.Services;
using StratisMinter.Store;

namespace StratisMinter.Modules
{
	public class StartupWalletModule : StartupModule
	{
		public ChainIndex ChainIndex { get; }
		private readonly ILogger logger;
		private readonly WalletStore walletStore;
		private readonly WalletWorker walletWorker;
		private readonly LogFilter logFilter;

		public StartupWalletModule(Context context,ILoggerFactory loggerFactory, WalletStore walletStore, WalletWorker walletWorker, LogFilter logFilter) : base(context)
		{
			this.ChainIndex = context.ChainIndex;
			this.walletStore = walletStore;
			this.walletWorker = walletWorker;
			this.logFilter = logFilter;
			this.logger = loggerFactory.CreateLogger<StartupWalletModule>();
		}

		public override int Priority => 12;

		public override void Execute()
		{
			// the approach I took with managing private keys
			// is on first time load a prv key file is created
			// using a password, after that only the password is 
			// required to load the key bag in to memory
			// if the file is not present and no key provided in 
			// config a message is passed to the user


			if (this.Context.Config.FirstLoadPrivateKey != null)
			{
				Console.WriteLine("Create a private key password:");
				var pass = Console.ReadLine();

				if (!this.walletStore.CreateAndSaveKeyBag(pass, this.Context.Config.FirstLoadPrivateKey))
				{
					Console.WriteLine("Key bag already exists!!");
					Console.WriteLine("Either remove your private key from config or delete the 'walletkeys.dat' file");
					Console.WriteLine("If you delete the file the private keys will be lost!!!");
					Console.ReadKey();
					this.Context.CancellationTokenSource.Cancel();
					throw new WalletException();
				}
			}
			else
			{
				if (!this.walletStore.BagFound())
				{
					this.logger.LogInformation("No wallet...");
					this.logFilter.Log = false;
					return;
				}

				Console.WriteLine("Enter the private key password:");
				var pass = Console.ReadLine();
				this.walletStore.LoadKeyBag(pass);
			}

			this.logger.LogInformation("Loading wallet...");
			this.walletStore.Load();

			if (this.walletStore.Wallet.WalletsList.Any())
			{
				// ensure all previous pub keys have 
				// the corresponding private key

				var pubKeys = this.walletStore.Wallet.WalletsList.Select(s => s.PubKey).Distinct();
				var prvtoPub = this.walletStore.KeyBag.Keys.Select(s => s.PubKey);
				if (pubKeys.Any(p => !prvtoPub.Contains(p)))
				{
					Console.WriteLine("Wallet files 'walletkeys.dat' and 'walletinfo.dat' don't match");
					Console.ReadKey();
					this.Context.CancellationTokenSource.Cancel();
					throw new WalletException();
				}
			}

			this.logger.LogInformation("Scanning wallet...");

			if (this.walletStore.Wallet.LastIndexBlock == null)
				this.walletStore.Wallet.LastIndexBlock = this.ChainIndex.Genesis.HashBlock;

			var index = 0;
			foreach (var chainedBlock in this.ChainIndex.EnumerateToTip(this.walletStore.Wallet.LastIndexBlock))
			{
				if (chainedBlock == this.ChainIndex.LastIndexedBlock)
					break;
				
				var block = this.ChainIndex.GetFullBlock(chainedBlock.HashBlock);
				if (block == null)
				{
					if(chainedBlock == this.ChainIndex.Genesis)
						continue;
					
					throw new InvalidBlockException(); // this should never really happen
				}

				index++;
				if (index%100 == 0)
					this.logger.LogInformation($"Processing block ${chainedBlock.Height}...");

				this.walletWorker.ProcessesBlock(block);
			}

			this.walletStore.Save();

			this.logFilter.Log = false;

		}
	}
}