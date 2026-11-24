namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Avalonia.Controls;
    using Kameo.Core;

    /// <summary>Builds the tray icon's native menu: About, management, per-harness launches and selection, and Quit.</summary>
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
                Profile? active = profiles.FirstOrDefault(profile => profile.Harness == harness && profile.IsActive);
                NativeMenu choices = new NativeMenu();
                NativeMenuItem launch = NativeMenus.Item(active == null ? "No profile selected" : "Launch " + active.Name, () => Launch(harness));
                launch.IsEnabled = active != null;
                NativeMenuItem launchIn = NativeMenus.Item("Launch in...", () => LaunchIn(harness));
                launchIn.IsEnabled = active != null;
                choices.Add(launch);
                choices.Add(launchIn);
                choices.Add(new NativeMenuItemSeparator());
                List<Profile> harnessProfiles = profiles.Where(profile => profile.Harness == harness).ToList();
                foreach (Profile profile in harnessProfiles)
                {
                    NativeMenuItem item = new NativeMenuItem(profile.Name) { ToggleType = MenuItemToggleType.Radio, IsChecked = profile.IsActive };
                    item.Click += (_, _) => Select(profile);
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

        private void Launch(HarnessKind harness)
        {
            // Launch without opening the manager; show it only so a failure message is visible.
            if (!_Window.LaunchSelected(harness)) _Show();
        }

        private void LaunchIn(HarnessKind harness)
        {
            // The folder picker belongs to the manager window, so it must be visible first.
            _Show();
            _Window.LaunchSelectedIn(harness);
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
