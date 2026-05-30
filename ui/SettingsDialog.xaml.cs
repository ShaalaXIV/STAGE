using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Pickles_Playlist_Editor.Tools;

namespace Pickles_Playlist_Editor
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsDialog()
        {
            this.InitializeComponent();

            DirectoryPathTextBox.Text = Path.Combine(
                Settings.PenumbraLocation ?? string.Empty,
                Settings.ModName ?? string.Empty);
            BaselineScdTextBox.Text = Settings.BaselineScdKey;
            BackgroundImageTextBox.Text = Settings.BackgroundImagePath;
            DefaultScdTemplateTextBox.Text = Settings.DefaultScdTemplateSourcePath;
            NormalizeVolumeCheckBox.IsChecked = Settings.NormalizeVolume;
            AutoReloadCheckBox.IsChecked = Settings.AutoReloadMod;
            AutoUpdateDependenciesCheckBox.IsChecked = Settings.AutoUpdateDependencies;
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

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                DirectoryPathTextBox.Text = folder.Path;
                ValidateFields();
            }
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
            if (file != null)
                BackgroundImageTextBox.Text = file.Path;
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
            if (file != null)
                DefaultScdTemplateTextBox.Text = file.Path;
        }

        private void DirectoryPathTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateFields();

        private void BaselineScdTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateFields();

        private void OkButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string path = DirectoryPathTextBox.Text.TrimEnd('\\', '/');
            string modName = Path.GetFileName(path);
            string penLocation = path.Length > modName.Length
                ? path[..^modName.Length]
                : path + Path.DirectorySeparatorChar;
            Settings.ModName = modName;
            Settings.PenumbraLocation = penLocation;
            Settings.BaselineScdKey = BaselineScdTextBox.Text;
            Settings.BackgroundImagePath = BackgroundImageTextBox.Text.Trim();
            Settings.DefaultScdTemplateSourcePath = DefaultScdTemplateTextBox.Text.Trim();
            Settings.NormalizeVolume = NormalizeVolumeCheckBox.IsChecked == true;
            Settings.AutoReloadMod = AutoReloadCheckBox.IsChecked == true;
            Settings.AutoUpdateDependencies = AutoUpdateDependenciesCheckBox.IsChecked == true;

            if (!string.IsNullOrWhiteSpace(Settings.DefaultScdTemplateSourcePath) && File.Exists(Settings.DefaultScdTemplateSourcePath))
            {
                string targetDefaultScd = Path.Combine(Directory.GetCurrentDirectory(), "default.scd");
                File.Copy(Settings.DefaultScdTemplateSourcePath, targetDefaultScd, overwrite: true);
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


        private async void UpdateDependenciesButton_Click(object sender, RoutedEventArgs e)
        {
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
                ValidateFields();
            }
        }

    }
}
