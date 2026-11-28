namespace Kameo.Desktop
{
    using System;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Avalonia.Threading;
    using Kameo.Core;

    internal sealed class AboutWindow : Window
    {
        private readonly Bitmap _Logo;
        private readonly DispatcherTimer? _Timer;
        internal string LogoResource { get; }

        internal AboutWindow(bool splash = false)
        {
            Title = splash ? "Kameo" : "About Kameo";
            Width = 360; SizeToContent = SizeToContent.Height; CanResize = false;
            ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.Background; Foreground = Palette.Text;
            LogoResource = Palette.Dark ? "Kameo.Logo" : "Kameo.LogoDark";
            using Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResource)!;
            _Logo = new Bitmap(image);
            Image mark = new Image { Name = "ProductLogo", Source = _Logo, Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock name = Ui.Text("Kameo", 30, weight: FontWeight.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            Border version = Ui.Badge(ProductInfo.Version + "  ·  " + ProductInfo.Stage.ToUpperInvariant(), true);
            version.HorizontalAlignment = HorizontalAlignment.Center;
            HyperlinkButton repository = new HyperlinkButton
            {
                Content = "github.com/jchristn/Kameo", NavigateUri = new Uri("https://github.com/jchristn/Kameo"),
                Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0)
            };
            TextBlock copyright = Ui.Text("Copyright (c) 2026 Joel Christner", 12, Palette.Muted);
            copyright.HorizontalAlignment = HorizontalAlignment.Center;
            copyright.Margin = new Thickness(0, 12, 0, 0);
            TextBlock license = Ui.Text("MIT License", 12, Palette.Muted);
            license.HorizontalAlignment = HorizontalAlignment.Center;
            license.Margin = new Thickness(0, 10, 0, 0);
            Button close = Ui.Button("Close", Close);
            close.HorizontalAlignment = HorizontalAlignment.Center; close.MinWidth = 90; close.Margin = new Thickness(0, 12, 0, 0);
            close.HorizontalContentAlignment = HorizontalAlignment.Center; close.VerticalContentAlignment = VerticalAlignment.Center;
            StackPanel content = Ui.Stack(mark, name, version, repository, copyright, license, close);
            content.Margin = new Thickness(28, 20, 28, 24);
            Content = content;
            if (splash)
            {
                _Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _Timer.Tick += (_, _) => Close();
                Opened += (_, _) => _Timer.Start();
            }
            Closed += (_, _) => { _Timer?.Stop(); _Logo.Dispose(); };
        }
    }
}
