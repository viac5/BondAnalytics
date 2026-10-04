using App.ViewModels;
using Domain;
using Infrastructure;
using App.Localization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.IO;
using System.Windows;

namespace BondAnalytics.App
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        public App()
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BondAnalytics",
                "Logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(logDirectory, "bond-analytics-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var services = new ServiceCollection();

            services.AddSingleton(provider =>
            {
                var localization = new LocalizationManager();
                LocalizationManager.SetCurrent(localization);
                return localization;
            });

            // Регистрация сервисов
            services.AddSingleton<ITokenProvider, EnvironmentTokenProvider>();
            services.AddSingleton<IPortfolioService, TinkoffApiService>();
            services.AddSingleton<EfPortfolioRepository>();
            services.AddSingleton<IPortfolioHistoryRepository>(provider => provider.GetRequiredService<EfPortfolioRepository>());
            services.AddSingleton<IInvestmentPlanRepository>(provider => provider.GetRequiredService<EfPortfolioRepository>());
            services.AddSingleton<Serilog.ILogger>(Log.Logger);

            // ViewModels
            services.AddSingleton<InvestmentPlanViewModel>();
            services.AddSingleton<PortfolioPeriodAnalyticsViewModel>();
            services.AddSingleton<MainViewModel>();

            // Окна
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                Log.Information("Bond Analytics starting");
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
                MessageBox.Show(
                    LocalizationManager.Current.Format("Startup.Message", ex.Message),
                    LocalizationManager.Current.Get("Startup.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Bond Analytics stopped with exit code {ExitCode}", e.ApplicationExitCode);
            _serviceProvider.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
