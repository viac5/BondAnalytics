using BondAnalytics.Mobile.ViewModels;
using Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BondAnalytics.Mobile.Services;

public sealed class MobileNavigationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITokenProvider _tokenProvider;
    private readonly ILogger<MobileNavigationService> _logger;
    private IServiceScope? _scope;
    private Window? _window;

    public MobileNavigationService(
        IServiceScopeFactory scopeFactory,
        ITokenProvider tokenProvider,
        ILogger<MobileNavigationService> logger)
    {
        _scopeFactory = scopeFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
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
        {
            var portfolio = nextScope.ServiceProvider.GetRequiredService<AppViewModel>();
            await portfolio.InitializeAsync();
            _ = PreloadPagesAsync(nextScope.ServiceProvider, portfolio.InitialRefreshTask);
        }
    }

    private async Task PreloadPagesAsync(IServiceProvider services, Task initialRefreshTask)
    {
        try
        {
            await initialRefreshTask;
            await services.GetRequiredService<AnalyticsViewModel>().LoadAsync();
            await services.GetRequiredService<InvestmentPlanViewModel>().LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not preload analytics and investment plan pages");
        }
    }
}
