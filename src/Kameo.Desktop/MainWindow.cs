namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Input.Platform;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Avalonia.Platform.Storage;
    using Kameo.Core;

    /// <summary>The profile manager, organized by harness with explicit selection and launch actions.</summary>
    public sealed class MainWindow : Window
    {
        private readonly ProfileStore _Store;
        private readonly Action _Changed;
        private readonly Action _Quit;
        private readonly Func<CancellationToken, Task<string?>> _PickFolder;
        private readonly Action<string, bool, string> _OpenTerminal;
        private HarnessKind _Harness = HarnessKind.Claude;
        private TextBox? _Search;
        private StackPanel? _Profiles;
        private TextBlock? _Status;
        private string _Fingerprint = "";
        private string _Workspace;
        private Bitmap? _Logo;

        /// <summary>Creates the manager for a local profile store.</summary>
        public MainWindow(ProfileStore store, Action changed, Action quit)
            : this(store, changed, quit, null, null) { }

        internal MainWindow(ProfileStore store, Action changed, Action quit,
            Func<CancellationToken, Task<string?>>? pickFolder, Action<string, bool, string>? openTerminal)
        {
            _Store = store;
            _Changed = changed;
            _Quit = quit;
            _PickFolder = pickFolder ?? PickFolderAsync;
            _OpenTerminal = openTerminal ?? ((id, login, directory) => TerminalLauncher.Open(_Store, id, login, directory));
            _Workspace = store.Preference("workspace") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(_Workspace)) _Workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Title = "Kameo";
            Width = 800; Height = 550; MinWidth = 700; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Closed += (_, _) => _Logo?.Dispose();
            InstallNativeMenus();
            Refresh();
        }

        /// <summary>Displays the local product and version information.</summary>
        public void ShowAbout()
        {
            Window about = new Window { Title = "About Kameo", Width = 450, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Palette.Background };
            StackPanel content = Ui.Stack(Ui.Text("Kameo", 32, weight: FontWeight.SemiBold), Ui.Badge("0.1.0  ·  ALPHA", true),
                Ui.Text("Your agent identities, in one place.", 16),
                Ui.Text("A local credential and profile manager for Claude Code, Codex, Gemini CLI, and Mux.\n\nCopyright (c) 2026 Joel Christner\nMIT License", 13, Palette.Muted),
                Ui.Button("Close", about.Close, true));
            content.Margin = new Thickness(30); about.Content = content;
            _ = about.ShowDialog(this);
        }

        private void InstallNativeMenus()
        {
            NativeMenu bar = new NativeMenu();
            bar.Add(NativeMenus.Submenu("File",
                NativeMenus.Item("Add Profile...", () => _ = EditAsync(null, CancellationToken.None), Avalonia.Input.Key.N),
                NativeMenus.Item("Choose Launch Directory...", () => _ = ChooseDirectoryAsync(null, CancellationToken.None)),
                new NativeMenuItemSeparator(), NativeMenus.Item("Close Window", Close, Avalonia.Input.Key.W)));
            bar.Add(NativeMenus.Edit(this));
            NativeMenu harnesses = new NativeMenu();
            foreach (HarnessKind harness in HarnessCatalog.All)
                harnesses.Add(NativeMenus.Item(HarnessCatalog.Name(harness), () => { _Harness = harness; Refresh(); }));
            bar.Add(new NativeMenuItem("Profiles") { Menu = harnesses });
            bar.Add(NativeMenus.Submenu("View",
                NativeMenus.Item("Refresh Profiles", Refresh, Avalonia.Input.Key.R),
                NativeMenus.Item("Appearance...", ShowAppearance),
                NativeMenus.Item("Light Appearance", () => SetAppearance(false)),
                NativeMenus.Item("Dark Appearance", () => SetAppearance(true))));
            bar.Add(NativeMenus.Submenu("Window",
                NativeMenus.Item("Minimize", () => WindowState = WindowState.Minimized, Avalonia.Input.Key.M),
                NativeMenus.Item("Zoom", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
                NativeMenus.Item("Bring All to Front", () => { Activate(); foreach (Window window in OwnedWindows) window.Activate(); })));
            bar.Add(NativeMenus.Submenu("Help", NativeMenus.Item("About Kameo", ShowAbout)));
            NativeMenu.SetMenu(this, bar);
        }

        private void SetAppearance(bool dark)
        {
            Palette.Apply(Application.Current!, dark);
            _Store.SetPreference("theme", dark ? "dark" : "light");
            Refresh();
        }

        private void ShowAppearance() => _ = new AppearanceWindow(_Store, Refresh).ShowDialog(this);

        /// <summary>Refreshes the view when another process changes profile state.</summary>
        public void RefreshIfChanged()
        {
            string fingerprint = Fingerprint(_Store.List());
            if (fingerprint != _Fingerprint) Refresh();
        }

        /// <summary>Rebuilds the current view while preserving its search filter.</summary>
        public void Refresh()
        {
            string search = _Search?.Text ?? "";
            IReadOnlyList<Profile> all = _Store.List();
            _Fingerprint = Fingerprint(all);
            Background = Palette.Background;
            Foreground = Palette.Text;
            Grid shell = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            shell.Children.Add(BuildSidebar(all));
            Grid main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18, 20, 18, 12) };
            Grid.SetColumn(main, 1);

            Grid heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            heading.Children.Add(Ui.Text(HarnessCatalog.Name(_Harness), 23, weight: FontWeight.SemiBold));
            Button add = Ui.Button("+  Add profile", () => _ = EditAsync(null, CancellationToken.None), true);
            add.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(add, 1); heading.Children.Add(add);

            _Search = new TextBox { Name = "SearchProfiles", PlaceholderText = "Search profiles by name", Text = search };
            _Search.TextChanged += (_, _) => RenderProfiles();
            StackPanel header = Ui.Stack(heading, _Search);
            header.Spacing = 12; header.Margin = new Thickness(0, 0, 0, 12);
            main.Children.Add(header);
            _Profiles = new StackPanel { Spacing = 10 };
            ScrollViewer scroll = new ScrollViewer { Content = _Profiles, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 4) };
            Grid.SetRow(scroll, 1); main.Children.Add(scroll);

            _Status = Ui.Text("Selection applies to new sessions.", 11, Palette.Muted);
            _Status.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_Status, 2); main.Children.Add(_Status);
            shell.Children.Add(main);
            Content = shell;
            RenderProfiles();
        }

        /// <summary>Shows a status or actionable error in the manager.</summary>
        public void Report(string message)
        {
            if (_Status != null) { _Status.Text = message; _Status.Foreground = Palette.Danger; }
        }

        private Control BuildSidebar(IReadOnlyList<Profile> all)
        {
            Grid panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 18, 12, 12) };
            _Logo?.Dispose();
            using Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream(Palette.Dark ? "Kameo.Logo" : "Kameo.LogoDark")!;
            _Logo = new Bitmap(image);
            Image mark = new Image { Source = _Logo, Width = 26, Height = 26 };
            StackPanel branding = Ui.Row(mark, Ui.Text("Kameo", 21, weight: FontWeight.SemiBold));
            panel.Children.Add(branding);
            StackPanel navigation = new StackPanel { Spacing = 5, Margin = new Thickness(0, 22, 0, 0) };
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                int count = all.Count(profile => profile.Harness == harness);
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Width = 120 };
                row.Children.Add(Ui.Text(HarnessCatalog.Name(harness), 13, harness == _Harness ? Palette.Accent : Palette.Text, FontWeight.SemiBold));
                TextBlock number = Ui.Text(count.ToString(System.Globalization.CultureInfo.InvariantCulture), 12, Palette.Muted);
                Grid.SetColumn(number, 1); row.Children.Add(number);
                Button button = new Button
                {
                    Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 10),
                    Background = harness == _Harness ? Palette.AccentSoft : Brushes.Transparent,
                    BorderBrush = harness == _Harness ? Palette.Border : Brushes.Transparent
                };
                button.Click += (_, _) => { _Harness = harness; if (_Search != null) _Search.Text = ""; Refresh(); };
                navigation.Children.Add(button);
            }
            navigation.Children.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 12) });
            navigation.Children.Add(Ui.Text("LAUNCH FOLDER", 10, Palette.Muted));
            Button folder = Ui.Button(Path.GetFileName(_Workspace.TrimEnd(Path.DirectorySeparatorChar)) + "...", () => _ = ChooseDirectoryAsync(null, CancellationToken.None));
            folder.MaxWidth = 146;
            ToolTip.SetTip(folder, _Workspace + "\nChoose a launch folder");
            navigation.Children.Add(folder);
            Grid.SetRow(navigation, 1); panel.Children.Add(navigation);
            StackPanel bottom = Ui.Stack(
                Ui.Button("Appearance...", ShowAppearance),
                Ui.Text("v0.1.0 · alpha", 10, Palette.Muted));
            Grid.SetRow(bottom, 2); panel.Children.Add(bottom);
            return new Border { Background = Palette.Sidebar, BorderBrush = Palette.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = panel };
        }

        private void RenderProfiles()
        {
            if (_Profiles == null) return;
            _Profiles.Children.Clear();
            IReadOnlyList<Profile> all = _Store.List();
            List<Profile> profiles = all.Where(profile => profile.Harness == _Harness).ToList();
            Profile? active = profiles.FirstOrDefault(profile => profile.IsActive);
            StackPanel launch = Ui.Stack(
                Ui.Text(active == null ? "No profile selected" : active.Name, 12, Palette.Accent, FontWeight.SemiBold),
                Ui.Text("kameo " + HarnessCatalog.Command(_Harness), 17, weight: FontWeight.Medium));
            Grid command = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            command.Children.Add(launch);
            Button launchIn = Ui.Button("Launch in...", () =>
            {
                if (active != null) _ = ChooseDirectoryAsync(active.Id, CancellationToken.None);
            });
            launchIn.IsEnabled = active != null;
            ToolTip.SetTip(launchIn, "Choose a folder for this launch without changing your saved launch folder.");
            Button launchDefault = Ui.Button("Launch", () =>
            {
                if (active != null) RunAction(() => _OpenTerminal(active.Id, false, _Workspace));
            }, true);
            launchDefault.IsEnabled = active != null;
            StackPanel commandActions = Ui.Row(Ui.Button("Copy", () => _ = CopyAsync(CancellationToken.None)), launchIn, launchDefault);
            commandActions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(commandActions, 1); command.Children.Add(commandActions);
            _Profiles.Children.Add(Ui.Card(command, true));
            if (profiles.Count == 0)
            {
                _Profiles.Children.Add(Ui.Card(Ui.Stack(
                    Ui.Text("No profiles yet", 17, weight: FontWeight.SemiBold),
                    Ui.Text("Add an API key or a native login. You can import an existing login after creating the profile.", 13, Palette.Muted))));
                return;
            }
            string search = _Search?.Text?.Trim() ?? "";
            List<Profile> filtered = profiles.Where(profile => profile.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0) _Profiles.Children.Add(Ui.Text("No profiles match your search.", 15, Palette.Muted));
            foreach (Profile profile in filtered)
            {
                Grid heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                heading.Children.Add(Ui.Text(profile.Name, 16, weight: FontWeight.SemiBold));
                Border badge = Ui.Badge(profile.IsActive ? "SELECTED" : profile.Kind == CredentialKind.ApiKey ? "API KEY" : "NATIVE LOGIN", profile.IsActive);
                Grid.SetColumn(badge, 1); heading.Children.Add(badge);
                string description = profile.Kind == CredentialKind.ApiKey ? "API key" : profile.HasNativeCredential ? "Native login saved" : "Sign in or import a login";
                StackPanel actions = Ui.Row(
                    Ui.Button(profile.IsActive ? "Selected" : "Use profile", () => RunAction(() => { _Store.Select(profile.Id); Refresh(); _Changed(); }), !profile.IsActive),
                    Ui.Button("Edit", () => _ = EditAsync(profile.Id, CancellationToken.None)));
                actions.Children[0].IsEnabled = !profile.IsActive;
                if (profile.Kind == CredentialKind.Native)
                {
                    actions.Children.Add(Ui.Button(profile.Harness == HarnessKind.Mux ? "Setup" : "Sign in", () => RunAction(() => _OpenTerminal(profile.Id, true, _Workspace))));
                    actions.Children.Add(Ui.Button("Import...", () => _ = ImportAsync(profile.Id, CancellationToken.None)));
                    actions.Children.Add(Ui.Button("Import current", () => _ = ImportCurrentAsync(profile.Id, CancellationToken.None)));
                }
                Button delete = Ui.Button("Delete", () => _ = DeleteAsync(profile, CancellationToken.None));
                delete.Foreground = Palette.Danger;
                actions.Children.Add(delete);
                WrapPanel wrappedActions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
                Control[] buttons = actions.Children.ToArray();
                actions.Children.Clear();
                foreach (Control button in buttons) wrappedActions.Children.Add(button);
                _Profiles.Children.Add(Ui.Card(Ui.Stack(heading, Ui.Text(description, 12, Palette.Muted), wrappedActions)));
            }
        }

        private async Task EditAsync(string? id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ProfileEditor editor = new ProfileEditor(_Store, _Harness, id);
                if (await editor.ShowDialog<bool>(this)) { Refresh(); _Changed(); }
            }
            catch (System.Text.Json.JsonException) { Report("The profile configuration is not valid JSON. Re-import the native configuration."); }
            catch (Exception error) when (error is InvalidOperationException or IOException or InvalidDataException or ArgumentException or KeyNotFoundException) { Report(error.Message); }
        }

        private async Task ImportAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Import native credential JSON", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("JSON credentials") { Patterns = new[] { "*.json" } } } });
            if (files.Count == 0) return;
            RunAction(() =>
            {
                string path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local credential file.");
                new CredentialFiles(_Store).Import(id, path); Refresh(); _Changed();
            });
        }

        private async Task ImportCurrentAsync(string id, CancellationToken cancellationToken)
        {
            try { await new CurrentLoginImporter(_Store).ImportAsync(id, cancellationToken); Refresh(); _Changed(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException) { Report(error.Message); }
        }

        private async Task DeleteAsync(Profile profile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Window dialog = new Window { Title = "Delete profile", Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Palette.Background };
            StackPanel content = Ui.Stack(Ui.Text("Delete " + profile.Name + "?", 22, weight: FontWeight.SemiBold),
                Ui.Text("This removes the profile and its saved credentials and harness data from this device. Your original harness login is kept.", 14, Palette.Muted),
                Ui.Row(Ui.Button("Cancel", () => dialog.Close(false)), Ui.Button("Delete profile", () => dialog.Close(true), true)));
            content.Margin = new Thickness(28); dialog.Content = content;
            if (await dialog.ShowDialog<bool>(this)) RunAction(() => { _Store.Delete(profile.Id); Refresh(); _Changed(); });
        }

        private async Task ChooseDirectoryAsync(string? launchProfileId, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? directory = await _PickFolder(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (directory == null) return;
                RunAction(() =>
                {
                    if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected launch folder is no longer available.");
                    if (launchProfileId != null) _OpenTerminal(launchProfileId, false, directory);
                    else { _Store.SetPreference("workspace", directory); _Workspace = directory; Refresh(); }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { Report(error.Message); }
        }

        private async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IStorageFolder? start = await StorageProvider.TryGetFolderFromPathAsync(_Workspace);
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose a launch directory", AllowMultiple = false, SuggestedStartLocation = start });
            try { return folders.Count == 0 ? null : folders[0].TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local launch folder."); }
            finally { foreach (IStorageFolder folder in folders) folder.Dispose(); }
        }

        private async Task CopyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Clipboard != null) await Clipboard.SetTextAsync("kameo " + HarnessCatalog.Command(_Harness));
            if (_Status != null) { _Status.Text = "Launch command copied."; _Status.Foreground = Palette.Accent; }
        }

        private void RunAction(Action action)
        {
            try { action(); }
            catch (Microsoft.Data.Sqlite.SqliteException) { Report("The profile database is busy or unavailable. Try again."); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or System.ComponentModel.Win32Exception) { Report(error.Message); }
        }

        private static string Fingerprint(IReadOnlyList<Profile> profiles) => string.Join("|", profiles.Select(profile => profile.Id + profile.Name + profile.IsActive + profile.HasNativeCredential));
    }
}
