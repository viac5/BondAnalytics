using App.ViewModels;
using Domain;
using Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace BondAnalytics.App
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();

            // Регистрация сервисов
            services.AddSingleton<ITokenProvider, EnvironmentTokenProvider>();
            services.AddSingleton<IPortfolioService, TinkoffApiService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();

            // Окна
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}
