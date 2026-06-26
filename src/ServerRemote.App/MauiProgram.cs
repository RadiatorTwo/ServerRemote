using Microsoft.Extensions.Logging;
using ServerRemote.App.Services;
using ServerRemote.App.Services.Hid;
using ServerRemote.App.ViewModels;
using ServerRemote.App.Views;

namespace ServerRemote.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				handlers.AddHandler(typeof(Components.KvmVideoView), typeof(Platforms.Android.KvmVideoViewHandler));
				// Custom entry handler: disables the fullscreen/extract mode of the soft keyboard,
				// so that no text field covers the live image in landscape.
				handlers.AddHandler(typeof(Components.HostKeyboardEntry), typeof(Platforms.Android.HostKeyboardEntryHandler));
#elif WINDOWS
				handlers.AddHandler(typeof(Components.KvmVideoView), typeof(Platforms.Windows.KvmVideoViewHandler));
#endif
			});

		// Services
		builder.Services.AddSingleton<ISettingsService, SettingsService>();
		builder.Services.AddSingleton<ServerApiClient>();
		builder.Services.AddSingleton<SystemMonitor>();
		builder.Services.AddSingleton<NanoKvmApiClient>();
		builder.Services.AddSingleton<NanoKvmMonitor>();
		// H.264 live stream (direct mode, hardware-decoded in the KvmVideoView).
		builder.Services.AddSingleton<H264StreamService>();
		// HID protocol: binary, verified against the official NanoKVM firmware 2.4.3.
		builder.Services.AddSingleton<IHidProtocol, BinaryHidProtocol>();
		builder.Services.AddSingleton<NanoKvmHidService>();

		// ViewModels (all share the SystemMonitor)
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddSingleton<ServicesViewModel>();
		builder.Services.AddSingleton<StorageViewModel>();
		builder.Services.AddSingleton<SensorsViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<NanoKvmSettingsViewModel>();
		builder.Services.AddSingleton<NanoKvmViewModel>();

		// Pages
		// Flyout pages as singletons: build once, then reuse when switching views
		// instead of rebuilding the (sometimes heavy) XAML tree on every navigation → fewer stalls.
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddSingleton<ServicesPage>();
		builder.Services.AddSingleton<StoragePage>();
		builder.Services.AddSingleton<SensorsPage>();
		builder.Services.AddSingleton<SettingsPage>();
		builder.Services.AddSingleton<NanoKvmPage>();
		// Detail pages pushed by route stay transient (created/destroyed each time).
		builder.Services.AddTransient<NanoKvmFullscreenPage>();
		builder.Services.AddTransient<NanoKvmSettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
