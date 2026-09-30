using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using STAGE.Tools;
using STAGE.Utils;

namespace STAGE
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        private readonly List<string> _managedModFolders;
        private Storyboard? _busyStoryboard;

        public SettingsDialog()
        {
            this.InitializeComponent();

            _managedModFolders = Settings.ManagedModFolders.ToList();
            string activeModFolder = Settings.ActiveModFolder;
            if (!string.IsNullOrWhiteSpace(activeModFolder)
                && !_managedModFolders.Contains(activeModFolder, StringComparer.OrdinalIgnoreCase))
            {
                _managedModFolders.Add(activeModFolder);
            }
            RefreshManagedModFolderList(activeModFolder);
            BaselineScdTextBox.Text = Settings.BaselineScdKey;
            BackgroundImageTextBox.Text = Settings.BackgroundImagePath;
            DefaultScdTemplateTextBox.Text = Settings.DefaultScdTemplateSourcePath;
            DefaultDonorPapTextBox.Text = Settings.DefaultDonorPapPath;
            string busyImagePath = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "stage.png");
            if (File.Exists(busyImagePath))
                SettingsBusyImage.Source = new BitmapImage(new Uri(busyImagePath));
            ScdAudioVolumeBox.Value = Settings.ScdAudioVolume;
            NormalizeVolumeCheckBox.IsChecked = Settings.NormalizeVolume;
            AutoReloadCheckBox.IsChecked = Settings.AutoReloadMod;
            AutoUpdateDependenciesCheckBox.IsChecked = Settings.AutoUpdateDependencies;
            AutoBackupRetentionDaysBox.Value = Settings.AutoBackupRetentionDays;
            foreach (string presetName in EqualizerSettings.PresetNames)
                ConvertAllEqPresetComboBox.Items.Add(presetName);
            ConvertAllEqPresetComboBox.SelectedIndex = (int)Settings.EqualizerPreset;
            ValidateFields();
        }

        private void BeginBusy(string message)
        {
            SettingsBusyLabel.Text = message;
            SettingsBusyOverlay.Visibility = Visibility.Visible;
            SettingsTabView.IsHitTestVisible = false;
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;

            if (_busyStoryboard == null)
            {
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new Duration(TimeSpan.FromSeconds(1.1)),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(animation, SettingsBusyRotate);
                Storyboard.SetTargetProperty(animation, "Angle");
                _busyStoryboard = new Storyboard();
                _busyStoryboard.Children.Add(animation);
            }

            SettingsBusyRotate.Angle = 0;
            _busyStoryboard.Begin();
        }

        private void EndBusy()
        {
            _busyStoryboard?.Stop();
            SettingsBusyRotate.Angle = 0;
            SettingsBusyProgressBar.IsIndeterminate = true;
            SettingsBusyLabel.Text = string.Empty;
            SettingsBusyOverlay.Visibility = Visibility.Collapsed;
            SettingsTabView.IsHitTestVisible = true;
            IsSecondaryButtonEnabled = true;
            ValidateFields();
        }

        private void RefreshManagedModFolderList(string? selectedPath = null)
        {
            ManagedModFoldersListBox.Items.Clear();
            foreach (string path in _managedModFolders.OrderBy(
                path => path, StringComparer.OrdinalIgnoreCase))
            {
                ManagedModFoldersListBox.Items.Add(path);
            }

            string? selection = _managedModFolders.FirstOrDefault(path =>
                string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (selection == null && ManagedModFoldersListBox.Items.Count > 0)
                selection = ManagedModFoldersListBox.Items[0] as string;

            ManagedModFoldersListBox.SelectedItem = selection;
            DirectoryPathTextBox.Text = selection ?? string.Empty;
            ValidateFields();
        }

        private void ValidateFields()
        {
            bool validDirectory = !string.IsNullOrEmpty(DirectoryPathTextBox.Text)
                && Directory.Exists(DirectoryPathTextBox.Text)
                && File.Exists(Path.Combine(DirectoryPathTextBox.Text, "meta.json"));
            bool validScd = !string.IsNullOrWhiteSpace(BaselineScdTextBox.Text)
                && BaselineScdTextBox.Text.Trim().EndsWith(".scd", StringComparison.OrdinalIgnoreCase);
            IsPrimaryButtonEnabled = validDirectory && validScd;
        }

        private async void AddManagedModFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
                return;

            string path = Path.GetFullPath(folder.Path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!File.Exists(Path.Combine(path, "meta.json")))
            {
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    "The selected folder does not contain meta.json. Select the root folder of a Penumbra mod.",
                    "Invalid Mod Folder",
                    0x00000010);
                return;
            }

            if (!_managedModFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                _managedModFolders.Add(path);
            RefreshManagedModFolderList(path);
        }

        private void RemoveManagedModFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (ManagedModFoldersListBox.SelectedItem is not string selectedPath)
                return;

            _managedModFolders.RemoveAll(path =>
                string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase));
            RefreshManagedModFolderList();
        }

        private void ManagedModFoldersListBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            DirectoryPathTextBox.Text =
                ManagedModFoldersListBox.SelectedItem as string ?? string.Empty;
            ValidateFields();
        }

        private async void BrowseBaselineScdButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".scd");
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            string selectedPath = file.Path;
            string baselineKey = Path.GetFileName(selectedPath);

            if (Directory.Exists(DirectoryPathTextBox.Text))
            {
                try
                {
                    string rel = Path.GetRelativePath(DirectoryPathTextBox.Text, selectedPath);
                    if (!rel.StartsWith(".."))
                        baselineKey = rel.Replace(Path.DirectorySeparatorChar, '/');
                }
                catch
                {
                    baselineKey = Path.GetFileName(selectedPath);
                }
            }

            BaselineScdTextBox.Text = baselineKey;
        }

        private async void BrowseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                string storedPath = UserAssetStore.Preserve(
                    file.Path,
                    "background",
                    "background image",
                    ".png", ".jpg", ".jpeg", ".bmp", ".gif");
                BackgroundImageTextBox.Text = storedPath;
                Settings.BackgroundImagePath = storedPath;
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    $"The background was copied into STAGE and applied:\n{storedPath}",
                    "Background Applied",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    ex.Message,
                    "Background Migration Failed",
                    0x00000010);
            }
        }

        private async void BrowseDefaultScdTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".scd");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                string storedPath = UserAssetStore.Preserve(
                    file.Path,
                    "donor",
                    "donor SCD",
                    ".scd");
                DefaultScdTemplateTextBox.Text = storedPath;
                Settings.DefaultScdTemplateSourcePath = storedPath;
                File.Copy(
                    storedPath,
                    Path.Combine(Directory.GetCurrentDirectory(), "default.scd"),
                    overwrite: true);
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    $"The donor SCD was copied into STAGE and is now the conversion basis:\n{storedPath}",
                    "Donor SCD Applied",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    ex.Message,
                    "Donor SCD Migration Failed",
                    0x00000010);
            }
        }

        private async void BrowseDefaultDonorPapButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".pap");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                string storedPath = UserAssetStore.Preserve(
                    file.Path,
                    "donor",
                    "donor PAP",
                    ".pap");
                DefaultDonorPapTextBox.Text = storedPath;
                Settings.DefaultDonorPapPath = storedPath;
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    $"The donor PAP was copied into STAGE and is now the animation basis:\n{storedPath}",
                    "Donor PAP Applied",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    ex.Message,
                    "Donor PAP Migration Failed",
                    0x00000010);
            }
        }

        private async void ApplyDonorLayoutToAllPapsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            string modDirectory = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
            string donorPapPath = DefaultDonorPapTextBox.Text.Trim();

            if (!File.Exists(donorPapPath))
            {
                MessageBox(
                    hwnd,
                    $"Select a valid donor PAP under Settings → Animation first.\n\n{donorPapPath}",
                    "Donor PAP Not Found",
                    0x00000010);
                return;
            }

            int papCount;
            try
            {
                papCount = Directory.EnumerateFiles(
                    modDirectory,
                    "*.pap",
                    SearchOption.AllDirectories).Count();
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Could Not Scan PAP Files", 0x00000010);
                return;
            }

            int confirm = MessageBox(
                hwnd,
                $"Apply the donor TMB layout to {papCount} PAP file(s) in the active mod?\n\n" +
                $"Donor:\n{donorPapPath}\n\n" +
                "A full external backup will be created first. Each target's animation-only C009 " +
                "and expression C010 tracks will be preserved. Its other TMB tracks will be replaced " +
                "by the donor layout; donor expressions and sound-reference tracks will not be copied.",
                "Apply Donor TMB Layout",
                0x00000001 | 0x00000030);
            if (confirm != 1)
                return;

            ApplyDonorLayoutToAllPapsButton.IsEnabled = false;
            BeginBusy($"Applying donor TMB layout to {papCount} PAP file(s)...");
            try
            {
                PapTmbLayoutBatchResult result = await Task.Run(() =>
                    PapTmbLayoutBatchService.Apply(modDirectory, donorPapPath));

                string errorSummary = result.Errors.Count == 0
                    ? string.Empty
                    : $"\n\nSkipped/failed:\n{string.Join("\n", result.Errors.Take(12))}" +
                      (result.Errors.Count > 12
                          ? $"\n…and {result.Errors.Count - 12} more."
                          : string.Empty);

                MessageBox(
                    hwnd,
                    $"Updated {result.PapFilesUpdated} of {result.PapFilesFound} PAP file(s)." +
                    $"\n\nSafety backup:\n{result.BackupPath}" +
                    errorSummary,
                    result.Errors.Count == 0
                        ? "Donor TMB Layout Applied"
                        : "Donor TMB Layout Applied With Warnings",
                    result.Errors.Count == 0 ? 0x00000040u : 0x00000030u);

                PenumbraApi.ScheduleReloadModFolder(modDirectory);
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Donor TMB Layout Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
                ApplyDonorLayoutToAllPapsButton.IsEnabled = true;
            }
        }

        private void DirectoryPathTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateFields();

        private void BaselineScdTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateFields();

        private void OkButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            try
            {
                string backgroundPath = UserAssetStore.Preserve(
                    BackgroundImageTextBox.Text.Trim(),
                    "background",
                    "background image",
                    ".png", ".jpg", ".jpeg", ".bmp", ".gif");
                string donorScdPath = UserAssetStore.Preserve(
                    DefaultScdTemplateTextBox.Text.Trim(),
                    "donor",
                    "donor SCD",
                    ".scd");
                string donorPapPath = UserAssetStore.Preserve(
                    DefaultDonorPapTextBox.Text.Trim(),
                    "donor",
                    "donor PAP",
                    ".pap");

                string path = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
                Settings.ManagedModFolders = _managedModFolders;
                Settings.SetActiveModFolder(path);
                Settings.BaselineScdKey = BaselineScdTextBox.Text;
                Settings.BackgroundImagePath = backgroundPath;
                Settings.DefaultScdTemplateSourcePath = donorScdPath;
                Settings.DefaultDonorPapPath = donorPapPath;
                Settings.ScdAudioVolume = (float)ScdAudioVolumeBox.Value;
                Settings.NormalizeVolume = NormalizeVolumeCheckBox.IsChecked == true;
                Settings.AutoReloadMod = AutoReloadCheckBox.IsChecked == true;
                Settings.AutoUpdateDependencies = AutoUpdateDependenciesCheckBox.IsChecked == true;
                Settings.AutoBackupRetentionDays = (int)AutoBackupRetentionDaysBox.Value;

                if (!string.IsNullOrWhiteSpace(donorScdPath))
                {
                    string targetDefaultScd = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "default.scd");
                    File.Copy(donorScdPath, targetDefaultScd, overwrite: true);
                }

                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    "Settings were applied.\n\n" +
                    $"Background: {(string.IsNullOrWhiteSpace(backgroundPath) ? "Default theme" : backgroundPath)}\n" +
                    $"Donor SCD basis: {(string.IsNullOrWhiteSpace(donorScdPath) ? "Not set" : donorScdPath)}\n" +
                    $"Donor PAP basis: {(string.IsNullOrWhiteSpace(donorPapPath) ? "Not set" : donorPapPath)}",
                    "Settings Applied",
                    0x00000040);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                MessageBox(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    ex.Message,
                    "Could Not Preserve Settings File",
                    0x00000010);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private void OrganizeLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            int result = MessageBox(hwnd,
                AppStrings.Dlg_OrganizeLibrary_Content,
                AppStrings.Dlg_OrganizeLibrary_Title,
                0x00000001 | 0x00000030); // MB_OKCANCEL | MB_ICONWARNING
            if (result != 1) // IDOK
                return;

            foreach (var playlist in MainWindow.Playlists.Values)
                playlist.Cleanup();
        }

        private async void CleanupMissingReferencesButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            int confirm = MessageBox(hwnd,
                "Remove missing audio tracks, PAP mappings, and VFX mappings from the managed mod JSON?\n\n" +
                "This does not delete existing files. It only purges references whose target files are already missing.",
                "Cleanup Missing Items",
                0x00000001 | 0x00000030); // MB_OKCANCEL | MB_ICONWARNING
            if (confirm != 1)
                return;

            BeginBusy("Scanning and cleaning missing mod references...");
            try
            {
                var result = await Task.Run(ModCleanupService.PurgeMissingReferences);
                MessageBox(hwnd,
                    $"Scanned JSON files: {result.FilesScanned}\n" +
                    $"Updated JSON files: {result.JsonFilesUpdated}\n" +
                    $"Removed audio tracks: {result.RemovedAudioTracks}\n" +
                    $"Removed VFX/PAP mappings: {result.RemovedVfxPapMappings}\n" +
                    $"Removed empty VFX/PAP options: {result.RemovedVfxPapOptions}" +
                    (result.Errors.Count == 0 ? "" : $"\n\nErrors:\n{string.Join("\n", result.Errors)}"),
                    result.Errors.Count == 0 ? "Cleanup Complete" : "Cleanup Complete With Errors",
                    result.Errors.Count == 0 ? 0x00000040u : 0x00000030u);
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Cleanup Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
            }
        }

        private async void SynchronizeAssetFilenamesButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            string modDirectory = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
            int confirm = MessageBox(
                hwnd,
                "Rename mapped PAP and AVFX files to match their option names and update every JSON reference?\n\n" +
                "Shared files are kept shared when names agree and copied when their option names differ. " +
                "A rollback backup and manifest will be created inside the mod.",
                "Synchronize PAP / VFX Filenames",
                0x00000001 | 0x00000030);
            if (confirm != 1)
                return;

            SynchronizeAssetFilenamesButton.IsEnabled = false;
            BeginBusy("Synchronizing PAP and VFX filenames...");
            try
            {
                var result = await Task.Run(() =>
                    ModAssetFilenameSynchronizer.Synchronize(modDirectory));
                MessageBox(
                    hwnd,
                    $"Created/renamed files: {result.FilesCreated}\n" +
                    $"Removed unreferenced old files: {result.OldFilesRemoved}\n" +
                    $"Updated mappings: {result.MappingsUpdated}\n" +
                    $"Missing mappings skipped: {result.MissingMappingsSkipped}\n" +
                    (string.IsNullOrWhiteSpace(result.BackupDirectory)
                        ? "\nEverything was already synchronized."
                        : $"\nRollback backup:\n{result.BackupDirectory}"),
                    "Filename Synchronization Complete",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Filename Synchronization Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
                SynchronizeAssetFilenamesButton.IsEnabled = true;
            }
        }

        private async void BackupModButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            BackupModButton.IsEnabled = false;
            BeginBusy("Creating a full backup of the active mod...");
            try
            {
                string modDirectory = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
                var result = await Task.Run(() =>
                    ModBackupService.CreateBackup(modDirectory));
                MessageBox(
                    hwnd,
                    $"Backed up {result.FileCount} files ({FormatBytes(result.TotalBytes)}).\n\n" +
                    result.BackupPath,
                    "Mod Backup Complete",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Mod Backup Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
                BackupModButton.IsEnabled = true;
            }
        }

        private async void CleanupExpiredAutoBackupsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            int retentionDays = (int)Math.Clamp(
                AutoBackupRetentionDaysBox.Value,
                1,
                3650);
            Settings.AutoBackupRetentionDays = retentionDays;
            CleanupExpiredAutoBackupsButton.IsEnabled = false;
            BeginBusy($"Removing automatic backups older than {retentionDays} day(s)...");
            try
            {
                string activeMod = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
                var modDirectories = _managedModFolders
                    .Append(activeMod)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                BackupRetentionResult result = await Task.Run(() =>
                    ModBackupService.CleanupExpiredAutomaticBackups(
                        modDirectories,
                        retentionDays));
                MessageBox(
                    hwnd,
                    result.AutomaticBackupsDeleted == 0
                        ? $"No automatic backups were older than {retentionDays} day(s).\n\n" +
                          "Manual backups were not examined for deletion."
                        : $"Deleted {result.AutomaticBackupsDeleted} expired automatic backup(s).\n" +
                          $"Freed {FormatBytes(result.BytesFreed)}.\n\n" +
                          "Manual backups were left untouched.",
                    "Automatic Backup Cleanup Complete",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(
                    hwnd,
                    ex.Message,
                    "Automatic Backup Cleanup Failed",
                    0x00000010);
            }
            finally
            {
                EndBusy();
                CleanupExpiredAutoBackupsButton.IsEnabled = true;
            }
        }

        private async void RestoreModButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".zip");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            int confirm = MessageBox(
                hwnd,
                $"Restore the active mod from this backup?\n\n{file.Path}\n\n" +
                "The current mod will be backed up automatically before restoration.",
                "Restore Mod Backup",
                0x00000001 | 0x00000030);
            if (confirm != 1)
                return;

            RestoreModButton.IsEnabled = false;
            BackupModButton.IsEnabled = false;
            BeginBusy("Creating a safety backup and restoring the selected mod backup...");
            try
            {
                string modDirectory = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
                var result = await Task.Run(() =>
                    ModBackupService.RestoreBackup(modDirectory, file.Path));
                MessageBox(
                    hwnd,
                    $"Restored {result.FileCount} files.\n\n" +
                    $"Pre-restore safety backup:\n{result.SafetyBackupPath}",
                    "Mod Restore Complete",
                    0x00000040);
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, ex.Message, "Mod Restore Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
                RestoreModButton.IsEnabled = true;
                BackupModButton.IsEnabled = true;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }


        private async void ConvertAllToDefaultScdButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            int total = 0;
            foreach (var playlist in MainWindow.Playlists.Values)
                foreach (var option in playlist.Options)
                    if (!string.IsNullOrEmpty(Playlist.GetScdPath(option)))
                        total++;

            if (total == 0)
            {
                MessageBox(hwnd, AppStrings.Dlg_NoSongs, "Convert All Tracks", 0x00000040);
                return;
            }

            int result = MessageBox(hwnd,
                AppStrings.RepackageScdFromDefaultConfirm(total),
                "Convert All Tracks",
                0x00000001 | 0x00000030); // MB_OKCANCEL | MB_ICONWARNING
            if (result != 1)
                return;

            string templatePath = DefaultScdTemplateTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                if (!File.Exists(templatePath))
                {
                    MessageBox(hwnd, $"Default SCD template not found:\n{templatePath}", "Convert All Tracks", 0x00000010);
                    return;
                }

                string targetDefaultScd = Path.Combine(Directory.GetCurrentDirectory(), "default.scd");
                File.Copy(templatePath, targetDefaultScd, overwrite: true);
                Settings.DefaultScdTemplateSourcePath = templatePath;
            }

            string currentDefaultScd = Path.Combine(Directory.GetCurrentDirectory(), "default.scd");
            if (!File.Exists(currentDefaultScd))
            {
                MessageBox(hwnd, $"Current default.scd not found:\n{currentDefaultScd}", "Convert All Tracks", 0x00000010);
                return;
            }

            ConvertAllToDefaultScdButton.IsEnabled = false;
            BeginBusy($"Converting {total} track(s) using the current default SCD...");
            try
            {
                var (updated, processed, errors) = await App.MainWindow.ConvertAllTracksToCurrentDefaultScdAsync();
                string content = errors.Count == 0
                    ? AppStrings.Processed(updated, processed)
                    : AppStrings.Processed(updated, processed) +
                      AppStrings.ProcessedErrors(string.Join("\n", errors.GetRange(0, Math.Min(errors.Count, 10))) +
                      (errors.Count > 10 ? "\n" + AppStrings.AndMore(errors.Count - 10) : ""));
                MessageBox(hwnd, content, AppStrings.Summary_RepackageScdFromDefault, errors.Count == 0 ? 0x00000040u : 0x00000030u);
            }
            finally
            {
                EndBusy();
                ConvertAllToDefaultScdButton.IsEnabled = true;
            }
        }

        private async void ConvertAllToEqPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            int total = MainWindow.Playlists.Values
                .SelectMany(playlist => playlist.Options)
                .Count(option => !string.IsNullOrEmpty(Playlist.GetScdPath(option)));

            if (total == 0)
            {
                MessageBox(hwnd, AppStrings.Dlg_NoSongs, "Convert All Tracks to EQ Preset", 0x00000040);
                return;
            }

            int presetIndex = ConvertAllEqPresetComboBox.SelectedIndex;
            if (presetIndex < 0 || presetIndex >= EqualizerSettings.PresetNames.Length)
                presetIndex = (int)EqualizerPreset.Neutral;

            string presetName = EqualizerSettings.PresetNames[presetIndex];
            float volumeLevel = (float)ScdAudioVolumeBox.Value;
            int result = MessageBox(
                hwnd,
                $"Convert all {total} track(s) to the '{presetName}' EQ preset at volume {volumeLevel:0.0}?",
                "Convert All Tracks to EQ Preset",
                0x00000001 | 0x00000030);
            if (result != 1)
                return;

            var settings = new EqualizerSettings { VolumeLevel = volumeLevel };
            settings.ApplyPreset((EqualizerPreset)presetIndex);
            Settings.EqualizerPreset = (EqualizerPreset)presetIndex;
            Settings.ScdAudioVolume = volumeLevel;

            ConvertAllToEqPresetButton.IsEnabled = false;
            BeginBusy($"Applying the {presetName} EQ preset to {total} track(s)...");
            try
            {
                var (updated, processed, errors) =
                    await App.MainWindow.ConvertAllTracksToEqualizerPresetAsync(settings);
                string content = errors.Count == 0
                    ? AppStrings.Processed(updated, processed)
                    : AppStrings.Processed(updated, processed) +
                      AppStrings.ProcessedErrors(string.Join("\n", errors.GetRange(
                          0, Math.Min(errors.Count, 10))) +
                      (errors.Count > 10 ? "\n" + AppStrings.AndMore(errors.Count - 10) : ""));
                MessageBox(
                    hwnd, content, AppStrings.Summary_ApplyEQ,
                    errors.Count == 0 ? 0x00000040u : 0x00000030u);
            }
            finally
            {
                EndBusy();
                ConvertAllToEqPresetButton.IsEnabled = true;
            }
        }


        private async void UpdateDependenciesButton_Click(object sender, RoutedEventArgs e)
        {
            BeginBusy("Checking and updating required components...");
            try
            {
                IsPrimaryButtonEnabled = false;
                await DependencyUpdateService.EnsureDependenciesUpToDateAsync();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                MessageBox(hwnd, $"Dependencies are up to date.\n{DependencyUpdateService.GetFfmpegVersion()}", "Dependency Update", 0x00000040);
            }
            catch (Exception ex)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                MessageBox(hwnd, ex.Message, "Dependency Update Failed", 0x00000010);
            }
            finally
            {
                EndBusy();
            }
        }

    }
}
