namespace Kameo.Desktop
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    internal static class Ui
    {
        internal static TextBlock Text(string text, double size = 14, IBrush? color = null, FontWeight? weight = null) => new TextBlock
        {
            Text = text, FontSize = size, Foreground = color ?? Palette.Text,
            FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap
        };

        internal static StackPanel Stack(params Control[] children)
        {
            StackPanel panel = new StackPanel { Spacing = 8 };
            foreach (Control child in children) panel.Children.Add(child);
            return panel;
        }

        internal static StackPanel Row(params Control[] children)
        {
            StackPanel panel = Stack(children);
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = 8;
            return panel;
        }

        internal static Button Button(string label, Action action, bool primary = false)
        {
            Button button = new Button { Content = label, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            if (primary) button.Classes.Add("accent");
            button.Click += (_, _) => action();
            return button;
        }

        internal static Border Card(Control content, bool highlighted = false) => new Border
        {
            Background = highlighted ? Palette.AccentSoft : Palette.Surface,
            BorderBrush = highlighted ? Palette.Accent : Palette.Border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(13), Child = content
        };

        internal static Control Field(string label, Control input, string? hint = null)
        {
            StackPanel stack = Stack(Text(label, 13, weight: FontWeight.SemiBold), input);
            stack.Spacing = 6;
            if (hint != null) stack.Children.Add(Text(hint, 12, Palette.Muted));
            return stack;
        }

        internal static Border Badge(string text, bool active = false) => new Border
        {
            Background = active ? Palette.AccentSoft : Palette.Background, CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, 11, active ? Palette.Accent : Palette.Muted, FontWeight.SemiBold)
        };
    }
}
