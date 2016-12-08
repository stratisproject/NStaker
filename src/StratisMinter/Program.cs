using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using nStratis;

namespace StratisMinter
{
    public class Program
    {
        public static void Main(string[] args)
		{
			var conf = new Config
	        {
		        FolderLocation = AppContext.BaseDirectory,
		        TrustedNodes = new List<IPEndPoint> {new IPEndPoint(IPAddress.Parse("127.0.0.1"), Network.Main.DefaultPort)}
	        };
			var miner = new Minter();
			miner.Run(conf);

        }
    }
}
