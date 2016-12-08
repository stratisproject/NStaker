using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter
{
    public static class Extensions
    {
		public static void ThrowIfCritical(this Exception ex)
		{
			if (ex is OutOfMemoryException)// || ex is ThreadAbortException || ex is StackOverflowException)
			{
				throw ex;
			}
		}
	}
}
