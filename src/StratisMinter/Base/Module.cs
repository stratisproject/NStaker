namespace StratisMinter.Base
{
	public abstract class Module : BaseItem
	{
		protected Module(Context context)
			: base(context)
		{
		}

		public abstract void Execute();
	}

	public abstract class StartupModule : Module
	{
		protected StartupModule(Context context)
			: base(context)
		{
		}
	}

	public abstract class ShutdownModule : Module
	{
		protected ShutdownModule(Context context)
			: base(context)
		{
		}
	}
}