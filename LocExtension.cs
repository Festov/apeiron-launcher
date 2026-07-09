using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Apeiron.Services;

namespace Apeiron;

[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget target)
            return LocalizationService.T(Key);

        if (target.TargetObject is not DependencyObject || target.TargetProperty is not DependencyProperty)
            return LocalizationService.T(Key);

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider)!;
    }
}
