using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StratisMinter
{
    public class Config
    {
		public string FolderLocation { get; set; }

		// use the specified ip to download the blockchain from
		// in cases where we know a trusted node we can specify it here
		// once blockchain is downloaded blocks will be downloaded from any node
		public List<IPEndPoint> TrustedNodes { get; set; }

		public string File(string path)
	    {
		    return $@"{this.FolderLocation}\{path}";
	    }
    }
}
