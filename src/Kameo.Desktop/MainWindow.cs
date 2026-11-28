namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Data;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Avalonia.Platform.Storage;
    using Avalonia.Threading;
    using Kameo.Core;

    /// <summary>The profile manager, organized by harness with explicit selection and launch actions.</summary>
    public sealed class MainWindow : Window
    {
        private readonly ProfileStore _Store;
        private readonly Action _Changed;
        private readonly Action _Quit;
        private readonly Func<CancellationToken, Task<string?>> _PickFolder;
        private readonly Func<CancellationToken, Task<string?>>? _PickFolderOverride;
        private readonly Action<LaunchRequest> _OpenTerminal;
        private readonly Func<string, CancellationToken, Task<UsageReport>> _ReadUsage;
        private readonly TimeProvider _Clock;
        private readonly Dictionary<string, UsageReport> _Usage = new Dictionary<string, UsageReport>();
        private readonly Dictionary<string, DateTimeOffset> _UsageCheckedAt = new Dictionary<string, DateTimeOffset>();
        private readonly Dictionary<string, Task> _PendingUsage = new Dictionary<string, Task>();

        /// <summary>How long a usage result is reused before navigation checks that login again.</summary>
        internal static readonly TimeSpan UsageCacheDuration = TimeSpan.FromMinutes(15);

        /// <summary>How often usage for every harness's native logins is refreshed in the background.</summary>
        internal static readonly TimeSpan UsageRefreshInterval = UsageCacheDuration;

        // A background refresh rechecks logins at least this old, so a check that finished a moment after the
        // previous tick is not skipped until the tick after.
        private static readonly TimeSpan _BackgroundRefreshAge = UsageRefreshInterval - TimeSpan.FromMinutes(1);
        private readonly DispatcherTimer _UsageTimer;
        private HarnessKind _Harness = HarnessKind.Claude;
        private TextBox? _Search;
        private StackPanel? _Profiles;
        private TextBlock? _Status;
        private string _Fingerprint = "";
        private string _Workspace;
        private Bitmap? _Logo;
        private LaunchOptionsWindow? _LaunchDialog;
        private readonly Dictionary<HarnessKind, NativeMenu> _HarnessMenus = new Dictionary<HarnessKind, NativeMenu>();

        /// <summary>Creates the manager for a local profile store.</summary>
        public MainWindow(ProfileStore store, Action changed, Action quit)
            : this(store, changed, quit, null, null) { }

        internal MainWindow(ProfileStore store, Action changed, Action quit,
            Func<CancellationToken, Task<string?>>? pickFolder, Action<LaunchRequest>? openTerminal,
            Func<string, CancellationToken, Task<UsageReport>>? readUsage = null, TimeProvider? clock = null)
        {
            _Store = store;
            _Changed = changed;
            _Quit = quit;
            _PickFolder = pickFolder ?? PickFolderAsync;
            _PickFolderOverride = pickFolder;
            _OpenTerminal = openTerminal ?? (request => TerminalLauncher.Open(_Store, request));
            _ReadUsage = readUsage ?? ((id, cancellationToken) => ReadUsageAsync(_Store, id, cancellationToken));
            _Clock = clock ?? TimeProvider.System;
            _Workspace = store.Preference("workspace") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(_Workspace)) _Workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Title = "Kameo";
            Width = 800; Height = 550; MinWidth = 700; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kameo.Logo")!)
                Icon = new WindowIcon(icon);
            Closed += (_, _) => _Logo?.Dispose();
            Opened += (_, _) => _ = CheckUsageAsync(false, CancellationToken.None);
            // Runs while the window is hidden in the tray too, so usage is current and expired sign-ins surface early.
            _UsageTimer = new DispatcherTimer { Interval = UsageRefreshInterval };
            _UsageTimer.Tick += (_, _) => _ = RefreshUsageInBackgroundAsync(CancellationToken.None);
            _UsageTimer.Start();
            Closed += (_, _) => _UsageTimer.Stop();
            InstallNativeMenus();
            Refresh();
        }

        /// <summary>Displays the local product and version information.</summary>
        public void ShowAbout()
        {
            _ = new AboutWindow().ShowDialog(this);
        }

        /// <summary>Opens the launch options for a harness's selected profile, then launches it with the chosen options.</summary>
        /// <param name="harness">The harness whose selected profile is launched.</param>
        /// <param name="cancellationToken">Closes the dialog without launching.</param>
        /// <returns>False when a failure was reported in this window; true when it launched or was canceled.</returns>
        internal async Task<bool> LaunchSelectedAsync(HarnessKind harness, CancellationToken cancellationToken)
        {
            Profile selected;
            try { selected = _Store.Selected(harness); }
            catch (Exception error) when (error is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { Report(error is InvalidOperationException ? error.Message : "The profile database is busy or unavailable. Try again."); return false; }
            return await LaunchAsync(selected.Id, false, cancellationToken) != LaunchResult.Failed;
        }

        /// <summary>The launch options dialog while it is open, whether over the manager or on its own from the tray.</summary>
        internal LaunchOptionsWindow? ActiveLaunchOptions => _LaunchDialog;

        /// <summary>Opens the launch options for a profile, then launches it with the chosen folder and permission level.</summary>
        /// <param name="id">The profile to launch.</param>
        /// <param name="standalone">
        /// True to show the dialog on its own, as the tray does, so only the dialog comes forward. Otherwise it is modal over
        /// the manager when the manager is visible; on macOS an owned window brings its owner to the front with it.
        /// </param>
        /// <param name="cancellationToken">Closes the dialog without launching.</param>
        /// <returns>False when a failure was reported in this window; true when it launched or was canceled.</returns>
        internal async Task<bool> LaunchProfileAsync(string id, bool standalone, CancellationToken cancellationToken) => await LaunchAsync(id, standalone, cancellationToken) != LaunchResult.Failed;

        private async Task<LaunchResult> LaunchAsync(string id, bool standalone, CancellationToken cancellationToken)
        {
            Profile profile;
            string? permission;
            try
            {
                profile = _Store.Get(id);
                permission = _Store.Preference(LaunchPermissions.PreferenceName(profile.Harness));
            }
            catch (Microsoft.Data.Sqlite.SqliteException) { Report("The profile database is busy or unavailable. Try again."); return LaunchResult.Failed; }
            catch (Exception error) when (error is KeyNotFoundException or ArgumentException) { Report(error.Message); return LaunchResult.Failed; }
            if (_LaunchDialog != null)
            {
                // One launch at a time: bring the open dialog forward instead of stacking another.
                _LaunchDialog.Activate();
                return LaunchResult.Canceled;
            }
            LaunchOptions? options = await ShowLaunchOptionsAsync(new LaunchOptionsWindow(profile, _Workspace, permission, _PickFolderOverride), standalone, cancellationToken);
            if (options == null) return LaunchResult.Canceled;
            LaunchPermission level = LaunchPermissions.Find(profile.Harness, options.PermissionId);
            bool launched = RunAction(() =>
            {
                if (!Directory.Exists(options.WorkingDirectory)) throw new DirectoryNotFoundException("The selected launch folder is no longer available.");
                _OpenTerminal(new LaunchRequest(profile.Id, false, options.WorkingDirectory, level.Arguments));
                // Remember choices only once the terminal opened.
                _Store.SetPreference(LaunchPermissions.PreferenceName(profile.Harness), level.Id);
                if (options.SaveFolder) { _Store.SetPreference("workspace", options.WorkingDirectory); _Workspace = options.WorkingDirectory; Refresh(); }
            });
            if (!launched) return LaunchResult.Failed;
            if (_Status != null)
            {
                _Status.Text = "Launched " + profile.Name + (level.Id == LaunchPermissions.DefaultId ? "." : " (" + level.Label + ").");
                _Status.Foreground = Palette.Accent;
            }
            return LaunchResult.Launched;
        }

        private static async Task<UsageReport> ReadUsageAsync(ProfileStore store, string id, CancellationToken cancellationToken)
        {
            using UsageService service = new UsageService(store);
            return await service.ReadAsync(id, false, cancellationToken).ConfigureAwait(false);
        }

        // Shows the dialog modal over the visible manager, or on its own when requested or when the manager is hidden.
        private async Task<LaunchOptions?> ShowLaunchOptionsAsync(LaunchOptionsWindow dialog, bool standalone, CancellationToken cancellationToken)
        {
            TaskCompletionSource<LaunchOptions?> closed = new TaskCompletionSource<LaunchOptions?>();
            _LaunchDialog = dialog;
            dialog.Closed += (_, _) => { _LaunchDialog = null; closed.TrySetResult(dialog.Result); };
            if (IsVisible && !standalone) _ = dialog.ShowDialog(this);
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
                dialog.Activate();
            }
            using (cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close)))
                return await closed.Task;
        }

        /// <summary>Whether a harness offers Launch lowest usage: Claude Code or Codex with at least two native logins.</summary>
        internal static bool OffersLowestUsage(HarnessKind harness, IEnumerable<Profile> profiles) =>
            HarnessCatalog.SupportsUsage(harness) && profiles.Count(profile => profile.Harness == harness && UsageService.Supports(profile)) > 1;

        private void InstallNativeMenus()
        {
            NativeMenu bar = new NativeMenu();
            bar.Add(NativeMenus.Submenu("File",
                NativeMenus.Item("Add Profile...", () => _ = EditAsync(null, CancellationToken.None), Avalonia.Input.Key.N),
                NativeMenus.Item("Choose Launch Directory...", () => _ = ChooseDirectoryAsync(CancellationToken.None)),
                new NativeMenuItemSeparator(), NativeMenus.Item("Close Window", Close, Avalonia.Input.Key.W)));
            bar.Add(NativeMenus.Edit(this));
            NativeMenu harnesses = new NativeMenu();
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                NativeMenu choices = new NativeMenu();
                _HarnessMenus.Add(harness, choices);
                harnesses.Add(new NativeMenuItem(HarnessCatalog.Name(harness)) { Menu = choices });
            }
            bar.Add(new NativeMenuItem("Profiles") { Menu = harnesses });
            bar.Add(NativeMenus.Submenu("View",
                NativeMenus.Item("Refresh Profiles", Refresh, Avalonia.Input.Key.R),
                NativeMenus.Item("Refresh Usage", () => _ = CheckUsageAsync(true, CancellationToken.None), Avalonia.Input.Key.U),
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

        private void RefreshProfileMenus(IReadOnlyList<Profile> profiles)
        {
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                NativeMenu choices = _HarnessMenus[harness];
                choices.Items.Clear();
                Profile? active = profiles.FirstOrDefault(profile => profile.Harness == harness && profile.IsActive);
                NativeMenuItem launch = NativeMenus.Item(active == null ? "No profile selected" : "Launch " + active.Name + "...",
                    () => _ = LaunchSelectedAsync(harness, CancellationToken.None));
                launch.IsEnabled = active != null;
                choices.Add(launch); choices.Add(new NativeMenuItemSeparator());
                choices.Add(NativeMenus.Item("Manage profiles...", () => ShowHarness(harness)));
            }
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
            IReadOnlyList<Profile> profiles = _Store.List();
            string fingerprint = Fingerprint(profiles);
            if (fingerprint == _Fingerprint) return;
            // A sign-in finished elsewhere: forget cached "signed out" results so the new login is checked.
            foreach (Profile profile in profiles)
                if (profile.HasNativeCredential && _Usage.TryGetValue(profile.Id, out UsageReport? cached) && cached.Status == UsageStatus.SignedOut) ForgetUsage(profile.Id);
            Refresh();
            _ = CheckUsageAsync(false, CancellationToken.None);
        }

        private void ShowHarness(HarnessKind harness)
        {
            _Harness = harness;
            if (_Search != null) _Search.Text = "";
            Refresh();
            _ = CheckUsageAsync(false, CancellationToken.None);
        }

        private void ForgetUsage(string id)
        {
            _Usage.Remove(id);
            _UsageCheckedAt.Remove(id);
        }

        /// <summary>Rebuilds the current view while preserving its search filter.</summary>
        public void Refresh()
        {
            string search = _Search?.Text ?? "";
            IReadOnlyList<Profile> all = _Store.List();
            _Fingerprint = Fingerprint(all);
            RefreshProfileMenus(all);
            Background = Palette.Background;
            Foreground = Palette.Text;
            Grid shell = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            shell.Children.Add(BuildSidebar(all));
            Grid main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18, 20, 18, 12) };
            Grid.SetColumn(main, 1);

            Grid heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            TextBlock title = Ui.Text(HarnessCatalog.Name(_Harness), 23, weight: FontWeight.SemiBold);
            title.Name = "HarnessTitle";
            heading.Children.Add(title);
            Button add = Ui.Button("+  Add profile", () => _ = EditAsync(null, CancellationToken.None), true);
            StackPanel headingActions = Ui.Row(add);
            headingActions.VerticalAlignment = VerticalAlignment.Center;
            List<Profile> natives = all.Where(profile => profile.Harness == _Harness && UsageService.Supports(profile)).ToList();
            if (HarnessCatalog.SupportsUsage(_Harness) && natives.Count > 0)
            {
                bool checking = natives.Any(profile => _PendingUsage.ContainsKey(profile.Id));
                Button usage = Ui.Button("Refresh usage", () => _ = CheckUsageAsync(true, CancellationToken.None));
                usage.IsEnabled = !checking;
                ToolTip.SetTip(usage, "Check plan limits now. Results are otherwise reused for 15 minutes. Idle Claude Code sign-ins are renewed when they are about to expire.");
                headingActions.Children.Insert(0, usage);
                if (OffersLowestUsage(_Harness, all))
                {
                    Button lowest = Ui.Button("Launch lowest usage", () => _ = LaunchLowestAsync(_Harness, false, CancellationToken.None));
                    UsageReport? least = UsageService.LeastUsed(natives.Where(profile => _Usage.ContainsKey(profile.Id)).Select(profile => _Usage[profile.Id]));
                    Profile? leastProfile = least == null ? null : natives.FirstOrDefault(profile => profile.Id == least.ProfileId);
                    ToolTip.SetTip(lowest, leastProfile == null
                        ? "Choose launch options for the login with the most remaining plan usage."
                        : "Choose launch options for " + leastProfile.Name + " (" + UsageFormat.Percent(least!.PeakPercent) + " used at its tightest limit).");
                    headingActions.Children.Insert(1, lowest);
                }
            }
            Grid.SetColumn(headingActions, 1); heading.Children.Add(headingActions);

            _Search = new TextBox { Name = "SearchProfiles", PlaceholderText = "Search profiles by name", Text = search, HorizontalAlignment = HorizontalAlignment.Left };
            _Search.TextChanged += (_, _) => RenderProfiles();
            StackPanel header = Ui.Stack(heading, _Search);
            header.Spacing = 12; header.Margin = new Thickness(0, 0, 0, 12);
            main.Children.Add(header);
            _Profiles = new StackPanel { Name = "ProfileCards", Spacing = 10 };
            _Search.Bind(WidthProperty, new Binding("Bounds.Width") { Source = _Profiles, Mode = BindingMode.OneWay });
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
                button.Click += (_, _) => ShowHarness(harness);
                navigation.Children.Add(button);
            }
            navigation.Children.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 12) });
            navigation.Children.Add(Ui.Text("LAUNCH FOLDER", 10, Palette.Muted));
            Button folder = Ui.Button(Path.GetFileName(_Workspace.TrimEnd(Path.DirectorySeparatorChar)) + "...", () => _ = ChooseDirectoryAsync(CancellationToken.None));
            folder.MaxWidth = 146;
            ToolTip.SetTip(folder, _Workspace + "\nChoose a launch folder");
            navigation.Children.Add(folder);
            Grid.SetRow(navigation, 1); panel.Children.Add(navigation);
            StackPanel bottom = Ui.Stack(
                Ui.Button("Appearance...", ShowAppearance),
                Ui.Text("v" + ProductInfo.Version + " · " + ProductInfo.Stage, 10, Palette.Muted));
            Grid.SetRow(bottom, 2); panel.Children.Add(bottom);
            return new Border { Background = Palette.Sidebar, BorderBrush = Palette.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = panel };
        }

        private void RenderProfiles()
        {
            if (_Profiles == null) return;
            _Profiles.Children.Clear();
            IReadOnlyList<Profile> all = _Store.List();
            List<Profile> profiles = all.Where(profile => profile.Harness == _Harness).ToList();
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
                string description = profile.Harness == HarnessKind.Antigravity && profile.Kind == CredentialKind.Native
                    ? profile.HasNativeCredential ? "Google account saved · isolated login" : "Sign in with a Google account · CLI " + AntigravityCompatibility.SupportedVersion + "+"
                    : profile.MuxEndpointsFile.Length > 0 ? "Shared endpoints: " + Path.GetFileName(profile.MuxEndpointsFile)
                    : profile.Kind == CredentialKind.ApiKey ? "API key" : profile.HasNativeCredential ? "Native login saved" : "Sign in or import a login";
                Button launch = Ui.Button("Launch", () => _ = LaunchProfileAsync(profile.Id, false, CancellationToken.None), true);
                ToolTip.SetTip(launch, "Choose a folder and permission level, then open a terminal with this profile.");
                Button use = Ui.Button(profile.IsActive ? "Selected" : "Use profile", () => RunAction(() => { _Store.Select(profile.Id); Refresh(); _Changed(); }));
                use.IsEnabled = !profile.IsActive;
                ToolTip.SetTip(use, "Make this the profile that kameo " + HarnessCatalog.Command(profile.Harness) + " and the tray launch.");
                WrapPanel primary = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
                primary.Children.Add(launch); primary.Children.Add(use);
                if (profile.Kind == CredentialKind.Native && profile.MuxEndpointsFile.Length == 0 && HarnessCatalog.SupportsNativeLogin(profile.Harness))
                {
                    primary.Children.Add(Ui.Button(profile.Harness == HarnessKind.Mux ? "Setup" : "Sign in", () => RunAction(() => _OpenTerminal(new LaunchRequest(profile.Id, true, _Workspace)))));
                    primary.Children.Add(ImportButton(profile));
                }
                StackPanel manage = Ui.Row(
                    Ui.IconButton("Edit", "Edit profile", Ui.GearIcon, () => _ = EditAsync(profile.Id, CancellationToken.None)),
                    Ui.IconButton("Delete", "Delete profile", Ui.TrashIcon, () => _ = DeleteAsync(profile, CancellationToken.None), Palette.Danger));
                manage.VerticalAlignment = VerticalAlignment.Top;
                manage.Margin = new Thickness(8, 0, 0, 0);
                Grid actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                actions.Children.Add(primary);
                Grid.SetColumn(manage, 1); actions.Children.Add(manage);
                StackPanel card = Ui.Stack(heading, Ui.Text(description, 12, Palette.Muted));
                if (UsageService.Supports(profile))
                {
                    if (_Usage.TryGetValue(profile.Id, out UsageReport? report)) card.Children.Add(BuildUsage(report));
                    else if (_PendingUsage.ContainsKey(profile.Id)) card.Children.Add(Ui.Text("Checking usage...", 12, Palette.Muted));
                }
                card.Children.Add(actions);
                _Profiles.Children.Add(Ui.Card(card));
            }
        }

        private static Control BuildUsage(UsageReport report)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            StackPanel panel = new StackPanel { Name = "Usage", Spacing = 18 };
            if (report.Status == UsageStatus.Available)
            {
                foreach (UsageWindow window in report.Windows)
                {
                    Grid caption = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    caption.Children.Add(Ui.Text(window.Label, 13, weight: FontWeight.SemiBold));
                    string reset = UsageFormat.Reset(window.ResetsAt, now);
                    TextBlock amount = Ui.Text(UsageFormat.Percent(window.UsedPercent) + " used" + (reset.Length > 0 ? " · " + reset : ""), 12, Palette.Muted);
                    amount.TextWrapping = TextWrapping.NoWrap;
                    amount.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(amount, 1); caption.Children.Add(amount);
                    ProgressBar bar = new ProgressBar
                    {
                        Minimum = 0, Maximum = 100, Value = Math.Clamp(window.UsedPercent, 0, 100), Height = 8, MinHeight = 8,
                        CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Stretch,
                        Foreground = window.UsedPercent >= 90 ? Palette.Danger : Palette.Accent, Background = Palette.Border
                    };
                    StackPanel row = new StackPanel { Spacing = 8 };
                    row.Children.Add(caption);
                    row.Children.Add(bar);
                    panel.Children.Add(row);
                }
            }
            string summary = string.Join(" · ", new[] { report.Plan, report.Detail, UsageFormat.Observed(report, now) }.Where(part => part.Length > 0));
            if (summary.Length > 0) panel.Children.Add(Ui.Text(summary, 12, Palette.Muted));
            if (report.Message.Length > 0)
                panel.Children.Add(Ui.Text(report.Message, 12, report.Status is UsageStatus.Unavailable or UsageStatus.Expired ? Palette.Danger : Palette.Muted));
            return new Border
            {
                Background = Palette.Background, BorderBrush = Palette.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 14), Margin = new Thickness(0, 8, 0, 8), Child = panel
            };
        }

        private Control ImportButton(Profile profile)
        {
            MenuItem current = new MenuItem { Header = "Current " + HarnessCatalog.Name(profile.Harness) + " login" };
            current.Click += (_, _) => _ = ImportCurrentAsync(profile.Id, CancellationToken.None);
            MenuItem file = new MenuItem { Header = "Credential file..." };
            file.Click += (_, _) => _ = ImportAsync(profile.Id, CancellationToken.None);
            MenuFlyout menu = new MenuFlyout();
            menu.Items.Add(current);
            menu.Items.Add(file);
            // A plain Button keeps the shared button height; DropDownButton's template is taller.
            TextBlock label = new TextBlock { Text = "Import", VerticalAlignment = VerticalAlignment.Center };
            PathIcon chevron = new PathIcon { Data = StreamGeometry.Parse(Ui.ChevronIcon), Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, chevron } };
            Button import = new Button { Content = content, Flyout = menu, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            Avalonia.Automation.AutomationProperties.SetName(import, "Import");
            ToolTip.SetTip(import, "Copy an existing login into this profile, from this computer's current login or a native credential file.");
            return import;
        }

        /// <summary>
        /// Checks plan usage for the current harness's native logins and shows the results on their cards. Unless
        /// <paramref name="force"/> is set, a login checked within <see cref="UsageCacheDuration"/> is not checked again.
        /// </summary>
        internal Task CheckUsageAsync(bool force, CancellationToken cancellationToken) => CheckUsageAsync(_Harness, force, cancellationToken);

        /// <summary>Checks plan usage for a harness's native logins, whether or not that harness is on screen.</summary>
        internal Task CheckUsageAsync(HarnessKind harness, bool force, CancellationToken cancellationToken) =>
            CheckUsageAsync(harness, force ? TimeSpan.Zero : UsageCacheDuration, force, cancellationToken);

        /// <summary>Whether the periodic background usage refresh is running.</summary>
        internal bool RefreshesUsageInBackground => _UsageTimer.IsEnabled && _UsageTimer.Interval == UsageRefreshInterval;

        /// <summary>
        /// Refreshes usage for every harness that reports it, on screen or not, without reporting configuration
        /// problems. Runs every <see cref="UsageRefreshInterval"/>.
        /// </summary>
        internal async Task RefreshUsageInBackgroundAsync(CancellationToken cancellationToken)
        {
            foreach (HarnessKind harness in HarnessCatalog.All.Where(HarnessCatalog.SupportsUsage))
                await CheckUsageAsync(harness, _BackgroundRefreshAge, false, cancellationToken);
        }

        private async Task CheckUsageAsync(HarnessKind harness, TimeSpan maxAge, bool force, CancellationToken cancellationToken)
        {
            if (!HarnessCatalog.SupportsUsage(harness))
            {
                if (force) Report("Plan usage is available for Claude Code and Codex native logins.");
                return;
            }
            List<Profile> natives;
            try { natives = _Store.List().Where(profile => profile.Harness == harness && UsageService.Supports(profile)).ToList(); }
            catch (Microsoft.Data.Sqlite.SqliteException) { if (force) Report("The profile database is busy or unavailable. Try again."); return; }
            DateTimeOffset now = _Clock.GetUtcNow();
            List<Profile> due = natives.Where(profile => !_PendingUsage.ContainsKey(profile.Id)
                && (!_UsageCheckedAt.TryGetValue(profile.Id, out DateTimeOffset checkedAt) || now - checkedAt >= maxAge)).ToList();
            if (due.Count == 0)
            {
                if (force && natives.Count == 0) Report("Add a native login to see plan usage. API keys have no plan limits.");
                return;
            }
            using SemaphoreSlim gate = new SemaphoreSlim(4);
            List<Task> checks = new List<Task>();
            foreach (Profile profile in due)
            {
                _PendingUsage[profile.Id] = Task.CompletedTask;
                Task check = ReadUsageAsync(profile, gate, cancellationToken);
                if (!check.IsCompleted) _PendingUsage[profile.Id] = check;
                checks.Add(check);
            }
            if (_Harness == harness && due.Any(profile => _PendingUsage.ContainsKey(profile.Id)))
            {
                Refresh();
                if (_Status != null) { _Status.Text = "Checking " + HarnessCatalog.Name(harness) + " usage..."; _Status.Foreground = Palette.Muted; }
            }
            try { await Task.WhenAll(checks); }
            finally
            {
                // Rebuilding for a harness that is not on screen would reset the visible harness's status line.
                if (_Harness == harness) Refresh();
                if (_Status != null && _Harness == harness) { _Status.Text = UsageCheckedMessage(harness, _Clock.GetLocalNow()); _Status.Foreground = Palette.Accent; }
            }
        }

        /// <summary>The status shown after a harness's usage is checked, with the local time of the check.</summary>
        internal static string UsageCheckedMessage(HarnessKind harness, DateTimeOffset localTime) =>
            HarnessCatalog.Name(harness) + " usage checked at " + localTime.ToString("t", CultureInfo.CurrentCulture) + ".";

        private async Task ReadUsageAsync(Profile profile, SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            try
            {
                await gate.WaitAsync(cancellationToken);
                UsageReport report;
                try { report = await _ReadUsage(profile.Id, cancellationToken); }
                catch (Exception error) when (error is IOException or InvalidOperationException or KeyNotFoundException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
                {
                    report = new UsageReport { ProfileId = profile.Id, ProfileName = profile.Name, Harness = profile.Harness, Status = UsageStatus.Unavailable, Message = "Unable to read usage for this profile." };
                }
                finally { gate.Release(); }
                _Usage[profile.Id] = report;
                _UsageCheckedAt[profile.Id] = _Clock.GetUtcNow();
            }
            catch (OperationCanceledException) { }
            finally
            {
                _PendingUsage.Remove(profile.Id);
                RenderProfiles();
            }
        }

        /// <summary>
        /// Opens the launch options for the harness's native login with the lowest usage at its tightest limit, checking
        /// stale usage first.
        /// </summary>
        /// <returns>False when a failure was reported in this window; true when it launched or was canceled.</returns>
        /// <param name="harness">The harness whose native logins are compared.</param>
        /// <param name="standalone">True to show the dialog on its own without bringing the manager forward, as the tray does.</param>
        /// <param name="cancellationToken">Cancels the usage check and closes the dialog.</param>
        internal async Task<bool> LaunchLowestAsync(HarnessKind harness, bool standalone, CancellationToken cancellationToken)
        {
            await CheckUsageAsync(harness, false, cancellationToken);
            await Task.WhenAll(_PendingUsage.Values.ToArray());
            List<Profile> natives;
            try { natives = _Store.List().Where(profile => profile.Harness == harness && UsageService.Supports(profile)).ToList(); }
            catch (Microsoft.Data.Sqlite.SqliteException) { Report("The profile database is busy or unavailable. Try again."); return false; }
            UsageReport? least = UsageService.LeastUsed(natives.Where(profile => _Usage.ContainsKey(profile.Id)).Select(profile => _Usage[profile.Id]));
            Profile? target = least == null ? null : natives.FirstOrDefault(profile => profile.Id == least.ProfileId);
            if (least == null || target == null)
            {
                Report("No plan usage is available for these " + HarnessCatalog.Name(harness) + " logins yet. Make sure they are signed in, then refresh usage.");
                return false;
            }
            LaunchResult result = await LaunchAsync(target.Id, standalone, cancellationToken);
            if (result == LaunchResult.Launched && _Status != null)
            {
                _Status.Text = "Launched " + target.Name + ": " + UsageFormat.Percent(least.PeakPercent) + " used at its tightest limit.";
                _Status.Foreground = Palette.Accent;
            }
            return result != LaunchResult.Failed;
        }

        private async Task EditAsync(string? id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ProfileEditor editor = new ProfileEditor(_Store, _Harness, id);
                if (await editor.ShowDialog<bool>(this)) { Refresh(); _Changed(); _ = CheckUsageAsync(false, CancellationToken.None); }
            }
            catch (System.Text.Json.JsonException) { Report("The profile configuration is not valid JSON. Re-import the native configuration."); }
            catch (Exception error) when (error is InvalidOperationException or IOException or InvalidDataException or ArgumentException or KeyNotFoundException) { Report(error.Message); }
        }

        private async Task ImportAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Import native credential file", AllowMultiple = false, FileTypeFilter = new[]
                { new FilePickerFileType("Credential files") { Patterns = new[] { "*.json", "*-oauth-token" } }, FilePickerFileTypes.All } });
            try
            {
                if (files.Count == 0) return;
                RunAction(() =>
                {
                    string path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local credential file.");
                    new CredentialFiles(_Store).Import(id, path); ForgetUsage(id); Refresh(); _Changed(); _ = CheckUsageAsync(false, CancellationToken.None);
                });
            }
            finally { foreach (IStorageFile file in files) file.Dispose(); }
        }

        private async Task ImportCurrentAsync(string id, CancellationToken cancellationToken)
        {
            try { await new CurrentLoginImporter(_Store).ImportAsync(id, cancellationToken); ForgetUsage(id); Refresh(); _Changed(); _ = CheckUsageAsync(false, CancellationToken.None); }
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

        private async Task ChooseDirectoryAsync(CancellationToken cancellationToken)
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
                    _Store.SetPreference("workspace", directory); _Workspace = directory; Refresh();
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

        private bool RunAction(Action action)
        {
            try { action(); return true; }
            catch (Microsoft.Data.Sqlite.SqliteException) { Report("The profile database is busy or unavailable. Try again."); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or System.ComponentModel.Win32Exception) { Report(error.Message); }
            return false;
        }

        private static string Fingerprint(IReadOnlyList<Profile> profiles) => string.Join("|", profiles.Select(profile => profile.Id + profile.Name + profile.IsActive + profile.HasNativeCredential + profile.MuxEndpointsFile));
    }
}
