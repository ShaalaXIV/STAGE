using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using STAGE.Tools;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;

namespace STAGE
{
    public sealed class YouTubeDownloadResult
    {
        public List<string> DownloadedFiles { get; set; } = new();
        public bool IsPlaylist { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? TargetPlaylistName { get; init; }
        public EqualizerSettings AudioSettings { get; init; } = new();
    }

    public sealed partial class YouTubeDownloadDialog : ContentDialog
    {
        public YouTubeDownloadResult? DownloadResult { get; private set; }
        private bool _initialized;

        public YouTubeDownloadDialog() : this(null)
        {
        }

        public YouTubeDownloadDialog(string? preferredPlaylistName)
        {
            this.InitializeComponent();
            foreach (string presetName in EqualizerSettings.PresetNames)
                PresetComboBox.Items.Add(presetName);
            PresetComboBox.SelectedIndex = (int)Settings.EqualizerPreset;
            VolumeSlider.Value = Settings.ScdAudioVolume;
            CookieBrowserComboBox.SelectedIndex = (int)Settings.YouTubeCookieBrowser;
            _initialized = true;
            UpdateVolumeLabel();
            LoadTargetPlaylists(preferredPlaylistName);
            UpdateTargetPlaylistState();
        }

        private void LoadTargetPlaylists(string? preferredPlaylistName)
        {
            var playlistNames = Playlist.GetAll().Keys.OrderBy(x => x).ToList();
            TargetPlaylistComboBox.ItemsSource = playlistNames;

            if (!string.IsNullOrWhiteSpace(preferredPlaylistName) && playlistNames.Contains(preferredPlaylistName))
                TargetPlaylistComboBox.SelectedItem = preferredPlaylistName;
            else if (playlistNames.Count > 0)
                TargetPlaylistComboBox.SelectedIndex = 0;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTargetPlaylistState();
        }

        private void UpdateTargetPlaylistState()
        {
            if (TargetPlaylistComboBox == null || ModeComboBox == null)
                return;

            // Playlist mode always creates a brand-new playlist, so selecting a target is disabled.
            TargetPlaylistComboBox.IsEnabled = ModeComboBox.SelectedIndex != 1;
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_initialized)
                UpdateVolumeLabel();
        }

        private void UpdateVolumeLabel()
        {
            VolumeValueLabel.Text = VolumeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private EqualizerSettings GetSelectedAudioSettings()
        {
            int presetIndex = PresetComboBox.SelectedIndex;
            if (presetIndex < 0 || presetIndex >= EqualizerSettings.PresetNames.Length)
                presetIndex = (int)EqualizerPreset.Neutral;

            var settings = new EqualizerSettings { VolumeLevel = (float)VolumeSlider.Value };
            settings.ApplyPreset((EqualizerPreset)presetIndex);
            return settings;
        }

        private async void DownloadButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var url = UrlTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                args.Cancel = true;
                StatusLabel.Text = AppStrings.Dlg_EnterYouTubeUrl;
                return;
            }

            var deferral = args.GetDeferral();
            IsPrimaryButtonEnabled = false;
            StatusLabel.Text = AppStrings.Prog_PreparingDownload;
            ProgressBar1.Value = 5;

            var tempDir = Path.Combine(Path.GetTempPath(), "stage-ytdlp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var mode = ModeComboBox.SelectedIndex == 1 ? YtDownloadMode.Playlist : YtDownloadMode.Single;
                var cookieBrowser = (YtCookieBrowser)Math.Clamp(
                    CookieBrowserComboBox.SelectedIndex, 0, (int)YtCookieBrowser.Edge);

                if (Settings.AutoUpdateDependencies)
                {
                    StatusLabel.Text = "Checking dependency updates...";
                    ProgressBar1.Value = 3;
                    try
                    {
                        await DependencyUpdateService.EnsureDependenciesUpToDateAsync(
                            s => StatusLabel.Text = s,
                            allowLargeDownloads: false);
                    }
                    catch (Exception ex)
                    {
                        App.WriteStartupLog("Dependency auto-update skipped", ex);
                        StatusLabel.Text = "Dependency update skipped; using bundled tools...";
                    }
                    ProgressBar1.Value = 5;
                }

                StatusLabel.Text = "Reading video info...";
                ProgressBar1.Value = 8;
                var progress = new Progress<YtDlpProgressInfo>(info =>
                {
                    StatusLabel.Text = $"{info.Stage} {info.Current}/{info.Total}";
                    int baseProgress = 10, maxProgress = 90;
                    double stageProgress = (info.Current - 1) / (double)Math.Max(1, info.Total);
                    if (info.Percent.HasValue)
                        stageProgress += (info.Percent.Value / 100.0) / Math.Max(1, info.Total);
                    var percent = baseProgress + (int)Math.Round(stageProgress * (maxProgress - baseProgress));
                    ProgressBar1.Value = Math.Clamp(percent, 0, 100);
                });

                var dlResult = await YtDlpService.DownloadAudioAsync(
                    url,
                    tempDir,
                    mode,
                    cookieBrowser,
                    p => ((IProgress<YtDlpProgressInfo>)progress).Report(p));

                EqualizerSettings audioSettings = GetSelectedAudioSettings();
                DownloadResult = new YouTubeDownloadResult
                {
                    DownloadedFiles = dlResult.DownloadedFiles,
                    IsPlaylist = dlResult.IsPlaylist,
                    Title = dlResult.Title ?? string.Empty,
                    TargetPlaylistName = mode == YtDownloadMode.Single ? TargetPlaylistComboBox.SelectedItem as string : null,
                    AudioSettings = audioSettings,
                };
                Settings.EqualizerPreset = (EqualizerPreset)Math.Clamp(
                    PresetComboBox.SelectedIndex, 0, EqualizerSettings.PresetNames.Length - 1);
                Settings.ScdAudioVolume = audioSettings.VolumeLevel;
                Settings.YouTubeCookieBrowser = cookieBrowser;

                ProgressBar1.Value = 100;
                StatusLabel.Text = AppStrings.Prog_Done;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = AppStrings.YTDownloadFailed(ex.Message);
                IsPrimaryButtonEnabled = true;
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
