using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StratisMinter
{
    public class Logger
    {
		private readonly Context context;
		private readonly ILogger logger;

		public Logger(Context context, ILoggerFactory loggerFactory)
		{
			this.context = context;
			this.logger = loggerFactory.CreateLogger<Staker>();
		}

	    public Task Run()
	    {
		    return Task.Run(() =>
		    {
			    while (!this.context.CancellationToken.IsCancellationRequested)
			    {
					this.logger.LogInformation(this.context.ToString());
				    this.context.CancellationToken.WaitHandle.WaitOne(10000);
			    }

		    }, this.context.CancellationToken);
	    }

	}
}
