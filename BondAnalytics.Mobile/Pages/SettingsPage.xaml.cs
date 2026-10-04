using BondAnalytics.Mobile.Services;
using Domain;
using Microsoft.Extensions.Logging;

namespace BondAnalytics.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SecureTokenProvider _tokenProvider;
    private readonly MobileNavigationService _navigation;
    private readonly ILogger<SettingsPage> _logger;

    public string TokenStatusText { get; private set; } = "Токен сохранён";
    public bool IsLightTheme => ThemeManager.CurrentTheme == AppTheme.Light;

    public SettingsPage(
        SecureTokenProvider tokenProvider,
        MobileNavigationService navigation,
        ILogger<SettingsPage> logger)
    {
        InitializeComponent();
        _tokenProvider = tokenProvider;
        _navigation = navigation;
        _logger = logger;
        BindingContext = this;
        ThemeSwitch.IsToggled = IsLightTheme;
    }

    private void OnThemeToggled(object? sender, ToggledEventArgs e)
    {
        ThemeManager.Apply(e.Value ? AppTheme.Light : AppTheme.Dark);
        OnPropertyChanged(nameof(IsLightTheme));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            TokenStatusText = string.IsNullOrWhiteSpace(await _tokenProvider.GetTokenAsync())
                ? "Токен не сохранён"
                : "Токен сохранён";
            OnPropertyChanged(nameof(TokenStatusText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not inspect the saved API token");
            StatusLabel.Text = $"Не удалось проверить защищённое хранилище: {ex.Message}";
            StatusLabel.IsVisible = true;
        }
    }

    private async void OnResetTokenClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Сбросить токен?",
            "Текущий токен будет удалён с устройства. Для следующего подключения понадобится выпустить или вставить токен снова.",
            "Сбросить",
            "Отмена");
        if (!confirmed)
            return;

        try
        {
            await _tokenProvider.ClearTokenAsync();
            await _navigation.ShowOnboardingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not clear the saved API token");
            StatusLabel.Text = $"Не удалось сбросить токен: {ex.Message}";
            StatusLabel.IsVisible = true;
        }
    }
}
