namespace Kameo.Desktop
{
    using System.Collections.Generic;
    using System.Linq;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Media;
    using Avalonia.Styling;

    internal static class Palette
    {
        internal static bool Dark { get; private set; } = true;
        internal static IReadOnlyList<ColorScheme> Schemes { get; } = new[]
        {
            new ColorScheme("fern", "Fern", "#12634D", "#85DEBD", "#E2F3EB", "#203E38", "#F1F6F3", "#FCFEFC", "#111B18", "#1C2B25"),
            new ColorScheme("ocean", "Ocean", "#215EB3", "#8BBEFF", "#E5EFFD", "#20334D", "#F1F5FA", "#FCFDFF", "#101923", "#1C2A3A"),
            new ColorScheme("iris", "Iris", "#6944B2", "#C4ABF4", "#F0EAFB", "#352B48", "#F5F2FA", "#FEFCFF", "#1A1623", "#2A2337"),
            new ColorScheme("amber", "Amber", "#895508", "#EDC37A", "#FCF0D9", "#403522", "#FAF6EE", "#FFFDFA", "#1E1A12", "#30291D"),
            new ColorScheme("rose", "Rose", "#A33962", "#EEA4BC", "#FAE9EF", "#432B37", "#FBF2F5", "#FFFCFD", "#21171D", "#34252E"),
            new ColorScheme("graphite", "Graphite", "#475C6B", "#B3C7D3", "#E8EEF1", "#2B3943", "#F2F4F5", "#FFFFFF", "#171B1E", "#252C31"),
            new ColorScheme("seafoam", "Seafoam", "#16705D", "#8BDCC9", "#DCF4EC", "#234138", "#EFF9F5", "#FAFFFD", "#101F1A", "#1D3028"),
            new ColorScheme("lagoon", "Lagoon", "#076E7A", "#79D4DF", "#DFF3F6", "#1C3C43", "#F0F8FA", "#FAFEFF", "#101D21", "#1A2F35"),
            new ColorScheme("cobalt", "Cobalt", "#3852B5", "#A5B8FF", "#E9EDFD", "#2A3355", "#F2F4FC", "#FCFCFF", "#141A2D", "#212C43"),
            new ColorScheme("lavender", "Lavender", "#78509E", "#D7B7ED", "#F1E9F7", "#3C2C48", "#F9F3FC", "#FFFCFF", "#211927", "#32273B"),
            new ColorScheme("coral", "Coral", "#AA4634", "#F4AA96", "#FBE9E3", "#482F29", "#FCF4F0", "#FFFCFA", "#251A17", "#392923"),
            new ColorScheme("sand", "Sand", "#79602F", "#DBC394", "#F2EBD9", "#3F3828", "#F7F4EB", "#FFFEF8", "#201E17", "#322E23"),
            new ColorScheme("espresso", "Espresso", "#79523D", "#D9B496", "#F2E6DD", "#413128", "#F8F3ED", "#FFFCF7", "#211915", "#332820"),
            new ColorScheme("midnight", "Midnight", "#4A4CA6", "#B9BCFA", "#ECEBFA", "#2D2D4F", "#F4F4FB", "#FEFDFF", "#0D1020", "#1A2035"),
            new ColorScheme("paper", "Paper", "#4E5459", "#D1D5D8", "#EAECEB", "#333638", "#F7F7F3", "#FFFFFF", "#171819", "#27292B"),
            new ColorScheme("nord", "Nord", "#35647A", "#8FBCBB", "#E3EDF2", "#344854", "#EEF2F6", "#FCFDFF", "#242B36", "#303A49")
        };
        internal static ColorScheme Scheme { get; private set; } = Schemes[0];
        internal static IBrush Background => Brush(Dark ? Scheme.DarkBackground : Scheme.LightBackground);
        internal static IBrush Sidebar => Brush(Dark ? Scheme.DarkSurface : Scheme.LightSurface);
        internal static IBrush Surface => Brush(Dark ? Scheme.DarkSurface : Scheme.LightSurface);
        internal static IBrush Border => new SolidColorBrush(Mix(Color.Parse(Dark ? Scheme.DarkSurface : Scheme.LightSurface), Color.Parse(Dark ? "#A8B3BD" : "#667581"), Dark ? 0.24 : 0.25));
        internal static IBrush Text => Brush(Dark ? "#F0F2F5" : "#252B31");
        internal static IBrush Muted => Brush(Dark ? "#ACB7C4" : "#586572");
        internal static IBrush Accent => Brush(Dark ? Scheme.DarkAccent : Scheme.LightAccent);
        internal static IBrush AccentSoft => Brush(Dark ? Scheme.DarkTint : Scheme.LightTint);
        internal static IBrush Danger => Brush(Dark ? "#FFB0AE" : "#A82D37");
        internal static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
        private static Color Mix(Color background, Color foreground, double amount) => Color.FromRgb(
            (byte)(background.R + (foreground.R - background.R) * amount),
            (byte)(background.G + (foreground.G - background.G) * amount),
            (byte)(background.B + (foreground.B - background.B) * amount));

        internal static void Apply(Application app, bool dark, string? scheme = null)
        {
            Dark = dark;
            if (scheme != null) Scheme = Schemes.FirstOrDefault(candidate => candidate.Id == scheme) ?? Schemes[0];
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            app.Resources["SystemAccentColor"] = Color.Parse(dark ? Scheme.DarkAccent : Scheme.LightAccent);
            app.Resources["ControlCornerRadius"] = new CornerRadius(7);
            app.Resources["OverlayCornerRadius"] = new CornerRadius(10);
            string[] backgrounds = { "ButtonBackground", "TextControlBackground", "TextControlBackgroundFocused", "ComboBoxBackground", "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground", "ToolTipBackground" };
            foreach (string key in backgrounds) app.Resources[key] = Surface;
            foreach (string key in new[] { "ButtonForeground", "TextControlForeground", "ComboBoxForeground", "ToolTipForeground" }) app.Resources[key] = Text;
            foreach (string key in new[] { "ButtonBorderBrush", "TextControlBorderBrush", "ComboBoxBorderBrush", "ComboBoxDropDownBorderBrush" }) app.Resources[key] = Border;
            foreach (string key in new[] { "ButtonBackgroundPointerOver", "ComboBoxBackgroundPointerOver", "ListBoxItemBackgroundPointerOver", "MenuFlyoutItemBackgroundPointerOver" }) app.Resources[key] = AccentSoft;
            app.Resources["TextControlPlaceholderForeground"] = Muted;
            app.Resources["TextControlBorderBrushFocused"] = Accent;
            app.Resources["AccentButtonBackground"] = Accent;
            app.Resources["AccentButtonForeground"] = dark ? Brush("#10251E") : Brushes.White;
            app.Resources["AccentButtonBackgroundPointerOver"] = Accent;
            app.Resources["AccentButtonForegroundPointerOver"] = dark ? Brush("#10251E") : Brushes.White;
        }

        internal static void InstallStyles(Application app)
        {
            app.Styles.Add(new Style(selector => selector.OfType<Button>())
            {
                Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)), new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)), new Setter(TemplatedControl.FontSizeProperty, 12d) }
            });
            app.Styles.Add(new Style(selector => selector.OfType<DropDownButton>())
            {
                Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)), new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 3, 6, 3)), new Setter(TemplatedControl.FontSizeProperty, 12d) }
            });
            app.Styles.Add(new Style(selector => selector.OfType<TextBox>())
            {
                Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)), new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 7)), new Setter(TemplatedControl.FontSizeProperty, 13d) }
            });
            app.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
            { Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) } });
        }
    }
}
