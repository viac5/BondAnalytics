using BondAnalytics.Mobile.Pages;
using BondAnalytics.Mobile.Services;
using BondAnalytics.Mobile.ViewModels;
using Domain;
using Infrastructure;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Tinkoff.InvestApi;

namespace BondAnalytics.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		SQLitePCL.Batteries_V2.Init();
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<SecureTokenProvider>();
		builder.Services.AddSingleton<ITokenProvider>(services => services.GetRequiredService<SecureTokenProvider>());
		builder.Services.AddSingleton<Serilog.ILogger>(_ =>
			new LoggerConfiguration()
				.MinimumLevel.Information()
				.WriteTo.Sink(new AndroidLogSink())
				.CreateLogger());
		builder.Services.AddSingleton<MobileNavigationService>();
		builder.Services
			.AddGrpcClient<InvestApiClient>(options =>
				options.Address = new Uri("https://invest-public-api.tinkoff.ru:443"))
			.ConfigurePrimaryHttpMessageHandler(TinkoffApiHttpHandler.Create)
			.ConfigureChannel((services, options) =>
			{
				var token = services.GetRequiredService<SecureTokenProvider>().GetCachedToken();

				var credentials = CallCredentials.FromInterceptor((_, metadata) =>
				{
					metadata.Add("Authorization", $"Bearer {token}");
					metadata.Add("x-app-name", "tinkoff.invest-api-csharp-sdk");
					return Task.CompletedTask;
				});
				options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);
				options.MaxReceiveMessageSize = null;
				options.ServiceConfig = new ServiceConfig
				{
					MethodConfigs =
					{
						new MethodConfig
						{
							Names = { MethodName.Default },
							RetryPolicy = new RetryPolicy
							{
								MaxAttempts = 5,
								InitialBackoff = TimeSpan.FromSeconds(1),
								MaxBackoff = TimeSpan.FromSeconds(5),
								BackoffMultiplier = 1.5,
								RetryableStatusCodes = { StatusCode.Unavailable }
							}
						}
					}
				};
			});
		builder.Services.AddScoped<IPortfolioService>(services =>
			new TinkoffApiService(
				services.GetRequiredService<ITokenProvider>(),
				services.GetRequiredService<Serilog.ILogger>(),
				services.GetRequiredService<InvestApiClient>()));
		builder.Services.AddScoped<EfPortfolioRepository>(services =>
			new EfPortfolioRepository(Path.Combine(
				FileSystem.AppDataDirectory,
				"portfolio-history.db")));
		builder.Services.AddScoped<IPortfolioHistoryRepository>(
			services => services.GetRequiredService<EfPortfolioRepository>());
		builder.Services.AddScoped<IInvestmentPlanRepository>(
			services => services.GetRequiredService<EfPortfolioRepository>());

		builder.Services.AddScoped<AppViewModel>();
		builder.Services.AddScoped<AnalyticsViewModel>();
		builder.Services.AddScoped<InvestmentPlanViewModel>();
		builder.Services.AddScoped<MainPage>();
		builder.Services.AddScoped<ChartsPage>();
		builder.Services.AddScoped<AnalyticsPage>();
		builder.Services.AddScoped<InvestmentPlanPage>();
		builder.Services.AddScoped<SettingsPage>();
		builder.Services.AddScoped<OnboardingPage>();
		builder.Services.AddTransient<StartupPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

internal static class TinkoffApiHttpHandler
{
	public static HttpMessageHandler Create()
	{
		var handler = new HttpClientHandler();
		handler.ServerCertificateCustomValidationCallback =
			TinkoffServerCertificateValidator.Validate;
		return handler;
	}
}

internal sealed class AndroidLogSink : ILogEventSink
{
	public void Emit(LogEvent logEvent)
	{
		var message = logEvent.RenderMessage();
		if (logEvent.Exception is not null)
			message = $"{message}{Environment.NewLine}{logEvent.Exception}";

		var tag = "BondAnalytics";
		switch (logEvent.Level)
		{
			case LogEventLevel.Fatal:
			case LogEventLevel.Error:
				Android.Util.Log.Error(tag, message);
				break;
			case LogEventLevel.Warning:
				Android.Util.Log.Warn(tag, message);
				break;
			default:
				Android.Util.Log.Info(tag, message);
				break;
		}
	}
}
