using StratisMinter.Base;

namespace StratisMinter.Modules
{
	public class ShutdownAddressManagerModule : ShutdownModule
	{
	
		public ShutdownAddressManagerModule(Context context) : base(context)
		{
		}

		public override int Priority => 9;

		public override void Execute()
		{
			this.Context.AddressManager.SavePeerFile(this.Context.Config.File("peers.dat"), this.Context.Network);
		}
	}
}