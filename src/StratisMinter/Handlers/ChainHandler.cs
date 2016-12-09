using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using nStratis;
using nStratis.BitcoinCore;
using nStratis.Protocol;
using nStratis.Protocol.Behaviors;

namespace StratisMinter.Handlers
{

	public class ChainIndex : ConcurrentChain
	{
		private BlockStore store;
		private IndexedBlockStore indexStore;

		public void Load(Context context)
		{
			// todo: create a repository that persists index data to file
			this.store = new BlockStore(context.Config.FolderLocation, context.Network);
			this.indexStore = new IndexedBlockStore(new InMemoryNoSqlRepository(), store);
			this.indexStore.ReIndex();
		}

		public void AddBlock(Block block)
		{
			block.SetPosParams();
			var header = this.GetBlock(block.GetHash());
			header.Header.PosParameters = block.Header.PosParameters;
			this.indexStore.Put(block);
		}

		public Block GetFullBlock(uint256 blockId)
		{
			return this.indexStore.Get(blockId);
		}

		public ChainedBlock FindLastIndexedBlock()
		{
			var current = this.Tip;

			while (current != this.Genesis)
			{
				if (indexStore.Get(current.HashBlock) != null)
					return current;
				current = current.Previous;
			}

			return this.Genesis;
		}
	}

	public class ChainHandler : Handler
	{
		private readonly Context context;
		private readonly CommunicationHandler comHandler;

		public ChainIndex ChainIndex { get; }

		public ChainHandler(Context context, CommunicationHandler comHandler)
		{
			this.context = context;
			this.ChainIndex = this.context.ChainIndex;
			this.comHandler = comHandler;
		}

		public ChainHandler LoadHeaders()
		{
			// load headers form file (or genesis)
			if (File.Exists(this.context.Config.File("headers.dat")))
			{
				this.ChainIndex.Load(File.ReadAllBytes(this.context.Config.File("headers.dat")));
			}
			else
			{
				this.ChainIndex.SetTip(new ChainedBlock(this.context.Network.GetGenesis().Header, 0));
			}
			
			// load the index chain this will 
			// add each block index to memory for fast lookup
			this.ChainIndex.Load(this.context);

			// sync the headers and save to disk
			this.SyncChain(true);

			// register a behaviour, the ChainBehavior maintains 
			// the chain of headers in sync with the network
			var behaviour = new ChainBehavior(ChainIndex);
			this.context.ConnectionParameters.TemplateBehaviors.Add(behaviour);

			return this;
		}

		public void SyncChain(bool saveToDisk = false)
		{
			// download all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			var node = this.comHandler.GetNode();
			node.SynchronizeChain(ChainIndex, null, context.CancellationToken);

			if(saveToDisk)
				this.SaveChainToDisk();
		}

		private LockObject saveLock = new LockObject();
		private long savedHeight = 0;

		// this method is thread safe
		// it should be called periodically by a behaviour  
		// that is in charge of keeping the chin in sync
		public void SaveChainToDisk()
		{
			saveLock.Lock(() => this.ChainIndex.Tip.Height > savedHeight, () =>
				{
					using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
					{
						this.ChainIndex.WriteTo(file);
					}

					this.savedHeight = this.ChainIndex.Tip.Height;
				});

			//if (this.ChainIndex.Tip.Height > savedHeight)
			//{
			//	lock (saveLock)
			//	{
			//		if (this.ChainIndex.Tip.Height > savedHeight)
			//		{
			//			using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
			//			{
			//				this.ChainIndex.WriteTo(file);
			//			}

			//			this.savedHeight = this.ChainIndex.Tip.Height;
			//		}
			//	}
			//}
		}
	}
}
