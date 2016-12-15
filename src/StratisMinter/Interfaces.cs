using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StratisMinter
{
	/// <summary>
	/// A interface represents finishing work
	/// </summary>
	public interface IStoppable
	{
		// this method is in charge clean up operations 
		// like storing to disk or finish long running tasks
		void OnStop();
	}

	/// <summary>
	/// A interface represents IStartable work
	/// </summary>
	public interface IStartable
	{ 
		// this method is in charge starting up operations 
		// like long running tasks
		void OnStart();
	}

	public interface IDiskStore
	{
		void SaveToDisk();
	}
}
