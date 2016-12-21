using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StratisMinter.Base
{
	public abstract class WorkItem : BaseItem , IDisposable
	{
		protected List<IDisposable> Disposables;

		protected WorkItem(Context context)
			: base(context)
		{
			this.Disposables = new List<IDisposable>();
		}

		private bool disposed;
		public void Dispose()
		{
			if (!disposed)
			{
				foreach (var disposable in Disposables)
					disposable.Dispose();
				this.disposed = true;
			}
		}
	}

	public abstract class BlockingWorkItem : WorkItem
	{
		protected BlockingWorkItem(Context context)
			: base(context)
		{
		}

		public abstract void Execute();
	}

	public abstract class BackgroundWorkItem : WorkItem
	{
		public Task RunningTask;
		protected CancellationTokenSource Cancellation;
		private volatile bool started;

		protected BackgroundWorkItem(Context context)
			: base(context)
		{
			this.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] { this.Context.CancellationToken });
		}

		protected bool NotCanceled()
		{
			return !this.Cancellation.IsCancellationRequested;
		}

		protected void WaitForDownLoadMode()
		{
			while (this.Context.DownloadMode)
				this.Cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
		}

		public void Execute()
		{
			if(this.started)
				return;

			this.started = true;

			this.RunningTask = Task.Factory.StartNew(() =>
				{
					try
					{
						this.Work();
					}
					catch (OperationCanceledException)
					{
						// we are done here
					}
					catch (Exception ex)
					{
						// unhandled exception
						this.Context.GeneralException = ex;
						this.Context.CancellationTokenSource.Cancel();
					}

				}, this.Cancellation.Token, TaskCreationOptions.LongRunning, this.Context.TaskScheduler);

		}

		protected abstract void Work();
	}
}
