using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NBitcoin;
using StratisMinter.Base;
using StratisMinter.Services;

namespace StratisMinter
{
	public class LogFilter
	{
		public bool Log { get; set; } = true;

		public Func<string, LogLevel, bool> Filter => this.OnFilter;

		private bool OnFilter(string category, LogLevel logLevel)
		{
			return this.Log;
		}
	}

	public class LoggerKeyReader : BackgroundWorkItem
	{
		private ILoggerFactory loggerFactory;
		private readonly LogFilter logFilter;

		public LoggerKeyReader(Context context, ILoggerFactory loggerFactory, LogFilter logFilter) : base(context)
		{
			this.loggerFactory = loggerFactory;
			this.logFilter = logFilter;
		}

		protected override void Work()
		{
			while (this.NotCanceled())
			{
				var key = Console.ReadKey();
				if (key.Key == ConsoleKey.Spacebar)
					this.logFilter.Log = !this.logFilter.Log;

				if((key.KeyChar == 'Q') || (key.KeyChar == 'q'))
					this.Context.CancellationTokenSource.Cancel();
			}
		}
	}

    public class Logger : BackgroundWorkItem
    {
		private readonly Context context;
	    private readonly LogFilter logFilter;
	    private readonly WalletStore walletStore;
	    private readonly WalletService walletService;
	    private readonly BlockMiner blockMiner;
	    private readonly ChainService chainService;
	    private readonly NodeConnectionService nodeConnectionService;
	    private readonly MinerService minerService;
	    private readonly ILogger logger;

		public Logger(Context context, ILoggerFactory loggerFactory, LogFilter logFilter, WalletStore walletStore, 
			WalletService walletService, BlockMiner blockMiner, ChainService chainService, NodeConnectionService nodeConnectionService, MinerService minerService) : base(context)
		{
			this.context = context;
			this.logFilter = logFilter;
			this.walletStore = walletStore;
			this.walletService = walletService;
			this.blockMiner = blockMiner;
			this.chainService = chainService;
			this.nodeConnectionService = nodeConnectionService;
			this.minerService = minerService;
			this.logger = loggerFactory.CreateLogger<Staker>();
		}

	    protected override void Work()
	    {
		    while (this.NotCanceled())
		    {
			    if (!this.logFilter.Log)
			    {
					Console.Clear();
					Console.Write(BuildOutput());
			    }
			    this.Cancellation.Token.WaitHandle.WaitOne(1000);
		    }
	    }

	    protected string BuildOutput()
	    {
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("Press 'Q' to quite.");
			builder.AppendLine("Press 'Spacebar' to switch views.");
			builder.AppendLine("==== Stats ====");
			builder.AppendLine($"Elapsed = \t\t {this.Context.Counter.Elapsed:c}");
			builder.AppendLine($"ConnectedNodes = \t {this.Service.GetService<NodeConnectionService>().NodesGroup.ConnectedNodes.Count}");
			builder.AppendLine($"HeaderTip = \t\t {this.context.ChainIndex?.Tip?.Height}");
			builder.AppendLine($"IndexedBlock = \t\t {this.context.ChainIndex?.LastIndexedBlock?.Height}");
			builder.AppendLine($"Alt Tips = \t\t {this.context.ChainIndex.AlternateTips.Count}");
			builder.AppendLine($"Tip hash = \t\t {this.context.ChainIndex.Tip.HashBlock}");
			builder.AppendLine("==== Staking ====");
			builder.AppendLine($"Balance = \t\t {this.walletService.GetBalance()}");
			builder.AppendLine($"Staking = \t\t {this.walletService.GetStake()}");
			builder.AppendLine($"Address = \t\t {string.Join(",", this.walletStore.KeyBag.Keys.Select(s => s.PubKey.ToString(this.context.Network)).ToList())}");
		    builder.AppendLine($"SearchInterval = \t {this.blockMiner.LastCoinStakeSearchInterval}");
		    builder.AppendLine($"RequestCount = \t\t {string.Join(",", this.minerService.GetRquestCount().Select(s => s.Value))}");
			builder.AppendLine($"{this.GetStakingInfo()}");
			if (this.context.DownloadMode)
		    {
			    builder.AppendLine("==== Download Perf ====");
			    builder.AppendLine($"CurrentBlock = \t\t {this.Context.Counter.BlockCount}");
			    builder.AppendLine($"PendingBlocks = \t {this.Context.Counter.PendingBlocks}");
			    builder.AppendLine($"Blocks = \t\t {(this.Context.Counter.IBDElapsed.TotalMilliseconds/this.Context.Counter.BlockCount):0.0000} ms/block");
		    }
		    return builder.ToString();
		}

		private string GetStakingInfo()
	    {
		    var nWeight = this.walletService.GetStakeWeight();

		    if (this.blockMiner.LastCoinStakeSearchInterval != 0 && nWeight != 0)
		    {

			    var nNetworkWeight = this.chainService.GetPoSKernelPS();
			    var nEstimateTime = BlockValidator.GetTargetSpacing(this.context.ChainIndex.Tip.Height)*nNetworkWeight/nWeight;

			    string text;
			    if (nEstimateTime < 60)
			    {
				    text = $"{nEstimateTime} second(s)";
			    }
			    else if (nEstimateTime < 60*60)
			    {
				    text = $"{nEstimateTime/60} minute(s)";
			    }
			    else if (nEstimateTime < 24*60*60)
			    {
				    text = $"{nEstimateTime/(60*60)} hour(s)";
			    }
			    else
			    {
				    text = $"{nEstimateTime/(60*60*24)} day(s)";
			    }

			    nWeight /= BlockValidator.COIN;
			    nNetworkWeight /= BlockValidator.COIN;

			    return
				    $"Staking... \n Your weight is {nWeight} Network weight is {nNetworkWeight} \n Expected time to earn reward is {text}";
		    }
		    else
		    {
			    if (this.walletStore.KeyBag.Keys.Empty())
				    return "Not staking because no keys found";

				if (this.nodeConnectionService.NodesGroup.ConnectedNodes.Count < this.context.Config.ConnectedNodesToStake)
				    return "Not staking because wallet is offline";

				if (this.context.DownloadMode)
				    return "Not staking because wallet is syncing";

				if (nWeight == 0)
				    return "Not staking because you don't have mature coins";

			    return "Not staking";
		    }
	    }

	}
}
