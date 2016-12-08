using System;
using System.IO;
using System.Linq;
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

	public class ChainHandler
	{
		private readonly Context context;
	    public ChainIndex ChainIndex { get; }

		public ChainHandler(Context context)
		{
			this.context = context;
			this.ChainIndex = this.context.ChainIndex;
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

			// doanload all block headers up to current tip
			// this will loop until complete using a new node
			// if the current node got disconnected 
			while (true)
			{
				try
				{
					// if we have trusted nodes use one of those, else
					// select a random address from the address manager
					// then try to synchrnoize blockchin headers
					var endpoint = this.context.Config.TrustedNodes?.Any() ?? false ? this.context.Config.TrustedNodes.First() : this.context.AddressManager.Select().Endpoint;
					var node = Node.Connect(this.context.Network, endpoint);
					node.VersionHandshake(null, context.CancellationToken);
					node.SynchronizeChain(ChainIndex, null, context.CancellationToken);
					break;
				}
				catch (OperationCanceledException tokenCanceledException)
				{
					tokenCanceledException.CancellationToken.ThrowIfCancellationRequested();
				}
				catch (ProtocolException)
				{
					// continue to try with another node
				}
				catch (Exception ex)
				{
					// try another node
					ex.ThrowIfCritical();
				}
			}

			// update file
			using (var file = File.OpenWrite(this.context.Config.File("headers.dat")))
			{
				ChainIndex.WriteTo(file);
			}

			// register a behaviour, the ChainBehavior maintaines 
			// the chain of headers in sync with the network
			var behaviour = new ChainBehavior(ChainIndex);
			this.context.ConnectionParameters.TemplateBehaviors.Add(behaviour);

			return this;
		}

	}
}
