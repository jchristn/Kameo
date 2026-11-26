namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Kameo.Core;

    /// <summary>
    /// Builds the tray icon's native menu: About, management, and for each harness a Lowest usage launcher (Claude Code
    /// and Codex with two or more native logins) and a submenu per profile with Launch... and Use as default. Every launch
    /// opens the launch options dialog, without the manager window unless a failure must be reported.
    /// </summary>
    internal sealed class TrayMenu
    {
        private readonly ProfileStore _Store;
        private readonly MainWindow _Window;
        private readonly Action _Show;
        private readonly Action _Quit;
        private string _Fingerprint = "";

        /// <summary>Creates a tray menu over a profile store and the manager window that reports launch failures.</summary>
        /// <param name="store">The local profile store.</param>
        /// <param name="window">The manager window used for launches, folder picking, and the About dialog.</param>
        /// <param name="show">Shows and activates the manager window.</param>
        /// <param name="quit">Quits the application.</param>
        internal TrayMenu(ProfileStore store, MainWindow window, Action show, Action quit)
        {
            _Store = store;
            _Window = window;
            _Show = show;
            _Quit = quit;
        }

        /// <summary>The menu attached to the tray icon. It is updated in place and never replaced.</summary>
        internal NativeMenu Menu { get; } = new NativeMenu();

        /// <summary>Rebuilds the menu items when profile names, kinds, or selections changed.</summary>
        internal void Refresh()
        {
            IReadOnlyList<Profile> profiles = _Store.List();
            string fingerprint = string.Join("|", profiles.Select(profile => profile.Id + profile.Name + profile.Kind + profile.IsActive));
            if (Menu.Items.Count > 0 && fingerprint == _Fingerprint) return;
            _Fingerprint = fingerprint;
            Menu.Items.Clear();
            Menu.Add(NativeMenus.Item("About Kameo", ShowAbout));
            Menu.Add(NativeMenus.Item("Manage profiles", _Show));
            Menu.Add(new NativeMenuItemSeparator());
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                NativeMenu choices = new NativeMenu();
                List<Profile> harnessProfiles = profiles.Where(profile => profile.Harness == harness).ToList();
                if (MainWindow.OffersLowestUsage(harness, harnessProfiles))
                {
                    choices.Add(NativeMenus.Item("Lowest usage...", () => _ = LaunchLowestAsync(harness)));
                    choices.Add(new NativeMenuItemSeparator());
                }
                foreach (Profile profile in harnessProfiles)
                {
                    NativeMenuItem use = NativeMenus.Item("Use as default", () => Select(profile));
                    use.ToggleType = MenuItemToggleType.CheckBox;
                    use.IsChecked = profile.IsActive;
                    use.IsEnabled = !profile.IsActive;
                    NativeMenuItem item = NativeMenus.Submenu(profile.Name,
                        NativeMenus.Item("Launch...", () => _ = LaunchAsync(profile.Id)),
                        new NativeMenuItemSeparator(),
                        use);
                    // The tick marks the profile that kameo commands and other launch shortcuts use.
                    item.ToggleType = MenuItemToggleType.Radio;
                    item.IsChecked = profile.IsActive;
                    choices.Add(item);
                }
                if (harnessProfiles.Count == 0) choices.Add(new NativeMenuItem("No profiles yet") { IsEnabled = false });
                Menu.Add(new NativeMenuItem(HarnessCatalog.Name(harness)) { Menu = choices });
            }
            Menu.Add(new NativeMenuItemSeparator());
            Menu.Add(NativeMenus.Item("Quit Kameo", _Quit));
        }

        private void ShowAbout()
        {
            _Show();
            _Window.ShowAbout();
        }

        private async Task LaunchAsync(string id)
        {
            // Only the options dialog comes forward, even when the manager is open behind other apps. The manager is
            // shown only so a failure message is visible.
            if (!await _Window.LaunchProfileAsync(id, true, CancellationToken.None)) _Show();
        }

        private async Task LaunchLowestAsync(HarnessKind harness)
        {
            if (!await _Window.LaunchLowestAsync(harness, true, CancellationToken.None)) _Show();
        }

        private void Select(Profile profile)
        {
            try
            {
                _Store.Select(profile.Id);
                _Window.Refresh();
                Refresh();
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or Microsoft.Data.Sqlite.SqliteException)
            {
                _Show();
                _Window.Report("Could not select that profile. Refresh and try again.");
            }
        }
    }
}
