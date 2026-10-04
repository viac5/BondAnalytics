namespace BondAnalytics.Mobile;

public partial class App : Application
{
	private readonly Pages.StartupPage _startupPage;

	public App(Pages.StartupPage startupPage)
	{
		_startupPage = startupPage;
		InitializeComponent();
		var savedTheme = Preferences.Get("app-theme", "Dark");
		ThemeManager.Apply(savedTheme == "Light" ? AppTheme.Light : AppTheme.Dark, persist: false);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_startupPage);
	}
}

public static class ThemeManager
{
	public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
	public static event EventHandler? ThemeChanged;

	public static void Apply(AppTheme theme, bool persist = true)
	{
		CurrentTheme = theme;
		if (Application.Current is { } application)
			application.UserAppTheme = theme;
		if (persist)
			Preferences.Set("app-theme", theme == AppTheme.Light ? "Light" : "Dark");

#if ANDROID
		var light = theme == AppTheme.Light;
		var statusBar = light ? "#F4F6F8" : "#07111F";
		var navigationBar = light ? "#FFFFFF" : "#0B1724";
		var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
		window?.SetStatusBarColor(Android.Graphics.Color.ParseColor(statusBar));
		window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor(navigationBar));
		if (window?.DecorView is { } decorView)
		{
			var insetsController = AndroidX.Core.View.WindowCompat.GetInsetsController(window, decorView);
			insetsController.AppearanceLightStatusBars = light;
			insetsController.AppearanceLightNavigationBars = light;
		}
#endif
		ThemeChanged?.Invoke(null, EventArgs.Empty);
	}
}