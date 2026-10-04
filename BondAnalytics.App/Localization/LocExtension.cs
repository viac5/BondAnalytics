using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace App.Localization
{
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Current,
                Mode = BindingMode.OneWay
            }.ProvideValue(serviceProvider);
        }
    }
}
