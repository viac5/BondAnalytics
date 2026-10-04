using BondAnalytics.Mobile.ViewModels;
using Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BondAnalytics.Mobile.Services;

public sealed class MobileNavigationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITokenProvider _tokenProvider;
    private IServiceScope? _scope;
    private Window? _window;

    public MobileNavigationService(IServiceScopeFactory scopeFactory, ITokenProvider tokenProvider)
    {
        _scopeFactory = scopeFactory;
        _tokenProvider = tokenProvider;
    }

    public async Task InitializeAsync(Window window)
    {
        _window = window;
        await ReplaceShellAsync(string.IsNullOrWhiteSpace(await _tokenProvider.GetTokenAsync()));
    }

    public Task ShowOnboardingAsync() => ReplaceShellAsync(showOnboarding: true);

    public Task ShowPortfolioAsync() => ReplaceShellAsync(showOnboarding: false);

    private async Task ReplaceShellAsync(bool showOnboarding)
    {
        var nextScope = _scopeFactory.CreateScope();
        var shell = new AppShell(nextScope.ServiceProvider);
        var previousScope = _scope;
        _scope = nextScope;
        if (_window is not null)
            _window.Page = shell;
        previousScope?.Dispose();

        await shell.GoToAsync(showOnboarding ? "//onboarding" : "//main/portfolio/portfolioPage");

        if (!showOnboarding)
            await nextScope.ServiceProvider.GetRequiredService<AppViewModel>().InitializeAsync();
    }
}
