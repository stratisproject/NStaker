using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter
{
	public class TimeSpanExtention
	{
		public static int Infinite => -1;
	}

    public static class Extensions
    {
		public static void ThrowIfCritical(this Exception ex)
		{
			if (ex is OutOfMemoryException)// || ex is ThreadAbortException || ex is StackOverflowException)
			{
				throw ex;
			}
		}

	    public static void Lock(this LockObject obj, Func<bool> condition, Action action)
	    {
		    DoubleLock.Lock(ref obj, condition, action);
	    }

	    public static bool Empty<T>(this IEnumerable<T> items)
	    {
		    return !items.Any();
	    }
    }

	public class DoubleLock
	{
		private object lockItem;
		private Func<bool> condition;
		private Action action;

		public static DoubleLock Lock(ref LockObject lockItem, Func<bool> condition, Action action)
		{
			return new DoubleLock { action = action, lockItem = lockItem, condition = condition }.LockInner();
		}

		public DoubleLock LockInner()
		{
			if (this.condition())
			{
				lock (this.lockItem)
				{
					if (this.condition())
					{
						this.action();
					}
				}
			}

			return this;
		}
	}

	public class LockObject
	{
	}
}
