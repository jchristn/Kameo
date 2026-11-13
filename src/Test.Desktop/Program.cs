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
            void Add(string name, Action<MainWindow, ProfileStore> action) => cases.Add(new TestCaseDescriptor("Desktop", name, name, cancellationToken => session.Dispatch(async () =>
            {
                string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kameo-ui-test-" + Guid.NewGuid().ToString("N"));
                MainWindow? window = null;
                try
                {
                    ProfileStore store = new ProfileStore(root);
                    window = new MainWindow(store, () => { }, () => { });
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    action(window, store);
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

            Add("EmptyStateHasDisabledLaunch", (window, store) => Check(!Button(window, "Launch").IsEnabled && !Button(window, "Launch in...").IsEnabled && Text(window).Contains("No profiles yet", StringComparison.Ordinal), "Empty state is missing or can launch without a profile."));
            Add("LaunchInPreservesDefaultFolder", (_, store) =>
            {
                string chosen = Directory.CreateDirectory(Path.Combine(store.Root, "Project with spaces")).FullName;
                WithLaunchWindow(store, _ => Task.FromResult<string?>(chosen), (window, launches) =>
                {
                    Check(Button(window, "Launch in...").IsEnabled, "Launch in should be available for a selected profile.");
                    Click(Button(window, "Launch in..."));
                    Check(launches.SequenceEqual(new[] { chosen }), "Launch in did not use the chosen directory exactly once.");
                    Check(store.Preference("workspace") == store.Root, "Launch in changed the saved launch folder.");
                    Click(Button(window, "Launch"));
                    Check(launches.SequenceEqual(new[] { chosen, store.Root }), "Normal Launch did not retain its default folder.");
                });
            });
            Add("LaunchInCancelDoesNotLaunch", (_, store) => WithLaunchWindow(store, _ => Task.FromResult<string?>(null), (window, launches) =>
            {
                Click(Button(window, "Launch in..."));
                Check(launches.Count == 0 && store.Preference("workspace") == store.Root, "Cancel launched a session or changed the saved folder.");
            }));
            Add("SidebarFolderStillUpdatesDefault", (_, store) =>
            {
                string chosen = Directory.CreateDirectory(Path.Combine(store.Root, "Saved project")).FullName;
                WithLaunchWindow(store, _ => Task.FromResult<string?>(chosen), (window, launches) =>
                {
                    Click(Button(window, Path.GetFileName(store.Root) + "..."));
                    Check(launches.Count == 0 && store.Preference("workspace") == chosen, "The sidebar folder picker did not save the default without launching.");
                    Click(Button(window, "Launch"));
                    Check(launches.SequenceEqual(new[] { chosen }), "Launch did not use the updated sidebar folder.");
                });
            });
            Add("LaunchInPickerFailureIsReported", (_, store) => WithLaunchWindow(store,
                _ => Task.FromException<string?>(new IOException("Folder picker unavailable.")), (window, launches) =>
            {
                Click(Button(window, "Launch in..."));
                Check(launches.Count == 0 && Text(window).Contains("Folder picker unavailable.", StringComparison.Ordinal), "Picker failure escaped or was not reported.");
                Check(store.Preference("workspace") == store.Root, "Picker failure changed the saved folder.");
            }));
            Add("LaunchInMissingFolderDoesNotLaunch", (_, store) => WithLaunchWindow(store,
                _ => Task.FromResult<string?>(Path.Combine(store.Root, "removed-folder")), (window, launches) =>
            {
                Click(Button(window, "Launch in..."));
                Check(launches.Count == 0 && Text(window).Contains("no longer available", StringComparison.Ordinal), "A missing folder was accepted or lacks a useful error.");
                Check(store.Preference("workspace") == store.Root, "A missing folder changed the saved folder.");
            }));
            Add("LaunchInTerminalFailureIsReported", (_, store) => WithLaunchWindow(store,
                _ => Task.FromResult<string?>(store.Root), (window, launches) =>
            {
                Click(Button(window, "Launch in..."));
                Check(Text(window).Contains("No test terminal available.", StringComparison.Ordinal), "Terminal failure was not reported.");
                Check(store.Preference("workspace") == store.Root, "Terminal failure changed the saved folder.");
            }, failLaunch: true));
            Add("CompactLayoutHasNoUnwantedFooter", (window, store) => Check(window.Width <= 800 && window.Height <= 550 && !Text(window).Contains("Stored on this device", StringComparison.Ordinal) && !window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Quit Kameo"), "The compact layout regressed."));
            Add("NativeMenuBarIsComplete", (window, store) =>
            {
                NativeMenu menu = NativeMenu.GetMenu(window) ?? throw new InvalidOperationException("Native menu missing.");
                Check(menu.Items.OfType<NativeMenuItem>().Select(item => item.Header).SequenceEqual(new[] { "File", "Edit", "Profiles", "View", "Window", "Help" }), "Native menu sections missing.");
            });
            Add("HarnessNavigation", (window, store) =>
            {
                NativeMenu menu = NativeMenu.GetMenu(window)!;
                NativeMenuItem profiles = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Profiles");
                Check(profiles.Menu!.Items.Count == 4, "All harnesses must be in the native menu.");
                Button mux = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is Grid grid && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Mux"));
                Click(mux); Check(Text(window).Contains("kameo mux", StringComparison.Ordinal), "Harness navigation did not update the launch command.");
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
                Check(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Personal") && window.GetVisualDescendants().OfType<Button>().Count(button => button.Content as string == "Edit") == 1, "Search did not filter profile cards.");
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
                Check(store.Selected(HarnessKind.Antigravity).Kind == CredentialKind.ApiKey && Text(window).Contains("kameo antigravity", StringComparison.Ordinal), "Antigravity API profile did not produce the current wrapper command.");
            });
            Add("AntigravityAccountCreationAndCredentialStatus", (window, store) =>
            {
                Navigate(window, "Antigravity"); Click(Button(window, "+  Add profile")); Window editor = window.OwnedWindows.Single();
                Check(Text(editor).Contains("paste the authorization code", StringComparison.Ordinal) && Text(editor).Contains(AntigravityCompatibility.SupportedVersion, StringComparison.Ordinal), "Account sign-in guidance is missing.");
                Field(editor, "ProfileName").Text = "Google Work"; Click(Button(editor, "Save profile")); window.Refresh();
                Profile profile = store.Selected(HarnessKind.Antigravity);
                Check(profile.Kind == CredentialKind.Native && !profile.HasNativeCredential, "A Google account requires an API key or was shown as authenticated before sign-in.");
                Check(Button(window, "Sign in").IsVisible && Button(window, "Launch").IsEnabled && Button(window, "Launch in...").IsEnabled, "Account actions are unavailable.");
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
                MainWindow window = new MainWindow(store, () => { }, () => { }, cancellationToken => Task.FromResult<string?>(store.Root), (id, login, directory) =>
                { Check(id == profile.Id && directory == store.Root, "Account action used the wrong profile or folder."); logins.Add(login); });
                try
                {
                    window.Show(); Navigate(window, "Antigravity"); Click(Button(window, "Sign in")); Click(Button(window, "Launch")); Click(Button(window, "Launch in..."));
                    Check(logins.SequenceEqual(new[] { true, false, false }), "Account actions did not distinguish login from launch.");
                }
                finally { window.Close(); }
            });
            return await ConsoleRunner.RunMainAsync(new[] { new TestSuiteDescriptor("Desktop", "Desktop workflows and native menu contracts", cases) }, args).ConfigureAwait(false);
        }

        private static void WithLaunchWindow(ProfileStore store, Func<CancellationToken, Task<string?>> pickFolder,
            Action<MainWindow, List<string>> verify, bool failLaunch = false)
        {
            Profile profile = store.Create(new Profile { Harness = HarnessKind.Claude, Name = "Launch test", ApiKey = "synthetic" });
            store.SetPreference("workspace", store.Root);
            List<string> launches = new List<string>();
            MainWindow window = new MainWindow(store, () => { }, () => { }, pickFolder, (id, login, directory) =>
            {
                Check(id == profile.Id && !login, "Launch in used the wrong profile or started login instead of a session.");
                if (failLaunch) throw new InvalidOperationException("No test terminal available.");
                launches.Add(directory);
            });
            try { window.Show(); Sync(window); verify(window, launches); }
            finally { window.Close(); }
        }

        private static Button Button(Window window, string label) { Sync(window); return window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == label); }
        private static void Navigate(MainWindow window, string harness) => Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is Grid grid && grid.Children.OfType<TextBlock>().Any(text => text.Text == harness)));
        private static TextBox Field(Window window, string name) { Sync(window); return window.GetVisualDescendants().OfType<TextBox>().Single(field => field.Name == name); }
        private static string Text(Window window) { Sync(window); return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)); }
        private static void Sync(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
