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
        private readonly NativeMenu _Menu = new NativeMenu();
        private string _MenuFingerprint = "";

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
                RefreshTray();
                _Tray = new TrayIcon { Menu = _Menu, Icon = new WindowIcon(icon), ToolTipText = "Kameo - agent profiles", IsVisible = true };
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
                if (!(desktop.Args?.Contains("--tray") == true) || OperatingSystem.IsLinux()) _Window.Show();
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
        private void RefreshTray()
        {
            if (_Store == null) return;
            System.Collections.Generic.IReadOnlyList<Profile> profiles = _Store.List();
            string fingerprint = string.Join("|", profiles.Select(profile => profile.Id + profile.Name + profile.IsActive));
            if (_Menu.Items.Count > 0 && fingerprint == _MenuFingerprint) return;
            _MenuFingerprint = fingerprint;
            NativeMenu menu = _Menu;
            menu.Items.Clear();
            NativeMenuItem manage = new NativeMenuItem("Manage profiles");
            manage.Click += (_, _) => Show();
            menu.Add(manage);
            menu.Add(new NativeMenuItemSeparator());
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                NativeMenu choices = new NativeMenu();
                foreach (Profile profile in profiles.Where(profile => profile.Harness == harness))
                {
                    NativeMenuItem item = new NativeMenuItem(profile.Name) { ToggleType = MenuItemToggleType.Radio, IsChecked = profile.IsActive };
                    item.Click += (_, _) =>
                    {
                        try { _Store.Select(profile.Id); _Window?.Refresh(); RefreshTray(); }
                        catch (Exception error) when (error is InvalidOperationException or System.Collections.Generic.KeyNotFoundException or Microsoft.Data.Sqlite.SqliteException)
                        { Show(); _Window?.Report("Could not select that profile. Refresh and try again."); }
                    };
                    choices.Add(item);
                }
                if (choices.Items.Count == 0) choices.Add(new NativeMenuItem("No profiles yet") { IsEnabled = false });
                menu.Add(new NativeMenuItem(HarnessCatalog.Name(harness)) { Menu = choices });
            }
            menu.Add(new NativeMenuItemSeparator());
            NativeMenuItem quit = new NativeMenuItem("Quit Kameo");
            quit.Click += (_, _) => Quit();
            menu.Add(quit);
        }
    }
}
