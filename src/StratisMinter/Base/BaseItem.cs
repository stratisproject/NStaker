using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter.Base
{
    public abstract class BaseItem
    {
	    protected Context Context;
	    protected IServiceProvider Service;

		protected BaseItem(Context context)
	    {
		    this.Context = context;
		    this.Service = context.Service;
			this.Priority = 50;

		}

		public virtual int Priority { get; }
    }

	
}
