using Android.App;
using Android.Runtime;

namespace SocketMobileGroceryScanner;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
		{
			try
			{
				var crashPath = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
				File.WriteAllText(crashPath, $"{DateTime.UtcNow}\n{args.Exception}");
			}
			catch { }
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
