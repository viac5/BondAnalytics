using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Markup;

namespace App.Localization
{
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        private IReadOnlyDictionary<string, string> _resources;
        private CultureInfo _culture = CultureInfo.GetCultureInfo("ru-RU");

        public static LocalizationManager Current { get; private set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? LanguageChanged;

        public string this[string key] => _resources.TryGetValue(key, out var value) ? value : key;
        public string LanguageButtonText => _culture.TwoLetterISOLanguageName == "ru" ? "EN" : "RU";
        public CultureInfo Culture => _culture;
        public XmlLanguage XmlLanguage => XmlLanguage.GetLanguage(_culture.IetfLanguageTag);

        public IReadOnlyList<string> Filters => new[]
        {
            this["Filter.All"], this["Filter.Government"], this["Filter.Corporate"], this["Filter.Other"]
        };

        public IReadOnlyList<string> Charts => ChartKeys.Select(key => this[key]).ToArray();

        public IReadOnlyList<string> Periods => new[]
        {
            this["Plan.Range.1Month"], this["Plan.Range.3Months"], this["Plan.Range.6Months"],
            this["Plan.Range.Year"], this["Plan.Range.10Years"], this["Plan.Range.Custom"]
        };

        public static IReadOnlyList<string> ChartKeys { get; } = new[]
        {
            "Chart.Coupons", "Chart.Value", "Chart.Profit", "Chart.Yield", "Chart.Nkd", "Chart.AnnualCoupons", "Chart.Allocation"
        };

        public static IReadOnlyList<string> ChartDescriptionKeys { get; } = new[]
        {
            "Chart.Coupons.Description", "Chart.Value.Description", "Chart.Profit.Description", "Chart.Yield.Description",
            "Chart.Nkd.Description", "Chart.AnnualCoupons.Description", "Chart.Allocation.Description"
        };

        public LocalizationManager()
        {
            _resources = LoadStrings("ru");
        }

        public static void SetCurrent(LocalizationManager localizationManager)
        {
            Current = localizationManager ?? throw new ArgumentNullException(nameof(localizationManager));
        }

        public void ToggleLanguage()
        {
            _culture = _culture.TwoLetterISOLanguageName == "ru"
                ? CultureInfo.GetCultureInfo("en-US")
                : CultureInfo.GetCultureInfo("ru-RU");
            _resources = LoadStrings(_culture.TwoLetterISOLanguageName);
            CultureInfo.CurrentUICulture = _culture;
            OnPropertyChanged("Item[]");
            OnPropertyChanged(nameof(LanguageButtonText));
            OnPropertyChanged(nameof(XmlLanguage));
            OnPropertyChanged(nameof(Filters));
            OnPropertyChanged(nameof(Charts));
            OnPropertyChanged(nameof(Periods));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Get(string key) => this[key];

        private static IReadOnlyDictionary<string, string> LoadStrings(string language)
        {
            var assembly = typeof(LocalizationManager).GetTypeInfo().Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith($"Strings.{language}.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Could not open localization catalog Strings.{language}.json.");
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                    ?? throw new InvalidOperationException($"Localization catalog Strings.{language}.json is empty.");
            }

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Localization", $"Strings.{language}.json"),
                Path.Combine(AppContext.BaseDirectory, $"Strings.{language}.json"),
                Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "Localization", $"Strings.{language}.json")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                var content = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(content)
                    ?? throw new InvalidOperationException($"Localization catalog Strings.{language}.json is empty.");
            }

            throw new InvalidOperationException($"Localization catalog Strings.{language}.json was not embedded and was not found in output directory.");
        }

        public string Format(string key, params object?[] arguments) =>
            string.Format(_culture, this[key], arguments);

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
