namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Platform.Storage;
    using Kameo.Core;

    internal sealed class ProfileEditor : Window
    {
        private readonly ProfileStore _Store;
        private readonly HarnessKind _Harness;
        private readonly Profile? _Existing;
        private readonly TextBox _Name = new TextBox { Name = "ProfileName", PlaceholderText = "e.g. Work, Personal, Acme client" };
        private readonly TextBox _Key = new TextBox { Name = "ApiKey", PlaceholderText = "Paste your API key", PasswordChar = '●' };
        private readonly TextBox _Executable = new TextBox { PlaceholderText = "Use the installed command on PATH" };
        private readonly ComboBox _Kind = new ComboBox { Name = "AuthenticationKind", ItemsSource = new[] { "API key", "Native login / import" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _Adapter = new ComboBox { ItemsSource = new[] { "openai", "openai-compatible", "anthropic", "gemini", "xai" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _Endpoint = new TextBox { Text = "https://api.openai.com/v1" };
        private readonly TextBox _Model = new TextBox { PlaceholderText = "Model identifier from your provider" };
        private readonly TextBox _MuxEndpoints = new TextBox { Name = "MuxEndpointsFile", PlaceholderText = "Choose a shared endpoints JSON file" };
        private readonly TextBlock _Error = Ui.Text("", 12, Palette.Danger);

        internal ProfileEditor(ProfileStore store, HarnessKind harness, string? id)
        {
            _Store = store; _Harness = harness;
            if (harness == HarnessKind.Mux) _Kind.ItemsSource = new[] { "API key", "Endpoints file / setup" };
            _Existing = id == null ? null : store.Get(id);
            if (harness == HarnessKind.Antigravity)
            {
                _Kind.ItemsSource = new[] { "API key", "Google account" };
                _Kind.SelectedIndex = 1;
            }
            Title = id == null ? "Add profile" : "Edit profile";
            Width = 460; Height = 570; MinHeight = 420; MinWidth = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.Background;
            NativeMenu menu = new NativeMenu();
            menu.Add(NativeMenus.Submenu("File", NativeMenus.Item("Close", Close, Avalonia.Input.Key.W)));
            menu.Add(NativeMenus.Edit(this));
            NativeMenu.SetMenu(this, menu);
            if (_Existing != null)
            {
                _Name.Text = _Existing.Name;
                _Kind.SelectedIndex = _Existing.Kind == CredentialKind.ApiKey ? 0 : 1;
                _Kind.IsEnabled = false;
                _Key.PlaceholderText = "Leave blank to keep the saved key";
                _Executable.Text = _Existing.Executable;
                _MuxEndpoints.Text = _Existing.MuxEndpointsFile;
                string endpoints = Path.Combine(store.ProfileDirectory(_Existing.Id), "endpoints.json");
                if (harness == HarnessKind.Mux && _Existing.Kind == CredentialKind.ApiKey && File.Exists(endpoints))
                {
                    JsonNode? endpoint = JsonNode.Parse(PrivateFiles.Read(endpoints))?["endpoints"]?[0];
                    _Adapter.SelectedItem = endpoint?["adapterType"]?.GetValue<string>() ?? "openai";
                    _Endpoint.Text = endpoint?["baseUrl"]?.GetValue<string>() ?? "";
                    _Model.Text = endpoint?["model"]?.GetValue<string>() ?? "";
                }
            }
            CheckBox reveal = new CheckBox { Content = "Show key while editing", Foreground = Palette.Muted, FontSize = 12 };
            reveal.IsCheckedChanged += (_, _) => _Key.PasswordChar = reveal.IsChecked == true ? '\0' : '●';
            Control keyFields = Ui.Stack(Ui.Field("API key", _Key), reveal);
            Control muxFields = Ui.Stack(Ui.Field("Provider adapter", _Adapter), Ui.Field("API endpoint", _Endpoint), Ui.Field("Model", _Model));
            Control muxFile = Ui.Stack(Ui.Field("Shared endpoints file (optional)", _MuxEndpoints),
                Ui.Button("Browse endpoints...", () => _ = ChooseMuxEndpointsAsync(CancellationToken.None)),
                Ui.Text("Multiple profiles can use the same file. Changes apply on the next launch. Kameo reads this file without modifying it. Leave blank to use Setup or an imported copy.", 12, Palette.Muted));
            TextBlock nativeHint = Ui.Text(harness == HarnessKind.Antigravity
                ? "Requires Antigravity CLI " + AntigravityCompatibility.SupportedVersion + " or later. Save, then click Sign in. Open the URL shown in the terminal, sign in with this profile's Google account, and paste the authorization code back into that terminal. Each profile keeps its own login."
                : harness == HarnessKind.Mux ? "Choose an endpoints file below, or save and use Setup to configure this profile." : "Save the profile, then sign in through the harness's own login flow or import an existing login. Kameo keeps its authentication files separate.", 13, Palette.Muted);
            void UpdateFields()
            {
                keyFields.IsVisible = _Kind.SelectedIndex == 0;
                muxFields.IsVisible = harness == HarnessKind.Mux && _Kind.SelectedIndex == 0;
                nativeHint.IsVisible = _Kind.SelectedIndex == 1;
                muxFile.IsVisible = harness == HarnessKind.Mux && _Kind.SelectedIndex == 1;
            }
            _Kind.SelectionChanged += (_, _) => UpdateFields();
            UpdateFields();
            StackPanel fields = Ui.Stack(Ui.Text(HarnessCatalog.Name(harness).ToUpperInvariant(), 11, Palette.Accent, FontWeight.SemiBold),
                Ui.Text(Title!, 23, weight: FontWeight.SemiBold),
                Ui.Field("Profile name", _Name), Ui.Field("Authentication", _Kind), keyFields, nativeHint, muxFields, muxFile,
                Ui.Field("Executable (optional)", _Executable, "A command name or absolute path. On Windows, npm shims are supported."),
                Ui.Text("Credentials are stored locally without Kameo encryption.", 12, Palette.Muted));
            fields.Spacing = 12;
            Grid layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
            layout.Children.Add(new ScrollViewer { Content = fields, Padding = new Thickness(0, 0, 12, 18) });
            StackPanel footer = Ui.Stack(_Error, Ui.Row(Ui.Button("Cancel", () => Close(false)), Ui.Button("Save profile", Save, true)));
            Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        }

        private void Save()
        {
            string? createdId = null;
            try
            {
                Profile profile = _Existing ?? new Profile { Harness = _Harness, Kind = _Kind.SelectedIndex == 0 ? CredentialKind.ApiKey : CredentialKind.Native };
                profile.Name = _Name.Text ?? "";
                profile.Executable = (_Executable.Text ?? "").Trim();
                profile.MuxEndpointsFile = _Harness == HarnessKind.Mux && profile.Kind == CredentialKind.Native ? (_MuxEndpoints.Text ?? "").Trim() : "";
                if (!string.IsNullOrEmpty(_Key.Text)) profile.ApiKey = _Key.Text;
                if (_Existing == null) { profile = _Store.Create(profile); createdId = profile.Id; }
                else _Store.Update(profile);
                if (_Harness == HarnessKind.Mux && profile.Kind == CredentialKind.ApiKey)
                    new CredentialFiles(_Store).ConfigureMux(profile.Id, _Adapter.SelectedItem as string ?? "openai", _Endpoint.Text ?? "", _Model.Text ?? "");
                _Key.Text = "";
                Close(true);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
            {
                if (createdId != null) _Store.Delete(createdId);
                _Error.Text = error is Microsoft.Data.Sqlite.SqliteException ? "Unable to save the profile. Check access to the data directory." : error.Message;
            }
        }

        private async Task ChooseMuxEndpointsAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = string.IsNullOrWhiteSpace(_MuxEndpoints.Text)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mux")
                    : Path.GetDirectoryName(Path.GetFullPath(_MuxEndpoints.Text))!;
                using IStorageFolder? start = await StorageProvider.TryGetFolderFromPathAsync(directory);
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Choose shared Mux endpoints file", AllowMultiple = false, SuggestedStartLocation = start,
                    FileTypeFilter = new[] { new FilePickerFileType("JSON endpoints") { Patterns = new[] { "*.json" } } } });
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (files.Count > 0) _MuxEndpoints.Text = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local endpoints file.");
                }
                finally { foreach (IStorageFile file in files) file.Dispose(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { _Error.Text = error.Message; }
        }
    }
}
