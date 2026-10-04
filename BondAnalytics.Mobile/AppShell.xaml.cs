using BondAnalytics.Mobile.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BondAnalytics.Mobile;

public partial class AppShell : Shell
{
	public AppShell(IServiceProvider services)
	{
		InitializeComponent();

		var tabs = new TabBar { Route = "main" };
		tabs.Items.Add(CreateTab("Портфель", "portfolio",
			() => services.GetRequiredService<MainPage>()));
		tabs.Items.Add(CreateTab("Обзор", "charts",
			() => services.GetRequiredService<ChartsPage>()));
		tabs.Items.Add(CreateTab("Аналитика", "analytics",
			() => services.GetRequiredService<AnalyticsPage>()));
		tabs.Items.Add(CreateTab("План", "plan",
			() => services.GetRequiredService<InvestmentPlanPage>()));
		tabs.Items.Add(CreateTab("Настройки", "settings",
			() => services.GetRequiredService<SettingsPage>()));
		Items.Add(tabs);
		Items.Add(new ShellContent
		{
			Title = "Подключение",
			Route = "onboarding",
			Content = services.GetRequiredService<OnboardingPage>()
		});
	}

	private static Tab CreateTab(string title, string route, Func<Page> createPage)
	{
		var tab = new Tab { Title = title, Route = route };
		tab.Icon = ImageSource.FromFile($"{route}.png");
		tab.Items.Add(new ShellContent
		{
			Title = title,
			Route = $"{route}Page",
			ContentTemplate = new DataTemplate(createPage)
		});
		return tab;
	}
}
