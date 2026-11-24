namespace Kameo.Desktop
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Themes.Fluent;
    using Avalonia.Threading;
    using Kameo.Core;

    /// <summary>Kameo's native desktop and tray lifetime.</summary>
    public sealed class App : Application
    {
        private ProfileStore? _Store;
        private MainWindow? _Window;
        private TrayIcon? _Tray;
        private DispatcherTimer? _Timer;
        private bool _Quitting;
        private TrayMenu? _TrayMenu;

        /// <inheritdoc />
        public override void Initialize()
        {
            Name = "Kameo";
            Styles.Add(new FluentTheme());
            Palette.InstallStyles(this);
            if (OperatingSystem.IsMacOS())
            {
                NativeMenu applicationMenu = new NativeMenu();
                applicationMenu.Add(NativeMenus.Item("About Kameo", () => { Show(); _Window?.ShowAbout(); }));
                applicationMenu.Add(new NativeMenuItemSeparator());
                applicationMenu.Add(NativeMenus.Item("Manage Profiles...", Show, Avalonia.Input.Key.OemComma));
                // Avalonia supplies the standard Services, Hide, Hide Others and Quit items.
                NativeMenu.SetMenu(this, applicationMenu);
            }
        }

        /// <inheritdoc />
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _Store = new ProfileStore();
                Palette.Apply(this, _Store.Preference("theme") != "light", _Store.Preference("palette") ?? "fern");
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.ShutdownRequested += (_, _) => _Quitting = true;
                _Window = new MainWindow(_Store, RefreshTray, Quit);
                desktop.MainWindow = _Window;
                _Window.Closing += (_, args) =>
                {
                    if (!_Quitting && _Tray?.IsVisible == true && !OperatingSystem.IsLinux()) { args.Cancel = true; _Window.Hide(); }
                    else if (!_Quitting) Quit();
                };
                using Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kameo.Logo")!;
                _TrayMenu = new TrayMenu(_Store, _Window, Show, Quit);
                RefreshTray();
                _Tray = new TrayIcon { Menu = _TrayMenu.Menu, Icon = new WindowIcon(icon), ToolTipText = "Kameo - agent profiles", IsVisible = true };
                _Tray.Clicked += (_, _) => { if (!OperatingSystem.IsMacOS()) Show(); };
                TrayIcon.SetIcons(this, new TrayIcons { _Tray });
                _Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _Timer.Tick += (_, _) =>
                {
                    try
                    {
                        if (File.Exists(Path.Combine(_Store.Root, "stop.request"))) { File.Delete(Path.Combine(_Store.Root, "stop.request")); Quit(); return; }
                        if (File.Exists(Path.Combine(_Store.Root, "show.request"))) { File.Delete(Path.Combine(_Store.Root, "show.request")); Show(); }
                        RefreshTray();
                        _Window.RefreshIfChanged();
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
                    { _Window.Report("Unable to refresh profiles. Check access to the Kameo data directory."); }
                };
                _Timer.Start();
                desktop.Exit += (_, _) => { _Timer.Stop(); _Tray.Dispose(); };
                // Always show the manager on platforms where a tray host may be unavailable (including WSLg).
                if (!(desktop.Args?.Contains("--tray") == true) || OperatingSystem.IsLinux())
                {
                    _Window.Show();
                    new AboutWindow(splash: true).Show(_Window);
                }
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                    activatable.Activated += (_, _) => Show();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private void Show() { _Window?.Show(); if (_Window != null) _Window.WindowState = WindowState.Normal; _Window?.Activate(); }
        private void Quit()
        {
            _Quitting = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        private void RefreshTray() => _TrayMenu?.Refresh();
    }
}
