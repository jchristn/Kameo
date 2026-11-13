namespace Kameo.Desktop
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Kameo.Core;

    internal sealed class AppearanceWindow : Window
    {
        private readonly ProfileStore _Store;
        private readonly Action _Refresh;

        internal AppearanceWindow(ProfileStore store, Action refresh)
        {
            _Store = store; _Refresh = refresh;
            Title = "Appearance"; Width = 520; CanResize = false; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Build();
        }

        private void Build()
        {
            Background = Palette.Background;
            StackPanel modes = Ui.Row(Ui.Button("Light", () => ChooseMode(false), !Palette.Dark), Ui.Button("Dark", () => ChooseMode(true), Palette.Dark));
            Grid choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
            for (int index = 0; index < Palette.Schemes.Count; index++)
            {
                ColorScheme scheme = Palette.Schemes[index];
                Grid previews = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 3 };
                previews.Children.Add(Preview(scheme, false));
                Border darkPreview = Preview(scheme, true); Grid.SetColumn(darkPreview, 1); previews.Children.Add(darkPreview);
                StackPanel sample = Ui.Stack(previews, Ui.Text(scheme.Name, 12, weight: FontWeight.SemiBold));
                Button button = new Button { Content = sample, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9), BorderBrush = scheme.Id == Palette.Scheme.Id ? Palette.Accent : Palette.Border, BorderThickness = new Thickness(scheme.Id == Palette.Scheme.Id ? 2 : 1) };
                button.Click += (_, _) => { Apply(Palette.Dark, scheme.Id); Close(); };
                Grid.SetColumn(button, index % 4); Grid.SetRow(button, index / 4); choices.Children.Add(button);
            }
            StackPanel content = Ui.Stack(Ui.Text("Appearance", 22, weight: FontWeight.SemiBold), modes, choices, Ui.Text("16 palettes, each with light and dark variants. Choose one to apply.", 12, Palette.Muted));
            content.Spacing = 14; content.Margin = new Thickness(20); Content = content;
        }

        private void ChooseMode(bool dark) { Apply(dark, Palette.Scheme.Id); Build(); }
        private static Border Preview(ColorScheme scheme, bool dark)
        {
            Border line = new Border { Height = 5, Width = 20, HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.Brush(dark ? scheme.DarkAccent : scheme.LightAccent), CornerRadius = new CornerRadius(2) };
            Border card = new Border { Height = 10, Background = Palette.Brush(dark ? scheme.DarkSurface : scheme.LightSurface), CornerRadius = new CornerRadius(2) };
            return new Border { Height = 34, Padding = new Thickness(5), CornerRadius = new CornerRadius(4), Background = Palette.Brush(dark ? scheme.DarkBackground : scheme.LightBackground), Child = new StackPanel { Spacing = 4, Children = { line, card } } };
        }
        private void Apply(bool dark, string scheme)
        {
            Palette.Apply(Application.Current!, dark, scheme);
            _Store.SetPreference("theme", dark ? "dark" : "light");
            _Store.SetPreference("palette", scheme);
            _Refresh();
        }
    }
}
