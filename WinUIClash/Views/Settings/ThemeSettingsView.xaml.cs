using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class ThemeSettingsView : UserControl
{
    public ThemeSettingsViewModel ViewModel { get; }

    public ThemeSettingsView()
    {
        ViewModel = ServiceLocator.Get<ThemeSettingsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => { Bindings.Update(); UpdateColorSelection(); };
        Unloaded += (_, _) => Bindings.StopTracking();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ThemeSettingsViewModel.ThemeColor color)
        {
            var index = Array.IndexOf(ViewModel.PrimaryColors, color);
            if (index >= 0) ViewModel.PrimaryColorIndex = index;
            UpdateColorSelection();
        }
    }

    private void ColorsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        UpdateColorBorder(args.Element, args.Index);
    }

    private void UpdateColorSelection()
    {
        for (int i = 0; i < ViewModel.PrimaryColors.Length; i++)
        {
            var element = ColorsRepeater.TryGetElement(i);
            if (element != null) UpdateColorBorder(element, i);
        }
    }

    private void UpdateColorBorder(UIElement element, int index)
    {
        if (element is Button btn)
        {
            if (index == ViewModel.PrimaryColorIndex)
            {
                var blackBrush = new SolidColorBrush(Colors.Black);
                btn.BorderThickness = new Thickness(2);
                btn.BorderBrush = blackBrush;
                btn.Resources["ButtonBorderBrushPointerOver"] = blackBrush;
                btn.Resources["ButtonBorderBrushPressed"] = blackBrush;
            }
            else
            {
                var transparentBrush = new SolidColorBrush(Colors.Transparent);
                btn.BorderThickness = new Thickness(0);
                btn.BorderBrush = null;
                btn.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
                btn.Resources["ButtonBorderBrushPressed"] = transparentBrush;
            }
        }
    }

    private void CustomColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
    {
        // Preview only — applied when user clicks the apply button
    }

    private void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var color = CustomColorPicker.Color;
        ViewModel.ApplyCustomAccentColor(color);
    }
}
