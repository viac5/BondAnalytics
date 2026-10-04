using BondAnalytics.Mobile.Services;
using Domain;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.Logging;

namespace BondAnalytics.Mobile.Pages;

public partial class OnboardingPage : ContentPage
{
    private readonly ITokenProvider _tokenProvider;
    private readonly MobileNavigationService _navigation;
    private readonly ILogger<OnboardingPage> _logger;

    public OnboardingPage(
        ITokenProvider tokenProvider,
        MobileNavigationService navigation,
        ILogger<OnboardingPage> logger)
    {
        InitializeComponent();
        _tokenProvider = tokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var token = TokenEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorLabel.Text = "Вставьте токен, чтобы продолжить.";
            ErrorLabel.IsVisible = true;
            return;
        }

        SaveButton.IsEnabled = false;
        ErrorLabel.IsVisible = false;
        try
        {
            await _tokenProvider.SaveTokenAsync(token);
            await _navigation.ShowPortfolioAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the T-Invest API token");
            ErrorLabel.Text = $"Не удалось сохранить токен: {ex.Message}";
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnOpenInvestClicked(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.OpenAsync("https://www.tbank.ru/invest/settings/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the T-Invest settings page");
            ErrorLabel.Text = "Не удалось открыть настройки T‑Invest. Перейдите к разделу API вручную.";
            ErrorLabel.IsVisible = true;
        }
    }
}
