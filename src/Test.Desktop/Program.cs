namespace Test.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Headless;
    using Avalonia.Interactivity;
    using Avalonia.Threading;
    using Avalonia.VisualTree;
    using Kameo.Core;
    using Kameo.Desktop;
    using Touchstone.Cli;
    using Touchstone.Core;

    /// <summary>Touchstone UI behavior tests running in a real Avalonia control tree on a headless dispatcher.</summary>
    public static class Program
    {
        /// <summary>Creates the application for headless tests without accessing a user's real profile store.</summary>
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--native-smoke") return await NativeSmoke.RunAsync(args[1], CancellationToken.None).ConfigureAwait(false);
            using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(Program));
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<MainWindow, ProfileStore> action) => AddAsync(name, (window, store, _) => { action(window, store); return Task.CompletedTask; });
            void AddAsync(string name, Func<MainWindow, ProfileStore, CancellationToken, Task> action) => cases.Add(new TestCaseDescriptor("Desktop", name, name, cancellationToken => session.Dispatch(async () =>
            {
                string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kameo-ui-test-" + Guid.NewGuid().ToString("N"));
                MainWindow? window = null;
                try
                {
                    ProfileStore store = new ProfileStore(root);
                    window = new MainWindow(store, () => { }, () => { });
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    await action(window, store, cancellationToken);
                    await Task.Delay(30, cancellationToken);
                }
                finally
                {
                    if (window != null) { foreach (Window child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
                    await Task.Delay(30, CancellationToken.None);
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                return true;
            }, cancellationToken), tags: new[] { "desktop", "isolated" }));

            Add("EmptyStateHasNoLaunch", (window, store) => Check(!window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string is "Launch" or "Launch in...") && Text(window).Contains("No profiles yet", StringComparison.Ordinal) && !Text(window).Contains("kameo claude", StringComparison.Ordinal), "Empty state is missing, can launch without a profile, or shows a command card."));
            Add("EachProfileLaunchesItself", (_, store) =>
            {
                Profile work = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Work", ApiKey = "synthetic" });
                Profile personal = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Personal", ApiKey = "synthetic" });
                string chosen = Directory.CreateDirectory(Path.Combine(store.Root, "Chosen")).FullName;
                store.SetPreference("workspace", store.Root);
                List<string> launches = new List<string>();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(chosen), request =>
                {
                    Check(!request.Login && request.Arguments.Count == 0, "Launch started a login or added arguments.");
                    launches.Add(request.ProfileId + "@" + request.WorkingDirectory);
                });
                try
                {
                    window.Show(); Sync(window);
                    Button[] LaunchButtons() => window.GetVisualDescendants().OfType<Button>().Where(button => button.Content as string == "Launch").ToArray();
                    Check(LaunchButtons().Length == 2 && !window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Launch in..."), "Each profile card needs one Launch button, which replaces Launch in....");
                    Click(LaunchButtons()[0]); Check(Dialog(window).Title == "Launch Personal" && Field(Dialog(window), "LaunchFolder").Text == store.Root, "The dialog should name the profile and propose the saved folder.");
                    Confirm(window);
                    Click(LaunchButtons()[1]); Confirm(window);
                    Click(LaunchButtons()[0]); Field(Dialog(window), "LaunchFolder").Text = chosen; Confirm(window);
                    Check(launches.SequenceEqual(new[] { personal.Id + "@" + store.Root, work.Id + "@" + store.Root, personal.Id + "@" + chosen }), "Card launches used the wrong profile or folder: " + string.Join(", ", launches));
                    Check(store.Selected(HarnessKind.Claude).Id == work.Id && store.Preference("workspace") == store.Root, "Launching a card changed the selection or saved folder.");
                }
                finally { window.Close(); }
            });
            Add("LaunchDialogChoosesFolderAndPermission", (_, store) =>
            {
                string chosen = Directory.CreateDirectory(Path.Combine(store.Root, "Project with spaces")).FullName;
                WithLaunchWindow(store, _ => Task.FromResult<string?>(chosen), (window, launches) =>
                {
                    Click(Button(window, "Launch"));
                    LaunchOptionsWindow dialog = Dialog(window);
                    ComboBox permission = Combo(dialog, "PermissionMode");
                    Check(permission.Items.Cast<string>().SequenceEqual(LaunchPermissions.For(HarnessKind.Claude).Select(level => level.Label)) && permission.SelectedIndex == 0, "The dialog should list Claude's permission levels, starting at the default.");
                    Check(!Text(dialog).Contains("without asking. Use only", StringComparison.Ordinal), "The default level should not warn.");
                    Click(Button(dialog, "Browse..."));
                    Check(Field(dialog, "LaunchFolder").Text == chosen, "Browse did not fill in the chosen folder.");
                    permission.SelectedIndex = permission.Items.Cast<string>().ToList().IndexOf("Skip all permission prompts"); Dispatcher.UIThread.RunJobs();
                    Check(Text(dialog).Contains("--dangerously-skip-permissions", StringComparison.Ordinal) && Text(dialog).Contains("Use only in a sandbox", StringComparison.Ordinal), "The bypass level should name its flag and warn.");
                    Confirm(window);
                    Check(launches.Count == 1 && launches[0].WorkingDirectory == chosen && launches[0].Arguments.SequenceEqual(new[] { "--dangerously-skip-permissions" }), "The launch did not use the chosen folder and permission flag.");
                    Check(store.Preference("workspace") == store.Root && store.Preference("launch-permission-claude") == "bypass", "The folder should stay unsaved and the permission level remembered.");
                    Check(Text(window).Contains("Launched Launch test (Skip all permission prompts).", StringComparison.Ordinal), "The launch was not confirmed with its permission level.");
                    Click(Button(window, "Launch")); dialog = Dialog(window);
                    Check(Field(dialog, "LaunchFolder").Text == store.Root && Combo(dialog, "PermissionMode").SelectedItem as string == "Skip all permission prompts", "The next launch should propose the saved folder and the remembered level.");
                    Combo(dialog, "PermissionMode").SelectedIndex = 1;
                    Field(dialog, "LaunchFolder").Text = chosen;
                    dialog.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "SaveFolder").IsChecked = true;
                    Confirm(window);
                    Check(launches[1].Arguments.SequenceEqual(new[] { "--permission-mode", "acceptEdits" }) && store.Preference("launch-permission-claude") == "accept-edits", "Changing the level did not apply or was not remembered.");
                    Check(store.Preference("workspace") == chosen && window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Project with spaces..."), "Saving the folder did not update the default and sidebar.");
                });
            });
            Add("LaunchDialogCancelDoesNotLaunch", (_, store) => WithLaunchWindow(store, _ => Task.FromResult<string?>(null), (window, launches) =>
            {
                Click(Button(window, "Launch"));
                Combo(Dialog(window), "PermissionMode").SelectedIndex = 4;
                Click(Button(Dialog(window), "Browse..."));
                Click(Button(Dialog(window), "Cancel"));
                Check(window.ActiveLaunchOptions == null && launches.Count == 0, "Cancel launched a session or left the dialog open.");
                Click(Button(window, "Launch")); Dialog(window).Close(); Dispatcher.UIThread.RunJobs();
                Check(launches.Count == 0 && store.Preference("workspace") == store.Root && store.Preference("launch-permission-claude") == null, "Closing the dialog launched or saved choices.");
            }));
            Add("SidebarFolderStillUpdatesDefault", (_, store) =>
            {
                string chosen = Directory.CreateDirectory(Path.Combine(store.Root, "Saved project")).FullName;
                WithLaunchWindow(store, _ => Task.FromResult<string?>(chosen), (window, launches) =>
                {
                    Click(Button(window, Path.GetFileName(store.Root) + "..."));
                    Check(launches.Count == 0 && store.Preference("workspace") == chosen, "The sidebar folder picker did not save the default without launching.");
                    Click(Button(window, "Launch")); Confirm(window);
                    Check(launches.Select(launch => launch.WorkingDirectory).SequenceEqual(new[] { chosen }), "Launch did not propose the updated sidebar folder.");
                });
            });
            Add("LaunchDialogPickerFailureIsReported", (_, store) => WithLaunchWindow(store,
                _ => Task.FromException<string?>(new IOException("Folder picker unavailable.")), (window, launches) =>
            {
                Click(Button(window, "Launch")); Click(Button(Dialog(window), "Browse..."));
                Check(launches.Count == 0 && Text(Dialog(window)).Contains("Folder picker unavailable.", StringComparison.Ordinal), "Picker failure escaped or was not reported in the dialog.");
                Check(Field(Dialog(window), "LaunchFolder").Text == store.Root, "Picker failure changed the proposed folder.");
            }));
            Add("LaunchDialogRejectsMissingFolder", (_, store) => WithLaunchWindow(store, _ => Task.FromResult<string?>(null), (window, launches) =>
            {
                Click(Button(window, "Launch"));
                Field(Dialog(window), "LaunchFolder").Text = Path.Combine(store.Root, "removed-folder"); Click(Button(Dialog(window), "Launch"));
                Check(launches.Count == 0 && window.ActiveLaunchOptions != null && Text(Dialog(window)).Contains("does not exist", StringComparison.Ordinal), "A missing folder was accepted or lacks a useful error.");
                Field(Dialog(window), "LaunchFolder").Text = " "; Click(Button(Dialog(window), "Launch"));
                Check(launches.Count == 0 && Text(Dialog(window)).Contains("Choose a folder", StringComparison.Ordinal), "An empty folder was accepted.");
                Check(store.Preference("workspace") == store.Root, "A rejected folder changed the saved folder.");
            }));
            Add("LaunchTerminalFailureIsReported", (_, store) => WithLaunchWindow(store, _ => Task.FromResult<string?>(store.Root), (window, launches) =>
            {
                Click(Button(window, "Launch"));
                Combo(Dialog(window), "PermissionMode").SelectedIndex = 4;
                Dialog(window).GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "SaveFolder").IsChecked = true;
                Field(Dialog(window), "LaunchFolder").Text = Directory.CreateDirectory(Path.Combine(store.Root, "Other")).FullName;
                Confirm(window);
                Check(Text(window).Contains("No test terminal available.", StringComparison.Ordinal), "Terminal failure was not reported.");
                Check(store.Preference("workspace") == store.Root && store.Preference("launch-permission-claude") == null, "A failed launch saved its choices.");
            }, failLaunch: true));
            Add("OneLaunchDialogAtATime", (_, store) => WithLaunchWindow(store, _ => Task.FromResult<string?>(null), (window, launches) =>
            {
                Click(Button(window, "Launch")); LaunchOptionsWindow first = Dialog(window);
                Check(window.LaunchProfileAsync(store.Selected(HarnessKind.Claude).Id, CancellationToken.None).IsCompletedSuccessfully, "A second launch should return at once.");
                Check(window.OwnedWindows.OfType<LaunchOptionsWindow>().Count() == 1 && ReferenceEquals(window.ActiveLaunchOptions, first), "A second launch opened another dialog.");
                Confirm(window);
                Check(launches.Count == 1, "The open dialog did not launch exactly once.");
            }));
            Add("CompactLayoutHasNoUnwantedFooter", (window, store) => Check(window.Width <= 800 && window.Height <= 550 && !Text(window).Contains("Stored on this device", StringComparison.Ordinal) && !window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Quit Kameo"), "The compact layout regressed."));
            Add("NativeMenuBarIsComplete", (window, store) =>
            {
                NativeMenu menu = NativeMenu.GetMenu(window) ?? throw new InvalidOperationException("Native menu missing.");
                Check(menu.Items.OfType<NativeMenuItem>().Select(item => item.Header).SequenceEqual(new[] { "File", "Edit", "Profiles", "View", "Window", "Help" }), "Native menu sections missing.");
            });
            Add("SearchWidthTracksCardsWithScrollingAndResize", (window, store) =>
            {
                void Verify()
                {
                    Sync(window);
                    TextBox search = Field(window, "SearchProfiles");
                    StackPanel cards = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "ProfileCards");
                    Check(cards.Bounds.Width > 100 && Math.Abs(search.Bounds.Width - cards.Bounds.Width) < 1, "Search and card widths differ.");
                    Check(Math.Abs(search.TranslatePoint(default, window)!.Value.X - cards.TranslatePoint(default, window)!.Value.X) < 1, "Search and cards are not aligned.");
                }
                Verify();
                for (int index = 0; index < 8; index++) store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Profile " + index, ApiKey = "synthetic" });
                window.Refresh(); Verify(); window.Width = 720; Verify();
                Field(window, "SearchProfiles").Text = "Profile 1"; Verify();
            });
            Add("ProfileSubmenusLaunchTheCurrentSelection", (_, store) =>
            {
                store.SetPreference("workspace", store.Root);
                List<string> launches = new List<string>();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(store.Root),
                    request => { Check(!request.Login && request.WorkingDirectory == store.Root, "Menu launch used the wrong mode or folder."); launches.Add(request.ProfileId); });
                try
                {
                    window.Show();
                    foreach (HarnessKind harness in HarnessCatalog.All)
                    {
                        NativeMenu choices = HarnessMenu(window, harness);
                        Check(choices.Items.Count == 3 && !((NativeMenuItem)choices.Items[0]).IsEnabled && choices.Items[1] is NativeMenuItemSeparator, "An empty harness can launch, or the menu has a separate Launch in... item.");
                        Profile first = store.Create(new Profile { Harness = harness, Name = "First", Kind = CredentialKind.Native });
                        Profile second = store.Create(new Profile { Harness = harness, Name = "Second", Kind = CredentialKind.Native });
                        window.Refresh(); Check(ReferenceEquals(choices, HarnessMenu(window, harness)), "Native submenu was replaced.");
                        Check(((NativeMenuItem)choices.Items[0]).Header == "Launch First...", "The menu should say Launch opens options.");
                        Click((NativeMenuItem)choices.Items[0]); Confirm(window); Check(launches.Last() == first.Id, "Menu launched another harness or profile.");
                        store.Select(second.Id);
                        // Resolve selection when invoked, even before the periodic UI refresh.
                        Click((NativeMenuItem)choices.Items[0]); Confirm(window); Check(launches.Last() == second.Id, "Menu launched a stale selection.");
                        window.RefreshIfChanged(); Check(((NativeMenuItem)choices.Items[0]).Header == "Launch Second...", "Selection label did not refresh.");
                        Click((NativeMenuItem)choices.Items[2]); Check(Title(window) == HarnessCatalog.Name(harness), "Manage profiles did not navigate.");
                        store.Delete(second.Id); store.Delete(first.Id); window.RefreshIfChanged();
                        Check(!((NativeMenuItem)choices.Items[0]).IsEnabled, "Deleted profiles remain launchable.");
                    }
                }
                finally { window.Close(); }
            });
            AddAsync("TrayMenuLaunchesEachProfileAndLowestUsage", async (_, store, cancellationToken) =>
            {
                store.SetPreference("workspace", store.Root);
                List<string> launches = new List<string>();
                List<string> folders = new List<string>();
                Dictionary<string, double> usage = new Dictionary<string, double>();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(store.Root),
                    request => { Check(!request.Login, "Tray launch used login mode."); launches.Add(request.ProfileId); folders.Add(request.WorkingDirectory); },
                    (id, token) => Task.FromResult(usage.TryGetValue(id, out double used)
                        ? new UsageReport { ProfileId = id, Status = UsageStatus.Available, Source = UsageSource.Live, Windows = new[] { new UsageWindow("Weekly", used, null, 10080) } }
                        : new UsageReport { ProfileId = id, Status = UsageStatus.SignedOut, Message = "Sign in." }));
                int shown = 0; int quits = 0;
                try
                {
                    TrayMenu tray = new TrayMenu(store, window, () => { shown++; window.Show(); }, () => quits++);
                    tray.Refresh();
                    NativeMenu menu = tray.Menu;
                    NativeMenuItem Top(string header) => menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);
                    NativeMenuItem Child(NativeMenu parent, string header) => parent.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);
                    Check(menu.Items.OfType<NativeMenuItem>().First().Header == "About Kameo", "Tray About is not the first item.");
                    Click(Top("About Kameo")); Dispatcher.UIThread.RunJobs();
                    AboutWindow about = window.OwnedWindows.OfType<AboutWindow>().Single();
                    Check(about.IsVisible && shown == 1, "Tray About did not show the About box over the manager."); about.Close();
                    foreach (HarnessKind harness in HarnessCatalog.All)
                    {
                        NativeMenu choices = Top(HarnessCatalog.Name(harness)).Menu!;
                        Check(choices.Items.Count == 1 && !((NativeMenuItem)choices.Items[0]).IsEnabled && ((NativeMenuItem)choices.Items[0]).Header == "No profiles yet", "An empty harness should only say it has no profiles.");
                        Profile first = store.Create(new Profile { Harness = harness, Name = "First", Kind = CredentialKind.Native });
                        Profile second = store.Create(new Profile { Harness = harness, Name = "Second", Kind = CredentialKind.ApiKey, ApiKey = "synthetic" });
                        tray.Refresh(); Check(ReferenceEquals(menu, tray.Menu), "Tray menu was replaced.");
                        choices = Top(HarnessCatalog.Name(harness)).Menu!;
                        Check(choices.Items.OfType<NativeMenuItem>().Select(item => item.Header).SequenceEqual(new[] { "First", "Second" }), "Each profile should be its own submenu, without a separate launch item.");
                        NativeMenuItem firstItem = Child(choices, "First");
                        Check(firstItem.IsChecked && !Child(choices, "Second").IsChecked, "The selected profile should be ticked.");
                        Check(firstItem.Menu!.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).Select(item => item.Header).SequenceEqual(new[] { "Launch...", "Use as default" }), "A profile submenu should offer Launch... and Use as default.");
                        Check(!Child(firstItem.Menu!, "Use as default").IsEnabled && Child(firstItem.Menu!, "Use as default").IsChecked, "The default profile's Use as default should be ticked and disabled.");
                        int shownBefore = shown;
                        Click(Child(firstItem.Menu!, "Launch...")); Confirm(window);
                        Check(launches.Last() == first.Id && folders.Last() == store.Root && shown == shownBefore, "Tray launch used the wrong profile or folder, or opened the manager.");
                        NativeMenu secondMenu = Child(choices, "Second").Menu!;
                        Click(Child(secondMenu, "Launch...")); Confirm(window); Check(launches.Last() == second.Id && store.Selected(harness).Id == first.Id, "Launching a profile should not change the default.");
                        int launched = launches.Count;
                        Click(Child(secondMenu, "Launch...")); Click(Button(Dialog(window), "Cancel"));
                        Check(launches.Count == launched && shown == shownBefore, "Canceling a tray launch started a session or opened the manager.");
                        // Hidden in the tray: the dialog appears on its own, without the manager.
                        window.Hide();
                        Click(Child(secondMenu, "Launch..."));
                        LaunchOptionsWindow standalone = window.ActiveLaunchOptions ?? throw new InvalidOperationException("The tray did not open launch options while the manager was hidden.");
                        Check(standalone.Owner == null && standalone.IsVisible && !window.IsVisible && shown == shownBefore, "The hidden-manager dialog should stand alone without showing the manager.");
                        Click(Button(standalone, "Launch"));
                        Check(launches.Count == launched + 1 && launches.Last() == second.Id, "The standalone dialog did not launch.");
                        window.Show();
                        Click(Child(secondMenu, "Use as default")); Check(store.Selected(harness).Id == second.Id, "Use as default did not change the profile.");
                        choices = Top(HarnessCatalog.Name(harness)).Menu!;
                        Check(Child(choices, "Second").IsChecked && !Child(choices, "First").IsChecked, "The tick did not follow the default.");
                        Profile third = store.Create(new Profile { Harness = harness, Name = "Third", Kind = CredentialKind.Native });
                        tray.Refresh(); choices = Top(HarnessCatalog.Name(harness)).Menu!;
                        if (HarnessCatalog.SupportsUsage(harness))
                        {
                            Check(choices.Items[0] is NativeMenuItem { Header: "Lowest usage...", Menu: null } && choices.Items[1] is NativeMenuItemSeparator, "Lowest usage... should come first as one item, then a separator.");
                            NativeMenuItem lowest = (NativeMenuItem)choices.Items[0];
                            usage[first.Id] = 70; usage[third.Id] = 20;
                            Click(lowest); await Task.Delay(20, cancellationToken);
                            Check(Dialog(window).Title == "Launch Third", "Lowest usage should open options for the native login with the most headroom.");
                            Confirm(window);
                            Check(launches.Last() == third.Id && folders.Last() == store.Root && store.Selected(harness).Id == second.Id, "Lowest usage should launch the native login with the most headroom.");
                            usage[first.Id] = 5;
                            await window.CheckUsageAsync(harness, true, cancellationToken);
                            Click(lowest); await Task.Delay(20, cancellationToken); Confirm(window);
                            Check(launches.Last() == first.Id, "Lowest usage should follow refreshed usage.");
                        }
                        else Check(!choices.Items.OfType<NativeMenuItem>().Any(item => item.Header == "Lowest usage"), HarnessCatalog.Name(harness) + " has no usage, so it should not offer Lowest usage.");
                        store.Delete(second.Id); store.Delete(first.Id); store.Delete(third.Id); tray.Refresh();
                        Check(((NativeMenuItem)Top(HarnessCatalog.Name(harness)).Menu!.Items[0]).Header == "No profiles yet", "Deleted profiles remain in the tray.");
                    }
                    Click(Top("Quit Kameo")); Check(quits == 1, "Tray Quit did not quit.");
                }
                finally { window.Close(); }
            });
            Add("AboutUsesPaletteLogoAndRepositoryLink", (window, store) =>
            {
                bool originalDark = Palette.Dark; string originalScheme = Palette.Scheme.Id;
                try
                {
                    foreach (ColorScheme scheme in Palette.Schemes)
                    foreach (bool dark in new[] { false, true })
                    {
                        Palette.Apply(Application.Current!, dark, scheme.Id); window.ShowAbout();
                        AboutWindow about = window.OwnedWindows.OfType<AboutWindow>().Single();
                        Sync(about);
                        Check(about.LogoResource == (dark ? "Kameo.Logo" : "Kameo.LogoDark") && about.GetVisualDescendants().OfType<Image>().Single().Source != null, "The logo does not match the palette.");
                        Check(about.GetVisualDescendants().OfType<HyperlinkButton>().Single().NavigateUri?.AbsoluteUri == "https://github.com/jchristn/Kameo", "Repository link is missing.");
                        Check(!Text(about).Contains("Your agent identities", StringComparison.Ordinal) && !Text(about).Contains("A local credential", StringComparison.Ordinal), "Removed description remains.");
                        TextBlock copyright = about.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Copyright (c) 2026 Joel Christner");
                        TextBlock license = about.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "MIT License");
                        Check(license.TranslatePoint(default, about)!.Value.Y - copyright.TranslatePoint(default, about)!.Value.Y - copyright.Bounds.Height >= 16, "Copyright and license need more space.");
                        Button close = Button(about, "Close");
                        TextBlock closeLabel = close.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Close");
                        double labelCenter = closeLabel.TranslatePoint(default, close)!.Value.X + (closeLabel.Bounds.Width / 2);
                        Check(close.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center && Math.Abs(labelCenter - (close.Bounds.Width / 2)) <= 1, "The Close label is not centered in its button.");
                        Click(close); Check(!about.IsVisible, "About did not close.");
                    }
                }
                finally { Palette.Apply(Application.Current!, originalDark, originalScheme); }
            });
            AddAsync("SplashClosesAfterTwoSecondsButAboutStaysOpen", async (window, store, cancellationToken) =>
            {
                AboutWindow splash = new AboutWindow(splash: true); splash.Show(window);
                window.ShowAbout(); AboutWindow about = window.OwnedWindows.OfType<AboutWindow>().Single(child => child != splash);
                await Task.Delay(1100, cancellationToken); Check(splash.IsVisible, "Splash closed before two seconds.");
                await Task.Delay(1200, cancellationToken); Dispatcher.UIThread.RunJobs();
                Check(!splash.IsVisible && about.IsVisible && window.IsVisible, "Splash timing closed the wrong window or did not dismiss.");
                about.Close();
                AboutWindow dismissed = new AboutWindow(splash: true); dismissed.Show(window); dismissed.Close();
                await Task.Delay(2100, cancellationToken); Check(window.IsVisible, "An early-closed splash affected the manager.");
            });
            Add("HarnessNavigation", (window, store) =>
            {
                NativeMenu menu = NativeMenu.GetMenu(window)!;
                NativeMenuItem profiles = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Profiles");
                Check(profiles.Menu!.Items.Count == 4, "All harnesses must be in the native menu.");
                Button mux = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is Grid grid && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Mux"));
                Click(mux); Check(Title(window) == "Mux", "Harness navigation did not update the heading.");
            });
            Add("EditorValidation", (window, store) =>
            {
                Click(Button(window, "+  Add profile"));
                Window editor = window.OwnedWindows.Single();
                Click(Button(editor, "Save profile"));
                Check(store.List().Count == 0 && Text(editor).Contains("Enter a profile name.", StringComparison.Ordinal), "Invalid form was saved or lacks a useful error.");
            });
            Add("CreateProfileFromEditor", (window, store) =>
            {
                Click(Button(window, "+  Add profile")); Window editor = window.OwnedWindows.Single();
                Field(editor, "ProfileName").Text = "UI work"; Field(editor, "ApiKey").Text = "synthetic-ui-key";
                Check(Field(editor, "ApiKey").PasswordChar != '\0', "Key field is not masked.");
                Click(Button(editor, "Save profile")); window.Refresh();
                Check(store.List().Count == 1 && store.Selected(HarnessKind.Claude).Name == "UI work", "Editor did not save and select the first profile.");
                Check(!Text(window).Contains("synthetic-ui-key", StringComparison.Ordinal), "Profile card leaked an API key.");
            });
            Add("SearchFiltersProfiles", (window, store) =>
            {
                store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Work", ApiKey = "test" });
                store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Personal", ApiKey = "test" }); window.Refresh();
                Field(window, "SearchProfiles").Text = "personal"; Dispatcher.UIThread.RunJobs();
                Sync(window);
                Check(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Personal") && window.GetVisualDescendants().OfType<Button>().Count(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Edit") == 1, "Search did not filter profile cards.");
            });
            Add("SwitchProfileFromCard", (window, store) =>
            {
                store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Work", ApiKey = "test" });
                Profile other = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Personal", ApiKey = "test" }); window.Refresh();
                Click(Button(window, "Use profile"));
                Check(store.Selected(HarnessKind.Claude).Id == other.Id, "Use profile did not persist selection.");
            });
            Add("BlankEditPreservesStoredKey", (window, store) =>
            {
                Profile profile = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Work", ApiKey = "original-synthetic" }); window.Refresh();
                Click(Button(window, "Edit")); Window editor = window.OwnedWindows.Single();
                Check(string.IsNullOrEmpty(Field(editor, "ApiKey").Text), "Editor unnecessarily exposes the stored key.");
                Field(editor, "ProfileName").Text = "Renamed"; Click(Button(editor, "Save profile"));
                Check(store.Get(profile.Id).ApiKey == "original-synthetic" && store.Get(profile.Id).Name == "Renamed", "Editing the name lost the key.");
            });
            Add("DeleteRequiresConfirmation", (window, store) =>
            {
                store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Work", ApiKey = "test" }); window.Refresh();
                Click(Button(window, "Delete")); Check(store.List().Count == 1, "Deletion happened before confirmation.");
                Click(Button(window.OwnedWindows.Single(), "Cancel")); Check(store.List().Count == 1, "Cancel deleted the profile.");
            });
            Add("AppearancePreferencePersists", (window, store) =>
            {
                Click(Button(window, "Appearance..."));
                Window picker = window.OwnedWindows.Single();
                foreach (string palette in new[] { "Fern", "Ocean", "Iris", "Amber", "Rose", "Graphite", "Seafoam", "Lagoon", "Cobalt", "Lavender", "Coral", "Sand", "Espresso", "Midnight", "Paper", "Nord" })
                    Check(Text(picker).Contains(palette, StringComparison.Ordinal), "Palette choice missing: " + palette);
                Click(Button(picker, "Light"));
                Button ocean = picker.GetVisualDescendants().OfType<Button>().Single(button => button.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "Ocean"));
                Click(ocean);
                Check(store.Preference("theme") == "light" && store.Preference("palette") == "ocean", "Palette selection was not saved.");
            });
            Add("MuxSharedFileCanBeChosenWhenCreatingProfile", (window, store) =>
            {
                string source = Path.Combine(store.Root, "Shared endpoints.json");
                File.WriteAllText(source, "{\"endpoints\":[{\"name\":\"shared\",\"adapterType\":\"openai\",\"model\":\"synthetic-model\"}]}");
                Navigate(window, "Mux"); Click(Button(window, "+  Add profile")); Window editor = window.OwnedWindows.Single();
                editor.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AuthenticationKind").SelectedIndex = 1;
                Field(editor, "ProfileName").Text = "Shared Mux"; Field(editor, "MuxEndpointsFile").Text = source;
                Check(Button(editor, "Browse endpoints...").IsEffectivelyVisible, "The endpoints file picker is not visible for a Mux file profile.");
                Click(Button(editor, "Save profile")); window.Refresh();
                Check(store.Selected(HarnessKind.Mux).MuxEndpointsFile == source, "The editor did not persist the shared endpoints file.");
                Check(Text(window).Contains("Shared endpoints: Shared endpoints.json", StringComparison.Ordinal), "The linked source is not shown on the profile card.");
                Check(!window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Setup"), "Shared profiles should not offer setup that edits only the temporary copy.");
            });
            Add("MuxInvalidFileEditPreservesProfile", (window, store) =>
            {
                Profile profile = store.Create(new Profile { Harness = HarnessKind.Mux, Kind = CredentialKind.Native, Name = "Original" });
                string source = Path.Combine(store.Root, "invalid.json"); File.WriteAllText(source, "{}");
                Navigate(window, "Mux"); Click(Button(window, "Edit")); Window editor = window.OwnedWindows.Single();
                Field(editor, "ProfileName").Text = "Changed"; Field(editor, "MuxEndpointsFile").Text = source;
                Click(Button(editor, "Save profile"));
                Check(store.Get(profile.Id).Name == "Original" && store.Get(profile.Id).MuxEndpointsFile == "", "Invalid shared file caused a partial profile update.");
                Check(Text(editor).Contains("valid Mux credentials", StringComparison.Ordinal), "Invalid file error is missing.");
            });
            Add("AntigravityEditorOffersAccountsAndApiKeys", (window, store) =>
            {
                Navigate(window, "Antigravity"); Click(Button(window, "+  Add profile")); Window editor = window.OwnedWindows.Single();
                ComboBox authentication = editor.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AuthenticationKind");
                Check(authentication.ItemCount == 2 && authentication.SelectedIndex == 1, "Google account must be available and selected for new profiles.");
                authentication.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                Field(editor, "ProfileName").Text = "Google API"; Field(editor, "ApiKey").Text = "synthetic-google-key";
                Click(Button(editor, "Save profile")); window.Refresh();
                Check(store.Selected(HarnessKind.Antigravity).Kind == CredentialKind.ApiKey && Text(window).Contains("Google API", StringComparison.Ordinal) && Button(window, "Launch").IsEnabled, "Antigravity API profile is not launchable from its card.");
            });
            Add("AntigravityAccountCreationAndCredentialStatus", (window, store) =>
            {
                Navigate(window, "Antigravity"); Click(Button(window, "+  Add profile")); Window editor = window.OwnedWindows.Single();
                Check(Text(editor).Contains("paste the authorization code", StringComparison.Ordinal) && Text(editor).Contains(AntigravityCompatibility.SupportedVersion, StringComparison.Ordinal), "Account sign-in guidance is missing.");
                Field(editor, "ProfileName").Text = "Google Work"; Click(Button(editor, "Save profile")); window.Refresh();
                Profile profile = store.Selected(HarnessKind.Antigravity);
                Check(profile.Kind == CredentialKind.Native && !profile.HasNativeCredential, "A Google account requires an API key or was shown as authenticated before sign-in.");
                Check(Button(window, "Sign in").IsVisible && Button(window, "Launch").IsEnabled, "Account actions are unavailable.");
                new CredentialFiles(store).ImportText(profile.Id, "{\"auth_method\":\"consumer\",\"token\":{\"access_token\":\"synthetic\",\"refresh_token\":\"synthetic-refresh\"}}");
                window.Refresh(); Check(Text(window).Contains("Google account saved", StringComparison.Ordinal), "Saved account status was not refreshed.");
                Click(Button(window, "Edit")); editor = window.OwnedWindows.Single();
                ComboBox authentication = editor.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AuthenticationKind");
                Check(authentication.SelectedIndex == 1 && !authentication.IsEnabled, "Editing changed the account's authentication kind.");
            });
            Add("AntigravitySignInLaunchesTheCorrectProfile", (_, store) =>
            {
                Profile profile = store.Create(new Profile { Harness = HarnessKind.Antigravity, Kind = CredentialKind.Native, Name = "Google Personal" });
                store.SetPreference("workspace", store.Root); List<bool> logins = new List<bool>();
                MainWindow window = new MainWindow(store, () => { }, () => { }, cancellationToken => Task.FromResult<string?>(store.Root), request =>
                { Check(request.ProfileId == profile.Id && request.WorkingDirectory == store.Root, "Account action used the wrong profile or folder."); logins.Add(request.Login); });
                try
                {
                    window.Show(); Navigate(window, "Antigravity"); Click(Button(window, "Sign in")); Click(Button(window, "Launch"));
                    Check(Combo(Dialog(window), "PermissionMode").Items.Cast<string>().SequenceEqual(LaunchPermissions.For(HarnessKind.Antigravity).Select(level => level.Label)), "The dialog should offer Antigravity's own permission levels.");
                    Confirm(window);
                    Check(logins.SequenceEqual(new[] { true, false }), "Account actions did not distinguish login from launch.");
                }
                finally { window.Close(); }
            });
            AddAsync("UsageIsCheckedOnNavigationAndCached", async (_, store, cancellationToken) =>
            {
                Profile work = store.Create(new Profile { Harness = HarnessKind.Codex, Kind = CredentialKind.Native, Name = "Codex Work" });
                Profile personal = store.Create(new Profile { Harness = HarnessKind.Codex, Kind = CredentialKind.Native, Name = "Codex Personal" });
                store.Create(new Profile { Harness = HarnessKind.Codex, Kind = CredentialKind.ApiKey, Name = "Codex Key", ApiKey = "synthetic" });
                store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.ApiKey, Name = "Claude Key", ApiKey = "synthetic" });
                List<string> checkedIds = new List<string>();
                TestClock clock = new TestClock();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(store.Root), _ => { }, (id, token) =>
                {
                    checkedIds.Add(id);
                    UsageReport report = id == work.Id
                        ? new UsageReport
                        {
                            ProfileId = id, ProfileName = "Codex Work", Harness = HarnessKind.Codex, Status = UsageStatus.Available, Source = UsageSource.Live, Plan = "Pro",
                            Detail = "Credits 2,500", ObservedUtc = DateTimeOffset.UtcNow,
                            Windows = new[] { new UsageWindow("5-hour", 42, DateTimeOffset.UtcNow.AddHours(2), 300), new UsageWindow("Weekly", 95, null, 10080) }
                        }
                        : new UsageReport { ProfileId = id, ProfileName = "Codex Personal", Harness = HarnessKind.Codex, Status = UsageStatus.Expired, Message = "The saved sign-in has expired." };
                    return Task.FromResult(report);
                }, clock);
                try
                {
                    window.Show(); Sync(window);
                    Check(checkedIds.Count == 0 && !window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string is "Refresh usage" or "Launch lowest usage"), "Harnesses without native logins should not check or offer usage.");
                    Navigate(window, "Codex");
                    await Task.Delay(20, cancellationToken);
                    string text = Text(window);
                    Check(checkedIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(new[] { work.Id, personal.Id }.OrderBy(id => id, StringComparer.Ordinal)), "Navigation should check each native login once.");
                    Check(text.Contains("5-hour", StringComparison.Ordinal) && text.Contains("42% used · resets in", StringComparison.Ordinal) && text.Contains("95% used", StringComparison.Ordinal)
                        && text.Contains("Pro · Credits 2,500 · Live, ", StringComparison.Ordinal), "Usage windows or plan summary are missing.");
                    Check(text.Contains("The saved sign-in has expired.", StringComparison.Ordinal) && text.Contains(MainWindow.UsageCheckedMessage(HarnessKind.Codex, clock.GetLocalNow()), StringComparison.Ordinal), "Failure messages or completion status are missing.");
                    Check(MainWindow.UsageCheckedMessage(HarnessKind.Codex, clock.GetLocalNow()).StartsWith("Codex usage checked at ", StringComparison.Ordinal), "The completion status should include the local check time.");
                    ProgressBar[] bars = window.GetVisualDescendants().OfType<ProgressBar>().ToArray();
                    Check(bars.Length == 2 && bars[0].Value == 42 && bars[1].Value == 95, "Usage bars are wrong.");
                    Navigate(window, "Claude Code"); Navigate(window, "Codex");
                    clock.Now += TimeSpan.FromMinutes(14);
                    Navigate(window, "Mux"); Navigate(window, "Codex");
                    Check(checkedIds.Count == 2 && window.GetVisualDescendants().OfType<ProgressBar>().Count() == 2, "Usage within 15 minutes should come from the cache.");
                    clock.Now += TimeSpan.FromMinutes(2);
                    Navigate(window, "Claude Code"); Navigate(window, "Codex");
                    Check(checkedIds.Count == 4, "Usage older than 15 minutes should be checked again on navigation.");
                    Click(Button(window, "Refresh usage"));
                    Check(checkedIds.Count == 6, "Refresh usage should bypass the cache.");
                    NativeMenu view = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "View").Menu!;
                    Click(view.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Refresh Usage"));
                    Check(checkedIds.Count == 8, "View > Refresh Usage should bypass the cache.");
                    Navigate(window, "Mux");
                    Click(view.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Refresh Usage"));
                    Check(Text(window).Contains("Plan usage is available for Claude Code and Codex", StringComparison.Ordinal) && checkedIds.Count == 8, "The menu should explain unsupported harnesses without checking.");
                }
                finally { window.Close(); }
            });
            AddAsync("UsageRefreshesInBackgroundForEveryHarness", async (_, store, cancellationToken) =>
            {
                Profile claude = store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.Native, Name = "Claude Work" });
                Profile codex = store.Create(new Profile { Harness = HarnessKind.Codex, Kind = CredentialKind.Native, Name = "Codex Work" });
                store.Create(new Profile { Harness = HarnessKind.Antigravity, Kind = CredentialKind.Native, Name = "Google Work" });
                List<string> checkedIds = new List<string>();
                TestClock clock = new TestClock();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(store.Root), _ => { }, (id, token) =>
                {
                    checkedIds.Add(id);
                    return Task.FromResult(new UsageReport { ProfileId = id, Harness = id == claude.Id ? HarnessKind.Claude : HarnessKind.Codex, Status = UsageStatus.Expired, Message = "The saved sign-in has expired." });
                }, clock);
                try
                {
                    Check(MainWindow.UsageRefreshInterval == TimeSpan.FromMinutes(15) && MainWindow.UsageRefreshInterval == MainWindow.UsageCacheDuration, "Background refresh should match the 15-minute usage cache.");
                    Check(window.RefreshesUsageInBackground, "The background usage refresh should start with the window, even before it is shown.");
                    window.Show(); Sync(window);
                    await Task.Delay(20, cancellationToken);
                    Check(checkedIds.SequenceEqual(new[] { claude.Id }), "Opening should check only the harness on screen.");
                    await window.RefreshUsageInBackgroundAsync(cancellationToken);
                    Check(checkedIds.SequenceEqual(new[] { claude.Id, codex.Id }), "A background refresh should check stale logins for every usage harness without navigation.");
                    clock.Now += TimeSpan.FromMinutes(5);
                    await window.RefreshUsageInBackgroundAsync(cancellationToken);
                    Check(checkedIds.Count == 2, "A background refresh should not recheck logins checked moments ago.");
                    clock.Now += TimeSpan.FromMinutes(10);
                    await window.RefreshUsageInBackgroundAsync(cancellationToken);
                    Check(checkedIds.Count == 4, "The next 15-minute refresh should recheck every login.");
                    Check(Text(window).Contains(MainWindow.UsageCheckedMessage(HarnessKind.Claude, clock.GetLocalNow()), StringComparison.Ordinal)
                        && Text(window).Contains("The saved sign-in has expired.", StringComparison.Ordinal), "A background refresh should surface expired sign-ins and the check time.");
                    Navigate(window, "Codex");
                    Check(checkedIds.Count == 4 && Text(window).Contains("The saved sign-in has expired.", StringComparison.Ordinal), "Navigating after a background refresh should use its results.");
                }
                finally { window.Close(); }
                Check(!window.RefreshesUsageInBackground, "Closing the window should stop the background usage refresh.");
            });
            AddAsync("LaunchLowestUsageChoosesMostHeadroom", async (_, store, cancellationToken) =>
            {
                Profile busy = store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.Native, Name = "Busy" });
                Profile roomy = store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.Native, Name = "Roomy" });
                Profile expired = store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.Native, Name = "Expired" });
                store.SetPreference("workspace", store.Root);
                List<string> launches = new List<string>();
                Dictionary<string, double[]> usage = new Dictionary<string, double[]> { [busy.Id] = new[] { 10.0, 80.0 }, [roomy.Id] = new[] { 60.0, 30.0 } };
                TestClock clock = new TestClock();
                MainWindow window = new MainWindow(store, () => { }, () => { }, _ => Task.FromResult<string?>(store.Root), request => launches.Add(request.ProfileId + "@" + request.WorkingDirectory), (id, token) =>
                    Task.FromResult(usage.TryGetValue(id, out double[]? used)
                        ? new UsageReport { ProfileId = id, Harness = HarnessKind.Claude, Status = UsageStatus.Available, Source = UsageSource.Live, ObservedUtc = clock.Now,
                            Windows = new[] { new UsageWindow("5-hour", used[0], null, 300), new UsageWindow("Weekly", used[1], null, 10080) } }
                        : new UsageReport { ProfileId = id, Harness = HarnessKind.Claude, Status = UsageStatus.Expired, Message = "Expired." }), clock);
                try
                {
                    window.Show(); Sync(window);
                    Button lowest = Button(window, "Launch lowest usage");
                    Check(ToolTip.GetTip(lowest) as string == "Choose launch options for Roomy (60% used at its tightest limit).", "The tooltip should name the login with the most headroom.");
                    Click(lowest);
                    await Task.Delay(20, cancellationToken);
                    Check(Dialog(window).Title == "Launch Roomy", "Lowest usage should open options for the login with the most headroom.");
                    Confirm(window);
                    Check(launches.SequenceEqual(new[] { roomy.Id + "@" + store.Root }) && store.Selected(HarnessKind.Claude).Id == busy.Id, "Lowest usage should launch the login whose tightest limit is lowest, without changing the selection.");
                    Check(Text(window).Contains("Launched Roomy: 60% used at its tightest limit.", StringComparison.Ordinal), "The launch was not confirmed.");
                    usage.Clear();
                    clock.Now += TimeSpan.FromMinutes(16);
                    Click(Button(window, "Launch lowest usage"));
                    await Task.Delay(20, cancellationToken);
                    Check(launches.Count == 1 && window.ActiveLaunchOptions == null && Text(window).Contains("No plan usage is available", StringComparison.Ordinal), "Stale usage should be rechecked, and nothing launched without usage.");
                }
                finally { window.Close(); }
            });
            Add("ProfileCardsGroupImportAndUseIcons", (window, store) =>
            {
                store.Create(new Profile { Harness = HarnessKind.Claude, Kind = CredentialKind.Native, Name = "Native" });
                window.Refresh(); Sync(window);
                Check(!window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string is "Import..." or "Import current" or "Edit" or "Delete"), "Import, edit and delete should not be separate text buttons.");
                Button import = Button(window, "Import");
                Check(import.Flyout is MenuFlyout menu && menu.Items.OfType<MenuItem>().Select(item => item.Header as string).SequenceEqual(new[] { "Current Claude Code login", "Credential file..." }), "Import should offer the current login and a credential file.");
                Button edit = Button(window, "Edit"); Button delete = Button(window, "Delete"); Button launch = Button(window, "Launch");
                Check(edit.Content is PathIcon && delete.Content is PathIcon && ToolTip.GetTip(edit) as string == "Edit profile" && ToolTip.GetTip(delete) as string == "Delete profile", "Edit and delete should be labelled icons.");
                double Left(Control control) => control.TranslatePoint(default, window)!.Value.X;
                Border card = window.GetVisualDescendants().OfType<Border>().First(border => border.GetVisualDescendants().Contains(edit) && border.Padding.Left > 10);
                double cardRight = Left(card) + card.Bounds.Width;
                Check(Left(edit) > Left(import) && Left(delete) > Left(edit) && cardRight - (Left(delete) + delete.Bounds.Width) < 20, "Edit and delete should sit at the far right of the card.");
                Check(Left(launch) < Left(import), "Launch should come first.");
                Check(window.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible && button.GetVisualDescendants().Contains(edit) == false && card.GetVisualDescendants().Contains(button))
                    .All(button => Math.Abs(button.Bounds.Height - launch.Bounds.Height) < 0.5), "Every card button, including Import and the icons, should share one height: " + string.Join(", ", card.GetVisualDescendants().OfType<Button>().Select(button => (Avalonia.Automation.AutomationProperties.GetName(button) ?? button.Content as string) + "=" + button.Bounds.Height)));
            });
            return await ConsoleRunner.RunMainAsync(new[] { new TestSuiteDescriptor("Desktop", "Desktop workflows and native menu contracts", cases) }, args).ConfigureAwait(false);
        }

        private static void WithLaunchWindow(ProfileStore store, Func<CancellationToken, Task<string?>> pickFolder,
            Action<MainWindow, List<LaunchRequest>> verify, bool failLaunch = false)
        {
            Profile profile = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Launch test", ApiKey = "synthetic" });
            store.SetPreference("workspace", store.Root);
            List<LaunchRequest> launches = new List<LaunchRequest>();
            MainWindow window = new MainWindow(store, () => { }, () => { }, pickFolder, request =>
            {
                Check(request.ProfileId == profile.Id && !request.Login, "Launch used the wrong profile or started login instead of a session.");
                if (failLaunch) throw new InvalidOperationException("No test terminal available.");
                launches.Add(request);
            });
            try { window.Show(); Sync(window); verify(window, launches); }
            finally { window.Close(); }
        }

        private static Button Button(Window window, string label) { Sync(window); return window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == label || Avalonia.Automation.AutomationProperties.GetName(button) == label); }
        private static void Navigate(MainWindow window, string harness) => Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is Grid grid && grid.Children.OfType<TextBlock>().Any(text => text.Text == harness)));
        private static TextBox Field(Window window, string name) { Sync(window); return window.GetVisualDescendants().OfType<TextBox>().Single(field => field.Name == name); }
        private static ComboBox Combo(Window window, string name) { Sync(window); return window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == name); }
        private static LaunchOptionsWindow Dialog(MainWindow window)
        {
            Dispatcher.UIThread.RunJobs();
            LaunchOptionsWindow dialog = window.ActiveLaunchOptions ?? throw new InvalidOperationException("The launch options dialog did not open.");
            Sync(dialog);
            return dialog;
        }
        private static void Confirm(MainWindow window) => Click(Button(Dialog(window), "Launch"));
        private static string Text(Window window) { Sync(window); return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)); }
        private static string? Title(Window window) { Sync(window); return window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "HarnessTitle").Text; }
        private static void Sync(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        private static void Click(NativeMenuItem item)
        {
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
        }
        private static NativeMenu HarnessMenu(MainWindow window, HarnessKind harness) => NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
            .Single(item => item.Header == "Profiles").Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == HarnessCatalog.Name(harness)).Menu!;
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
