using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NBitcoin;

namespace StratisMinter
{
    public class Config
    {
		public string FolderLocation { get; set; }

	    public int MaximumNodeConnection { get; set; } = 8;

	    public int MaxBlocksInMemory { get; set; } = 20000;

	    public int ConnectedNodesToStake { get; set; } = 3;

		public Key FirstLoadPrivateKey { get; set; }

		// use the specified ip to download the blockchain from
		// in cases where we know a trusted node we can specify it here
		// once blockchain is downloaded blocks will be downloaded from any node
		public List<IPEndPoint> TrustedNodes { get; set; } = new List<IPEndPoint>();

		public string File(string path)
	    {
		    return $@"{this.FolderLocation}\{path}";
	    }
    }
}
