using BondAnalytics.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace BondAnalytics.Mobile.Pages;

public partial class StartupPage : ContentPage
{
    private readonly MobileNavigationService _navigation;
    private readonly ILogger<StartupPage> _logger;
    private bool _starting;

    public StartupPage(MobileNavigationService navigation, ILogger<StartupPage> logger)
    {
        InitializeComponent();
        _navigation = navigation;
        _logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await StartAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) => await StartAsync();

    private async Task StartAsync()
    {
        if (_starting)
            return;

        _starting = true;
        RetryButton.IsVisible = false;
        try
        {
            if (Window is null)
                throw new InvalidOperationException("Не удалось открыть окно приложения.");

            await _navigation.InitializeAsync(Window);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mobile application startup failed");
            StatusLabel.Text = "Не удалось открыть защищённое хранилище. Проверьте устройство и попробуйте снова.";
            RetryButton.IsVisible = true;
        }
        finally
        {
            _starting = false;
        }
    }
}
