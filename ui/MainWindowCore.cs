using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace STAGE
{
    public sealed partial class MainWindow
    {
        private bool _busyOverlayVisible;
        private readonly Dictionary<string, bool> _playlistExpandedStates = new();
        private Storyboard? _spinnerStoryboard;
        private CancellationTokenSource? _bpmScanCancellation;
        private int _playlistLoadGeneration;

        private bool IsVfxPapMode => _contentMode == MainContentMode.VfxPap;

        private void LoadCurrentContent() => LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);

        private void LoadCurrentContentDeferredAudioMetadata() =>
            LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty, deferAudioMetadata: true);

        private void LoadCurrentContentDeferredAudioMetadata(string filter) =>
            LoadCurrentContent(filter, deferAudioMetadata: true);

        private void LoadCurrentContent(string filter) => LoadCurrentContent(filter, deferAudioMetadata: false);

        private void LoadCurrentContent(string filter, bool deferAudioMetadata)
        {
            if (IsVfxPapMode)
                LoadVfxPapGroups(filter);
            else
                LoadPlaylists(filter, null, deferAudioMetadata);
        }

        public void LoadPlaylists() => LoadPlaylists(string.Empty);

        public void LoadPlaylistsAndExpand(string playlistName) => LoadPlaylists(string.Empty, playlistName);

        public void LoadPlaylists(string filter) => LoadPlaylists(filter, null);

        private void LoadPlaylists(string filter, string? forceExpandedPlaylistName) =>
            LoadPlaylists(filter, forceExpandedPlaylistName, deferAudioMetadata: false);

        private void LoadPlaylists(string filter, string? forceExpandedPlaylistName, bool deferAudioMetadata)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => LoadPlaylists(filter, forceExpandedPlaylistName, deferAudioMetadata));
                return;
            }

            // Snapshot expanded state before rebuild
            if (RootPlaylistItems.Count > 0)
            {
                foreach (var child in RootPlaylistItems[0].Children)
                    _playlistExpandedStates[child.Name] = child.IsExpanded;
            }

            try
            {
                _bpmScanCancellation?.Cancel();
                _bpmScanCancellation?.Dispose();
                _bpmScanCancellation = null;
                int loadGeneration = ++_playlistLoadGeneration;

                var loadedPlaylists = Playlist.GetAll()
                    .Where(pair => VfxPapGroup.IsAudioPlaylistGroup(pair.Value))
                    .ToList();
                if (BaselineScdDetector.TryApplyFromPlaylists(
                    loadedPlaylists.Select(pair => pair.Value), out string detectedBaseline))
                {
                    App.WriteStartupText(
                        "Baseline SCD auto-detected",
                        "BaselineScdKey: " + detectedBaseline);
                }
                Playlists = loadedPlaylists.ToDictionary(pair => pair.Key, pair => pair.Value);
                var missingAudioPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!Settings.BpmFirstTimeMessageShown)
                {
                    Settings.BpmFirstTimeMessageShown = true;
                    _ = ShowDialogAsync(AppStrings.Dlg_BPMDetection_Title, AppStrings.Dlg_BPMDetection_Content);
                }

                RootPlaylistItems.Clear();

                var rootContent = new PlaylistNodeContent
                {
                    Name = "Playlists",
                    DisplayText = "Playlists",
                    Level = 0,
                    IconGlyph = PlaylistNodeContent.RootGlyph,
                    IsExpanded = true
                };

                RootPlaylistItems.Add(rootContent);

                foreach (var playlist in Playlists.Values)
                {
                    bool wasExpanded = _playlistExpandedStates.TryGetValue(playlist.Name ?? "", out bool exp) && exp;

                    var playlistContent = new PlaylistNodeContent
                    {
                        Name = playlist.Name ?? "",
                        DisplayText = playlist.Name ?? "",
                        Level = 1,
                        IconGlyph = PlaylistNodeContent.PlaylistGlyph,
                        IsExpanded = wasExpanded
                    };

                    TimeSpan playlistTime = TimeSpan.Zero;
                    bool matchFound = false;

                    if (playlist.Options == null) continue;

                    foreach (var song in playlist.Options)
                    {
                        if (song == null) continue;
                        try
                        {
                            TimeSpan time = TimeSpan.Zero;
                            bool hasDuration = false;
                            int bpm = 0;
                            bool hasBpm = false;
                            string scdPath = Playlist.GetScdPath(song);
                            if (!string.IsNullOrEmpty(scdPath))
                            {
                                string fullScdPath = Playlist.GetFullScdPath(scdPath);
                                hasDuration = BPMDetector.TryGetCachedDuration(fullScdPath, out time);
                                hasBpm = BPMDetector.TryGetCachedBPM(fullScdPath, out bpm);
                                if (hasDuration)
                                    playlistTime = playlistTime.Add(time);
                                if (!hasBpm && File.Exists(fullScdPath))
                                    missingAudioPaths.Add(fullScdPath);
                            }

                            string displayText = song.Name
                                + GetBPMString(hasBpm, bpm)
                                + (hasDuration ? GetTimeString(time) : string.Empty);

                            var songContent = new PlaylistNodeContent
                            {
                                Name = song.Name,
                                SongScdPath = Playlist.GetScdPath(song) ?? string.Empty,
                                DisplayText = displayText,
                                Level = 2,
                                IconGlyph = PlaylistNodeContent.SongGlyph
                            };

                            if (!string.IsNullOrEmpty(filter))
                            {
                                var cmp = StringComparison.OrdinalIgnoreCase;
                                if (playlist.Name?.IndexOf(filter, cmp) >= 0 ||
                                    song.Name?.IndexOf(filter, cmp) >= 0)
                                {
                                    matchFound = true;
                                    playlistContent.AddChild(songContent);
                                }
                            }
                            else
                            {
                                playlistContent.AddChild(songContent);
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = ShowDialogAsync(AppStrings.Dlg_Error, AppStrings.ErrorLoadingSong(song.Name, playlist.Name, ex.Message));
                        }
                    }

                    if (filter.Length > 0 && playlistContent.Children.Count == 0)
                        continue;

                    // Update display text with duration
                    playlistContent.DisplayText = playlist.Name + GetTimeString(playlistTime);

                    if (matchFound)
                        playlistContent.IsExpanded = true;

                    if (!string.IsNullOrWhiteSpace(forceExpandedPlaylistName) &&
                        string.Equals(playlist.Name, forceExpandedPlaylistName, StringComparison.OrdinalIgnoreCase))
                    {
                        playlistContent.IsExpanded = true;
                    }

                    _playlistExpandedStates[playlistContent.Name] = playlistContent.IsExpanded;

                    rootContent.AddChild(playlistContent);
                }

                if (missingAudioPaths.Count > 0)
                {
                    _bpmScanCancellation = new CancellationTokenSource();
                    _ = QueueMissingAudioAnalysisAsync(
                        missingAudioPaths.ToList(),
                        filter,
                        forceExpandedPlaylistName,
                        loadGeneration,
                        _bpmScanCancellation.Token);
                }
            }
            catch (Exception ex)
            {
                _ = ShowDialogAsync(AppStrings.Dlg_Error, AppStrings.ErrorLoadingPlaylists(ex.Message));
            }
        }

        private async Task QueueMissingAudioAnalysisAsync(
            IReadOnlyList<string> scdPaths,
            string filter,
            string? forceExpandedPlaylistName,
            int loadGeneration,
            CancellationToken cancellationToken)
        {
            bool cachedAny = false;
            try
            {
                foreach (string scdPath in scdPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.Exists(scdPath))
                        {
                            await BPMDetector.AnalyzeQueuedAsync(scdPath, cancellationToken);
                            cachedAny |= BPMDetector.TryGetCachedBPM(scdPath, out _);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // One unreadable track should not stop the rest of the queue.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                return;
            }

            if (cachedAny && !cancellationToken.IsCancellationRequested && loadGeneration == _playlistLoadGeneration)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (loadGeneration == _playlistLoadGeneration)
                        if (!IsVfxPapMode)
                            LoadPlaylists(filter, forceExpandedPlaylistName);
                });
            }
        }

        private void LoadVfxPapGroups(string filter)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => LoadVfxPapGroups(filter));
                return;
            }

            try
            {
                _bpmScanCancellation?.Cancel();
                _bpmScanCancellation?.Dispose();
                _bpmScanCancellation = null;
                ++_playlistLoadGeneration;

                RootPlaylistItems.Clear();

                var rootContent = new PlaylistNodeContent
                {
                    Name = "VFX / PAP",
                    DisplayText = "VFX / PAP",
                    Level = 0,
                    IconGlyph = PlaylistNodeContent.RootGlyph,
                    IsExpanded = true
                };
                RootPlaylistItems.Add(rootContent);

                var groups = VfxPapGroup.GetAll();
                AddVfxPapCategory(rootContent, "Animations", VfxPapGroupKind.Animation, groups, filter);
                AddVfxPapCategory(rootContent, "Color Variants", VfxPapGroupKind.ColorVariant, groups, filter);
                AddVfxPapCategory(rootContent, "VFX Slots", VfxPapGroupKind.VfxSlot, groups, filter);
                AddVfxPapCategory(rootContent, "Other", VfxPapGroupKind.Other, groups, filter);
            }
            catch (Exception ex)
            {
                _ = ShowDialogAsync(AppStrings.Dlg_Error, $"Error loading VFX/PAP groups: {ex.Message}");
            }
        }

        private static void AddVfxPapCategory(
            PlaylistNodeContent rootContent,
            string categoryName,
            VfxPapGroupKind kind,
            IReadOnlyList<VfxPapGroup> groups,
            string filter)
        {
            var categoryContent = new PlaylistNodeContent
            {
                Name = categoryName,
                DisplayText = categoryName,
                Level = 1,
                IconGlyph = kind == VfxPapGroupKind.Animation ? PlaylistNodeContent.PapGlyph : PlaylistNodeContent.VfxGlyph,
                AssetKind = kind.ToString(),
                IsExpanded = true
            };

            foreach (var group in groups.Where(group => group.Kind == kind))
            {
                var groupContent = new PlaylistNodeContent
                {
                    Name = group.Name,
                    DisplayText = $"{group.Name} ({group.Options.Count} options)",
                    Level = 2,
                    IconGlyph = kind == VfxPapGroupKind.Animation ? PlaylistNodeContent.PapGlyph : PlaylistNodeContent.VfxGlyph,
                    AssetKind = kind.ToString(),
                    SourceJsonPath = group.FilePath,
                    IsExpanded = false
                };

                bool groupMatches = MatchesFilter(group.Name, filter);
                foreach (var option in group.Options)
                {
                    string optionName = option.Name ?? "";
                    bool optionMatches = groupMatches || MatchesFilter(optionName, filter) || OptionMappingsMatch(option, filter);
                    if (!optionMatches && !string.IsNullOrWhiteSpace(filter))
                        continue;

                    var optionContent = new PlaylistNodeContent
                    {
                        Name = optionName,
                        DisplayText = BuildVfxPapOptionText(option),
                        Level = 3,
                        IconGlyph = PlaylistNodeContent.OptionGlyph,
                        AssetKind = kind.ToString(),
                        SourceJsonPath = group.FilePath,
                        IsExpanded = false
                    };

                    if (option.Files != null)
                    {
                        foreach (var mapping in option.Files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(filter) &&
                                !groupMatches &&
                                !MatchesFilter(optionName, filter) &&
                                !MatchesFilter(mapping.Key, filter) &&
                                !MatchesFilter(mapping.Value, filter))
                            {
                                continue;
                            }

                            optionContent.AddChild(new PlaylistNodeContent
                            {
                                Name = mapping.Key,
                                DisplayText = $"{mapping.Key} -> {mapping.Value}",
                                Level = 4,
                                IconGlyph = PlaylistNodeContent.OptionGlyph,
                                AssetKind = kind.ToString(),
                                SourceJsonPath = group.FilePath,
                                AssetGamePath = mapping.Key,
                                AssetLocalPath = mapping.Value
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(filter) && optionContent.Children.Count > 0)
                        optionContent.IsExpanded = true;

                    groupContent.AddChild(optionContent);
                }

                if (!string.IsNullOrWhiteSpace(filter) && groupContent.Children.Count == 0 && !groupMatches)
                    continue;

                if (!string.IsNullOrWhiteSpace(filter))
                    groupContent.IsExpanded = true;

                categoryContent.AddChild(groupContent);
            }

            if (categoryContent.Children.Count > 0)
                rootContent.AddChild(categoryContent);
        }

        private static bool MatchesFilter(string value, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool OptionMappingsMatch(Option option, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                   option.Files?.Any(pair =>
                       MatchesFilter(pair.Key, filter) ||
                       MatchesFilter(pair.Value, filter)) == true;
        }

        private static string BuildVfxPapOptionText(Option option)
        {
            int fileCount = option.Files?.Count ?? 0;
            return fileCount == 0
                ? option.Name
                : $"{option.Name} ({fileCount} file{(fileCount == 1 ? "" : "s")})";
        }

        private void RecomputePlaylistDurations(bool checkUI = true)
        {
            LoadPlaylists();
        }

        private static string GetBPMString(bool hasBpm, int bpm)
        {
            return hasBpm ? " (" + bpm + " BPM)" : string.Empty;
        }

        private static string GetTimeString(TimeSpan time)
        {
            int hours = (int)time.TotalHours;
            return $" ({hours:D2}:{time.Minutes:D2}:{time.Seconds:D2})";
        }

        public void SetProgressBarPercent(int percent)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => SetProgressBarPercent(percent));
                return;
            }
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            ProgressBar1.Value = percent;
            BusyProgressBar.Value = percent;

            if (percent > 0 || !string.IsNullOrWhiteSpace(ProgressLabel.Text))
                SetBusyOverlayVisible(true);

            // Long operations may still be saving JSON, refreshing playlists, or showing
            // summaries after reporting 100%. Call ClearProgressDisplay explicitly when
            // the whole operation is actually done.
        }

        public void SetProgressBarText(string text)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => SetProgressBarText(text));
                return;
            }
            ProgressLabel.Text = text;
            BusyProgressLabel.Text = text;

            if (string.IsNullOrWhiteSpace(text))
                SetBusyOverlayVisible(false);
            else
                SetBusyOverlayVisible(true);
        }

        public void ClearProgressDisplay()
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(ClearProgressDisplay);
                return;
            }

            ProgressBar1.Value = 0;
            BusyProgressBar.Value = 0;
            ProgressLabel.Text = "";
            BusyProgressLabel.Text = "";
            SetBusyOverlayVisible(false);
        }

        private Storyboard GetSpinnerStoryboard()
        {
            if (_spinnerStoryboard != null) return _spinnerStoryboard;
            var anim = new DoubleAnimation
            {
                From = 0, To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1.1)),
                RepeatBehavior = RepeatBehavior.Forever,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(anim, BusySpinnerRotate);
            Storyboard.SetTargetProperty(anim, "Angle");
            _spinnerStoryboard = new Storyboard();
            _spinnerStoryboard.Children.Add(anim);
            return _spinnerStoryboard;
        }

        private void SetBusyOverlayVisible(bool visible)
        {
            if (_busyOverlayVisible == visible) return;

            _busyOverlayVisible = visible;
            BusyOverlay.Visibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            MainContentGrid.IsHitTestVisible = !visible;

            if (visible)
            {
                BusySpinnerRotate.Angle = 0;
                GetSpinnerStoryboard().Begin();
            }
            else
            {
                GetSpinnerStoryboard().Stop();
                BusySpinnerRotate.Angle = 0;
            }
        }

        private async Task<bool> DoDeleteAsync()
        {
            try
            {
                if (IsVfxPapMode)
                    return await DeleteSelectedVfxPapNodesAsync();

                var result = await ShowDialogAsync(
                    AppStrings.Dlg_ConfirmDelete_Title,
                    AppStrings.Dlg_ConfirmDelete_Content,
                    AppStrings.Btn_Yes, null, AppStrings.Btn_No);

                if (result != ContentDialogResult.Primary) return false;

                var selectedItems = PlaylistTreeView.SelectedItems.OfType<PlaylistNodeContent>().ToList();
                foreach (var item in selectedItems)
                {
                    if (item.Level == 1 && Playlists.TryGetValue(item.Name, out var pl))
                    {
                        int checkedChildCount = selectedItems.Count(x => x.Level == 2 && x.Parent == item);
                        if (checkedChildCount > 0)
                        {
                            // skip — child songs are selected
                        }
                        else if (pl.Options.Count > 1)
                        {
                            await ShowDialogAsync(
                                AppStrings.Dlg_NonEmptyPlaylist_Title,
                                AppStrings.Dlg_NonEmptyPlaylist_Content);
                        }
                        else
                        {
                            pl.Delete();
                        }
                    }
                    else if (item.Level == 2 && item.Parent != null)
                    {
                        if (Playlists.TryGetValue(item.Parent.Name, out var parentPl))
                        {
                            var song = FindSongOption(parentPl, item);
                            if (song != null && !song.Name.Equals("Off", StringComparison.InvariantCultureIgnoreCase))
                            {
                                string? scdPath = Playlist.GetScdPath(song);
                                parentPl.Options.Remove(song);
                                parentPl.Save();

                                if (!string.IsNullOrWhiteSpace(scdPath))
                                {
                                    string fullSongPath = Playlist.GetFullScdPath(scdPath);
                                    if (File.Exists(fullSongPath))
                                        File.Delete(fullSongPath);
                                    string baselinePath = fullSongPath + ".stage-audio-baseline";
                                    if (File.Exists(baselinePath))
                                        File.Delete(baselinePath);

                                    string? containingDir = Path.GetDirectoryName(fullSongPath);
                                    string playlistDir = Playlist.GetPlaylistDirectory(parentPl.Name);
                                    if (!string.IsNullOrWhiteSpace(containingDir) &&
                                        containingDir.StartsWith(playlistDir, StringComparison.OrdinalIgnoreCase) &&
                                        Directory.Exists(containingDir) &&
                                        !Directory.EnumerateFileSystemEntries(containingDir).Any())
                                    {
                                        Directory.Delete(containingDir, true);
                                    }
                                }
                            }
                        }
                    }
                }

                DeleteButton.IsEnabled = false;
                ShuffleButton.IsEnabled = false;
                SortByBPMButton.IsEnabled = false;
                LoadPlaylists();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(AppStrings.Dlg_Error, AppStrings.ErrorDeletion(ex.Message));
                return false;
            }
            return true;
        }

        private string ResolveTargetPlaylistForSingle()
        {
            if (_selectedNode != null)
            {
                if (_selectedNode.Level == 1) return _selectedNode.Name;
                if (_selectedNode.Level == 2 && _selectedNode.Parent != null) return _selectedNode.Parent.Name;
            }
            return null;
        }

        // Automatic in-app update checks intentionally disabled.
    }
}
