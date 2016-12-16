using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StratisMinter.Base;

namespace StratisMinter
{
    public class Logger : BackgroundWorkItem
    {
		private readonly Context context;
		private readonly ILogger logger;

		public Logger(Context context, ILoggerFactory loggerFactory) : base(context)
		{
			this.context = context;
			this.logger = loggerFactory.CreateLogger<Staker>();
		}

	    protected override void Work()
	    {
		    while (this.NotCanceled())
		    {
			    this.logger.LogInformation(this.context.ToString());
			    this.Cancellation.Token.WaitHandle.WaitOne(10000);
		    }
	    }

    }
}
