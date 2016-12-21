using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nStratis;
using StratisMinter.Base;

namespace StratisMinter.Services
{
    public class WalletService : WorkItem
    {
	    public WalletService(Context context) : base(context)
	    {
	    }

	    public bool CreateCoinStake(uint bits, long nSearchInterval, long fees, ref Transaction txNew, ref Key key)
	    {
		    return false;
	    }

    }
}
