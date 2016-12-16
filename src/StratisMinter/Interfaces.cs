using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter
{
	public interface IDiskStore
	{
		void SaveToDisk();
	}
}
