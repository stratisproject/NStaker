using System;
using System.Linq;
using nStratis;

namespace StratisMinter.Handlers
{
	public class InvalidBlockException : Exception
	{
		public InvalidBlockException()
		{
		}

		public InvalidBlockException(string message): base(message)
		{
		}
	}
		
    public class BlockHandler : Handler
	{
		private readonly Context context;
		private readonly ConnectionHandler connectionHandler;
		private readonly DownloadHandler downloadHandler;
		private readonly ChainHandler chainHandler;
		private readonly ChainIndex chainIndex;

		public BlockHandler(Context context, ConnectionHandler connectionHandler, DownloadHandler downloadHandler, ChainHandler chainHandler)
		{
			this.context = context;
			this.connectionHandler = connectionHandler;
			this.chainIndex = context.ChainIndex;
			this.chainHandler = chainHandler;
			this.downloadHandler = downloadHandler; 
		}

		public void Stake()
		{
			// the block handler 
		}
	}
}
