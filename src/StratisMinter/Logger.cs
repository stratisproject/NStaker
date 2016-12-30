using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
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
	    private readonly ILogger logger;

		public Logger(Context context, ILoggerFactory loggerFactory, LogFilter logFilter, WalletStore walletStore) : base(context)
		{
			this.context = context;
			this.logFilter = logFilter;
			this.walletStore = walletStore;
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
			builder.AppendLine("==== Staking ====");
			builder.AppendLine($"Balance = \t\t {this.walletStore.GetBalance()}");
		    builder.AppendLine($"Address = \t\t {string.Join(",", this.walletStore.KeyBag.Keys.Select(s => s.PubKey.ToString(this.context.Network)).ToList())}");
			if (this.context.DownloadMode)
		    {
			    builder.AppendLine("==== Download Perf ====");
			    builder.AppendLine($"CurrentBlock = \t\t {this.Context.Counter.BlockCount}");
			    builder.AppendLine($"PendingBlocks = \t {this.Context.Counter.PendingBlocks}");
			    builder.AppendLine($"Blocks = \t\t {(this.Context.Counter.IBDElapsed.TotalMilliseconds/this.Context.Counter.BlockCount):0.0000} ms/block");
		    }
		    return builder.ToString();
		}
    }
}
