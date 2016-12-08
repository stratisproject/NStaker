using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter
{
    public class Logger
    {
		private readonly Context context;

		public Logger(Context context)
		{
			this.context = context;
		}

	    public static Logger Create(Context context)
	    {
		    return new Logger(context);
	    }

	    public Task Run()
	    {
		    return Task.Run(() =>
		    {
			    while (!this.context.CancellationToken.IsCancellationRequested)
			    {
					Console.WriteLine();
					Console.Write(this.context.ToString());
				    this.context.CancellationToken.WaitHandle.WaitOne(10000);
			    }

		    }, this.context.CancellationToken);
	    }

	}
}
