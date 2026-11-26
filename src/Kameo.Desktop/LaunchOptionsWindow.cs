namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Platform.Storage;
    using Kameo.Core;

    /// <summary>Asks where to launch a profile and with which permission level. <see cref="Result"/> is set only by Launch.</summary>
    internal sealed class LaunchOptionsWindow : Window
    {
        private readonly Func<CancellationToken, Task<string?>>? _PickFolder;
        private readonly IReadOnlyList<LaunchPermission> _Levels;
        private readonly TextBox _Folder = new TextBox { Name = "LaunchFolder", PlaceholderText = "Folder to start the session in" };
        private readonly CheckBox _SaveFolder = new CheckBox { Name = "SaveFolder", Content = "Make this my default launch folder", Foreground = Palette.Muted, FontSize = 12 };
        private readonly ComboBox _Permission = new ComboBox { Name = "PermissionMode", HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _Description = Ui.Text("", 12, Palette.Muted);
        private readonly TextBlock _Warning = Ui.Text("", 12, Palette.Danger, FontWeight.SemiBold);
        private readonly TextBlock _Error = Ui.Text("", 12, Palette.Danger);

        /// <summary>Creates the dialog for a profile.</summary>
        /// <param name="profile">The profile to launch.</param>
        /// <param name="folder">The initially proposed folder, normally the saved launch folder.</param>
        /// <param name="permissionId">The initially selected permission level; unknown values select the harness default.</param>
        /// <param name="pickFolder">A folder picker substitute, or null to use this window's own picker.</param>
        internal LaunchOptionsWindow(Profile profile, string folder, string? permissionId, Func<CancellationToken, Task<string?>>? pickFolder)
        {
            ArgumentNullException.ThrowIfNull(profile);
            _PickFolder = pickFolder;
            _Levels = LaunchPermissions.For(profile.Harness);
            Title = "Launch " + profile.Name;
            Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.Background;
            NativeMenu menu = new NativeMenu();
            menu.Add(NativeMenus.Submenu("File", NativeMenus.Item("Close", Close, Avalonia.Input.Key.W)));
            menu.Add(NativeMenus.Edit(this));
            NativeMenu.SetMenu(this, menu);

            _Folder.Text = folder;
            _Permission.ItemsSource = _Levels.Select(level => level.Label).ToArray();
            LaunchPermission initial = LaunchPermissions.Find(profile.Harness, permissionId);
            _Permission.SelectedIndex = _Levels.ToList().IndexOf(initial);
            _Permission.SelectionChanged += (_, _) => ShowPermission();
            ShowPermission();

            Grid folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            folderRow.Children.Add(_Folder);
            Button browse = Ui.Button("Browse...", () => _ = BrowseAsync(CancellationToken.None));
            browse.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);

            Button cancel = Ui.Button("Cancel", Close);
            cancel.IsCancel = true;
            Button launch = Ui.Button("Launch", Launch, true);
            launch.IsDefault = true;
            StackPanel content = Ui.Stack(
                Ui.Text(HarnessCatalog.Name(profile.Harness).ToUpperInvariant(), 11, Palette.Accent, FontWeight.SemiBold),
                Ui.Text("Launch " + profile.Name, 23, weight: FontWeight.SemiBold),
                Ui.Field("Folder", folderRow), _SaveFolder,
                Ui.Field("Permissions", _Permission), _Description, _Warning,
                _Error, Ui.Row(cancel, launch));
            content.Spacing = 12;
            content.Margin = new Thickness(24);
            Content = content;
        }

        /// <summary>The chosen options after Launch, or null when the dialog was canceled or closed.</summary>
        internal LaunchOptions? Result { get; private set; }

        private void ShowPermission()
        {
            LaunchPermission level = _Levels[Math.Max(0, _Permission.SelectedIndex)];
            _Description.Text = level.Description;
            _Warning.Text = level.IsDangerous ? "The agent can change or delete files and run any command without asking. Use only in a sandbox or a project you can restore." : "";
            _Warning.IsVisible = level.IsDangerous;
        }

        private void Launch()
        {
            string folder = (_Folder.Text ?? "").Trim();
            if (folder.Length == 0) { _Error.Text = "Choose a folder to launch in."; return; }
            if (!Directory.Exists(folder)) { _Error.Text = "That folder does not exist. Choose another folder."; return; }
            Result = new LaunchOptions
            {
                WorkingDirectory = Path.GetFullPath(folder), SaveFolder = _SaveFolder.IsChecked == true,
                PermissionId = _Levels[Math.Max(0, _Permission.SelectedIndex)].Id
            };
            Close();
        }

        private async Task BrowseAsync(CancellationToken cancellationToken)
        {
            try
            {
                string? chosen = _PickFolder != null ? await _PickFolder(cancellationToken) : await PickAsync(cancellationToken);
                if (chosen != null) { _Folder.Text = chosen; _Error.Text = ""; }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { _Error.Text = error.Message; }
        }

        private async Task<string?> PickAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = (_Folder.Text ?? "").Trim();
            using IStorageFolder? start = Directory.Exists(current) ? await StorageProvider.TryGetFolderFromPathAsync(current) : null;
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose a launch folder", AllowMultiple = false, SuggestedStartLocation = start });
            try { return folders.Count == 0 ? null : folders[0].TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local launch folder."); }
            finally { foreach (IStorageFolder chosen in folders) chosen.Dispose(); }
        }
    }
}
