using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using StratisMinter.Services;
using Microsoft.Extensions.DependencyInjection;
using StratisMinter.Store;

namespace StratisMinter
{
	public class Context
	{
		public static Context Create(Network network, Config config)
		{
			var cancellationTokenSource = new CancellationTokenSource();
			var context = new Context()
			{
				Network = network,
				Config = config,
				CancellationTokenSource = cancellationTokenSource,
				ConnectionParameters = new NodeConnectionParameters(),
				ChainIndex = new ChainIndex(),
				Counter = new PerformanceCounter(),
				TaskScheduler = TaskScheduler.Default,
				DownloadMode = true, // we always start in download mode
			};
			
			// override the connection cancelation token
			context.ConnectionParameters.ConnectCancellation = context.CancellationToken;
			//context.ChainIndex.Load(context);
			return context;
		}

		public Network Network { get; private set; }
		public Config Config { get; private set; }
		public AddressManager AddressManager { get; set; }
		public CancellationToken CancellationToken => this.CancellationTokenSource.Token;
		public CancellationTokenSource CancellationTokenSource { get; private set; }
		public NodeConnectionParameters ConnectionParameters { get; private set; }
		public ChainIndex ChainIndex { get; private set; }
		public PerformanceCounter Counter { get; private set; }
		public IServiceProvider Service { get; set; }
		public TaskScheduler TaskScheduler { get; private set; }
		public bool DownloadMode { get; set; }

		public Exception GeneralException { get; set; }
	}
}
