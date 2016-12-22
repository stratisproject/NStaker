
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using nStratis;
using nStratis.BitcoinCore;

namespace StratisMinter.Store
{
    // An index to store and append blocks and Proof Of Stake parameters
    // This combines both the block and the PosParams when persisting to disk POS
    // Avoiding storing the PosParams in the header reduces complexity of maintaining
    // the POS proof and modifiers in sync with the stored blocks in disk
    public class IndexedStakeBlockStore : IndexedStore<StoredStakeBlock, StakeBlock>, IBlockProvider
    {
        public new StakeBlockStore Store { get; }

        public IndexedStakeBlockStore(NoSqlRepository index, StakeBlockStore store)
            : base(index, store)
        {
            Store = store;
            IndexedLimit = "Last Index Position";
        }
        
        public StakeBlock Get(uint256 id)
        {
            try
            {
                return GetAsync(id).Result;
            }
            catch (AggregateException aex)
            {
                ExceptionDispatchInfo.Capture(aex.InnerException).Throw();
                return null; //Can't happen
            }
        }

        public Task<StakeBlock> GetAsync(uint256 id)
        {
            return GetAsync(id.ToString());
        }

        #region IBlockProvider Members

        public Block GetBlock(uint256 id, List<byte[]> searchedData)
        {
            var stakeBlock = Get(id.ToString());
            if (stakeBlock == null)
                throw new Exception("Block " + id + " not present in the index");
            if (!stakeBlock.Block.Header.PosParameters.IsSet())
                stakeBlock.Block.Header.PosParameters = stakeBlock.Stake;
            return stakeBlock.Block;
        }

        #endregion

        protected override string GetKey(StakeBlock item)
        {
            return item.Block.GetHash().ToString();
        }

        protected override IEnumerable<StoredStakeBlock> EnumerateForIndex(DiskBlockPosRange range)
        {
            return Store.Enumerate(range);
        }

        protected override IEnumerable<StoredStakeBlock> EnumerateForGet(DiskBlockPosRange range)
        {
            return Store.Enumerate(range);
        }
    }

    public class StakeBlock : IBitcoinSerializable
    {
        public Block Block;
        public PosParameters Stake;

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.Block);
            stream.ReadWrite(ref this.Stake);

			// a small hack, when the block serializes itself 
			// it overrides the pos params, here we set them back
			// this needs to be fixed in NStratis where the block
			// serializer will check if params are already set
			this.Block.Header.PosParameters = this.Stake;
		}
    }

    public class StakeBlockStore : Store<StoredStakeBlock, StakeBlock>
    {
        public const int MAX_BLOCKFILE_SIZE = 0x8000000; // 128 MiB

        public StakeBlockStore(string folder, Network network)
            : base(folder, network)
        {
            MaxFileSize = MAX_BLOCKFILE_SIZE;
            FilePrefix = "blk";
        }

        protected override StoredStakeBlock ReadStoredItem(Stream stream, DiskBlockPos pos)
        {
            StoredStakeBlock storedStakeBlock = new StoredStakeBlock(Network, pos);
            storedStakeBlock.ReadWrite(stream, false);
			
			// set the POS values in to the block header
	        storedStakeBlock.Item.Block.Header.PosParameters = storedStakeBlock.Item.Stake;

			return storedStakeBlock;
        }

        protected override StoredStakeBlock CreateStoredItem(StakeBlock item, DiskBlockPos position)
        {
            return new StoredStakeBlock(Network.Magic, item, position);
        }
    }

    public class StoredStakeBlock : StoredItem<StakeBlock>
    {
        public StoredStakeBlock(Network expectedNetwork, DiskBlockPos position)
            : base(expectedNetwork, position)
        {
        }

        public StoredStakeBlock(uint magic, StakeBlock stakeBlock, DiskBlockPos blockPosition)
            : base(magic, stakeBlock, blockPosition)
        {
        }

        #region IBitcoinSerializable Members

        protected override void ReadWriteItem(BitcoinStream stream, ref StakeBlock item)
        {
            stream.ReadWrite(ref item);
        }

        #endregion

        public static IEnumerable<StoredBlock> EnumerateFile(Network network, string file, uint fileIndex = 0,
            DiskBlockPosRange range = null)
        {
            return new BlockStore(Path.GetDirectoryName(file), network).EnumerateFile(file, fileIndex, range);
        }

        public static IEnumerable<StoredBlock> EnumerateFolder(Network network, string folder,
            DiskBlockPosRange range = null)
        {
            return new BlockStore(folder, network).EnumerateFolder(range);
        }
    }
}
