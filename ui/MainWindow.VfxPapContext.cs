using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace STAGE
{
    public sealed partial class MainWindow
    {
        private void ShowVfxPapContextMenu(PlaylistNodeContent content, Windows.Foundation.Point point)
        {
            var flyout = new MenuFlyout();

            if (IsPapOptionNode(content))
            {
                var details = new MenuFlyoutItem { Text = "View PAP Details" };
                details.Click += (_, _) => _ = ViewPapDetailsAsync(content);
                flyout.Items.Add(details);

                var expression = new MenuFlyoutItem { Text = "Add Expression..." };
                expression.Click += (_, _) => _ = AddExpressionAsync(content);
                flyout.Items.Add(expression);

                var renameOption = new MenuFlyoutItem { Text = "Rename PAP Option" };
                renameOption.Click += (_, _) => _ = RenamePapOptionAsync(content);
                flyout.Items.Add(renameOption);
            }

            if (IsPapMappingNode(content))
            {
                var details = new MenuFlyoutItem { Text = "View PAP Details" };
                details.Click += (_, _) => _ = ViewPapDetailsAsync(content);
                flyout.Items.Add(details);

                var expression = new MenuFlyoutItem { Text = "Add Expression..." };
                expression.Click += (_, _) => _ = AddExpressionAsync(content);
                flyout.Items.Add(expression);

                var renameFile = new MenuFlyoutItem { Text = "Rename PAP File" };
                renameFile.Click += (_, _) => _ = RenamePapFileAsync(content);
                flyout.Items.Add(renameFile);
            }

            if (IsVfxPapOptionNode(content))
            {
                if (flyout.Items.Count > 0)
                    flyout.Items.Add(new MenuFlyoutSeparator());

                var deleteOption = new MenuFlyoutItem { Text = $"Delete {GetVfxPapAssetLabel(content)} Option" };
                deleteOption.Click += (_, _) => _ = DeleteVfxPapOptionAsync(content);
                flyout.Items.Add(deleteOption);
            }

            if (IsVfxPapMappingNode(content))
            {
                if (flyout.Items.Count > 0)
                    flyout.Items.Add(new MenuFlyoutSeparator());

                var deleteMapping = new MenuFlyoutItem { Text = $"Delete {GetVfxPapAssetLabel(content)} File Mapping" };
                deleteMapping.Click += (_, _) => _ = DeleteVfxPapMappingAsync(content);
                flyout.Items.Add(deleteMapping);
            }

            if (flyout.Items.Count == 0)
            {
                var unavailable = new MenuFlyoutItem
                {
                    Text = "No PAP action available",
                    IsEnabled = false
                };
                flyout.Items.Add(unavailable);
            }

            flyout.ShowAt(PlaylistTreeView, point);
        }

        private static bool IsPapOptionNode(PlaylistNodeContent content)
        {
            return content.Level == 3 &&
                   string.Equals(content.AssetKind, VfxPapGroupKind.Animation.ToString(), StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(content.SourceJsonPath);
        }

        private static bool IsVfxPapOptionNode(PlaylistNodeContent content)
        {
            return content.Level == 3 &&
                   !string.IsNullOrWhiteSpace(content.SourceJsonPath) &&
                   IsDeletableVfxPapKind(content.AssetKind);
        }

        private static bool IsPapMappingNode(PlaylistNodeContent content)
        {
            return content.Level == 4 &&
                   !string.IsNullOrWhiteSpace(content.SourceJsonPath) &&
                   (content.AssetLocalPath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase) ||
                    content.AssetGamePath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsVfxPapMappingNode(PlaylistNodeContent content)
        {
            return content.Level == 4 &&
                   !string.IsNullOrWhiteSpace(content.SourceJsonPath) &&
                   IsDeletableVfxPapKind(content.AssetKind) &&
                   (content.AssetLocalPath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase) ||
                    content.AssetGamePath.EndsWith(".pap", StringComparison.OrdinalIgnoreCase) ||
                    content.AssetLocalPath.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase) ||
                    content.AssetGamePath.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsDeletableVfxPapKind(string kind)
        {
            return string.Equals(kind, VfxPapGroupKind.Animation.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(kind, VfxPapGroupKind.VfxSlot.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBulkDeletableVfxPapNode(PlaylistNodeContent content)
        {
            return IsVfxPapOptionNode(content) || IsVfxPapMappingNode(content);
        }

        private static string GetVfxPapAssetLabel(PlaylistNodeContent content)
        {
            return string.Equals(content.AssetKind, VfxPapGroupKind.Animation.ToString(), StringComparison.OrdinalIgnoreCase)
                ? "PAP"
                : "VFX";
        }

        private async Task AddExpressionAsync(PlaylistNodeContent node)
        {
            try
            {
                string papPath = ResolveExpressionPapPath(node);
                var selected = await ShowExpressionPickerAsync(Path.GetFileName(papPath));
                if (selected == null)
                    return;

                bool replaced = BeesKneesPapTmbPatcher.ApplyExpression(papPath, selected.Key);
                PenumbraApi.ScheduleReloadModFolder(GetVfxPapModDirectory(node.SourceJsonPath));
                await ShowDialogAsync(
                    "Expression Applied",
                    $"{selected.DisplayName} ({selected.Key}) was applied to:\n{papPath}\n\n" +
                    (replaced
                        ? "Actor 0's existing expression C010 animation was replaced."
                        : "A C010 animation track was added to Actor 0.") +
                    "\n\nThe original PAP was saved beside it as a .stage-original.bak file.");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Expression Failed", ex.Message);
            }
        }

        private async Task ViewPapDetailsAsync(PlaylistNodeContent node)
        {
            try
            {
                string papPath = ResolveExpressionPapPath(node);
                PapInspection inspection = await Task.Run(() =>
                    BeesKneesPapTmbPatcher.InspectPap(papPath));

                var details = new System.Text.StringBuilder();
                details.AppendLine($"File: {inspection.FilePath}");
                details.AppendLine($"Size: {FormatPapBytes(inspection.FileBytes)}");
                details.AppendLine($"Animations: {inspection.Animations.Count}");

                foreach (PapAnimationInspection animation in inspection.Animations)
                {
                    details.AppendLine();
                    details.AppendLine($"Animation {animation.Index + 1}");
                    details.AppendLine($"  Name: {animation.Name}");
                    details.AppendLine($"  Embedded TMB: {FormatPapBytes(animation.EmbeddedTmbBytes)}");
                    details.AppendLine($"  Actors: {animation.ActorCount}");
                    details.AppendLine($"  Tracks: {animation.TrackCount}");
                    details.AppendLine($"  Actor 0 tracks: {animation.Actor0TrackCount}");
                    details.AppendLine($"  Events: {animation.EventCount}");
                    details.AppendLine(
                        $"  C009 animation-only: {FormatPapValues(animation.AnimationOnlyNames)}");
                    details.AppendLine(
                        $"  C010 expressions: {FormatPapValues(animation.Expressions)}");
                    details.AppendLine(
                        $"  Event types: {(animation.EventTypes.Count == 0 ? "None" : string.Join(
                            ", ",
                            animation.EventTypes.Select(pair => $"{pair.Key} ×{pair.Value}")))}");
                }

                var text = new TextBlock
                {
                    Text = details.ToString().TrimEnd(),
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    IsTextSelectionEnabled = true,
                    Padding = new Thickness(12)
                };
                var scroller = new ScrollViewer
                {
                    Content = text,
                    Height = 520,
                    MinWidth = 720,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Enabled,
                    VerticalScrollMode = ScrollMode.Enabled
                };
                var dialog = new ContentDialog
                {
                    Title = $"PAP Details — {Path.GetFileName(papPath)}",
                    Content = scroller,
                    CloseButtonText = "Close",
                    XamlRoot = Content.XamlRoot,
                    MinWidth = 760,
                    MaxWidth = 920
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Could Not Read PAP Details", ex.Message);
            }
        }

        private static string FormatPapValues(IReadOnlyList<string> values) =>
            values.Count == 0 ? "None" : string.Join(", ", values);

        private static string FormatPapBytes(long bytes)
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

        private static string ResolveExpressionPapPath(PlaylistNodeContent node)
        {
            string localPath;
            if (IsPapMappingNode(node))
            {
                localPath = node.AssetLocalPath;
            }
            else
            {
                var papMappings = node.Children
                    .Where(IsPapMappingNode)
                    .ToList();
                if (papMappings.Count == 0)
                    throw new InvalidOperationException("This option has no PAP file mapping.");

                var loop = papMappings.FirstOrDefault(mapping =>
                    mapping.AssetLocalPath.Contains("loop", StringComparison.OrdinalIgnoreCase) ||
                    mapping.AssetGamePath.Contains("loop", StringComparison.OrdinalIgnoreCase));
                if (loop != null)
                    localPath = loop.AssetLocalPath;
                else if (papMappings.Count == 1)
                    localPath = papMappings[0].AssetLocalPath;
                else
                    throw new InvalidOperationException(
                        "This option has multiple PAP files and no clear loop PAP. Right-click the exact PAP file instead.");
            }

            string modDirectory = GetVfxPapModDirectory(node.SourceJsonPath);
            string fullPath = Path.GetFullPath(Path.Combine(
                modDirectory,
                localPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            string safeRoot = Path.GetFullPath(modDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The PAP mapping points outside the mod directory.");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Could not find the mapped PAP file.", fullPath);
            return fullPath;
        }

        private static string GetVfxPapModDirectory(string sourceJsonPath)
        {
            if (PenumbraMeta.IsGroupReference(sourceJsonPath) &&
                PenumbraMeta.TryParseGroupReference(sourceJsonPath, out string modDirectory, out _, out _))
            {
                return modDirectory;
            }

            return Path.GetDirectoryName(sourceJsonPath)
                   ?? throw new InvalidOperationException("Could not determine the mod directory.");
        }

        private async Task<ExpressionCatalogItem?> ShowExpressionPickerAsync(string papFileName)
        {
            var catalog = ExpressionCatalog.Load();
            var search = new TextBox
            {
                PlaceholderText = "Search expressions...",
                Margin = new Thickness(0, 0, 0, 10)
            };
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                Height = 520
            };
            var source = new TextBlock
            {
                Text = $"Previews: {catalog.Source}",
                Opacity = 0.65,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"Choose an expression for {papFileName}.",
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(search);
            panel.Children.Add(list);
            panel.Children.Add(source);

            var dialog = new ContentDialog
            {
                Title = "Add Expression",
                Content = panel,
                PrimaryButtonText = "Apply Expression",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.Content.XamlRoot,
                MaxWidth = 920
            };

            void Populate(string filter)
            {
                string value = filter.Trim();
                list.Items.Clear();
                foreach (var expression in catalog.Expressions.Where(item =>
                             value.Length == 0 ||
                             item.DisplayName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                             item.Key.Contains(value, StringComparison.OrdinalIgnoreCase)))
                {
                    var previews = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Width = 340
                    };
                    foreach (string relativePath in expression.Previews)
                    {
                        string fullPath = Path.Combine(
                            ExpressionCatalog.DirectoryPath,
                            relativePath.Replace('/', Path.DirectorySeparatorChar));
                        previews.Children.Add(new Image
                        {
                            Source = new BitmapImage(new Uri(fullPath)) { DecodePixelWidth = 220 },
                            Width = 160,
                            Height = 160,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        });
                    }

                    var labels = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 0, 0)
                    };
                    labels.Children.Add(new TextBlock { Text = expression.DisplayName, FontSize = 16 });
                    labels.Children.Add(new TextBlock { Text = expression.Key, Opacity = 0.7 });
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(previews);
                    row.Children.Add(labels);
                    list.Items.Add(new ListViewItem
                    {
                        Content = row,
                        Tag = expression,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    });
                }
            }

            search.TextChanged += (_, _) => Populate(search.Text ?? "");
            list.SelectionChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = list.SelectedItem is ListViewItem;
            Populate("");

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary &&
                   list.SelectedItem is ListViewItem selectedItem
                ? selectedItem.Tag as ExpressionCatalogItem
                : null;
        }

        private async Task RenamePapOptionAsync(PlaylistNodeContent node)
        {
            string currentName = node.Name;
            string? newName = await PromptForTextAsync("Rename PAP Option", "Option name", currentName);
            if (string.IsNullOrWhiteSpace(newName) ||
                string.Equals(newName.Trim(), currentName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var result = VfxPapRenamer.RenameOption(node.SourceJsonPath, currentName, newName.Trim());
                LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);
                await ShowDialogAsync(
                    "PAP Option Renamed",
                    $"Renamed to '{result.NewName}'.\n\n" +
                    $"Updated mappings: {result.UpdatedMappings}\n" +
                    $"Renamed file(s):\n{FormatFileList(result.RenamedFiles)}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("PAP Rename Failed", ex.Message);
            }
        }

        private async Task RenamePapFileAsync(PlaylistNodeContent node)
        {
            string currentName = Path.GetFileNameWithoutExtension(node.AssetLocalPath);
            string? newName = await PromptForTextAsync("Rename PAP File", "PAP file name", currentName);
            if (string.IsNullOrWhiteSpace(newName) ||
                string.Equals(newName.Trim(), currentName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var result = VfxPapRenamer.RenameMapping(node.SourceJsonPath, node.AssetLocalPath, newName.Trim());
                LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);
                await ShowDialogAsync(
                    "PAP File Renamed",
                    $"Renamed to '{result.NewName}'.\n\n" +
                    $"Updated mappings: {result.UpdatedMappings}\n" +
                    $"Renamed file(s):\n{FormatFileList(result.RenamedFiles)}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("PAP Rename Failed", ex.Message);
            }
        }

        private async Task DeleteVfxPapOptionAsync(PlaylistNodeContent node)
        {
            string label = GetVfxPapAssetLabel(node);
            var confirm = await ShowDialogAsync(
                $"Delete {label} Option",
                $"Delete '{node.Name}' from this group?\n\nAny .pap/.avfx files used only by this option will also be deleted from disk.",
                "Delete",
                close: "Cancel");
            if (confirm != ContentDialogResult.Primary)
                return;

            try
            {
                var result = VfxPapDeleter.DeleteOption(node.SourceJsonPath, node.Name);
                LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);
                await ShowDialogAsync(
                    $"{label} Option Deleted",
                    $"Removed options: {result.RemovedOptions}\n" +
                    $"Removed mappings: {result.RemovedMappings}\n" +
                    $"Deleted file(s):\n{FormatDeleteFileList(result.DeletedFiles)}\n\n" +
                    $"Kept because still referenced:\n{FormatDeleteFileList(result.KeptReferencedFiles)}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync($"{label} Delete Failed", ex.Message);
            }
        }

        private async Task DeleteVfxPapMappingAsync(PlaylistNodeContent node)
        {
            string label = GetVfxPapAssetLabel(node);
            string optionName = node.Parent?.Name ?? "";
            var confirm = await ShowDialogAsync(
                $"Delete {label} File Mapping",
                $"Delete this mapping?\n\n{node.AssetGamePath} -> {node.AssetLocalPath}\n\nThe physical file will be deleted only if no group JSON still references it.",
                "Delete",
                close: "Cancel");
            if (confirm != ContentDialogResult.Primary)
                return;

            try
            {
                var result = VfxPapDeleter.DeleteMapping(node.SourceJsonPath, optionName, node.AssetGamePath, node.AssetLocalPath);
                LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);
                await ShowDialogAsync(
                    $"{label} File Mapping Deleted",
                    $"Removed mappings: {result.RemovedMappings}\n" +
                    $"Deleted file(s):\n{FormatDeleteFileList(result.DeletedFiles)}\n\n" +
                    $"Kept because still referenced:\n{FormatDeleteFileList(result.KeptReferencedFiles)}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync($"{label} Delete Failed", ex.Message);
            }
        }

        private async Task<bool> DeleteSelectedVfxPapNodesAsync()
        {
            var selectedNodes = PlaylistTreeView.SelectedItems
                .OfType<PlaylistNodeContent>()
                .Where(IsBulkDeletableVfxPapNode)
                .ToList();

            if (selectedNodes.Count == 0)
                return false;

            var optionNodes = selectedNodes
                .Where(IsVfxPapOptionNode)
                .DistinctBy(node => $"{node.SourceJsonPath}\n{node.Name}")
                .ToList();

            var mappingNodes = selectedNodes
                .Where(IsVfxPapMappingNode)
                .Where(node => !HasSelectedOptionAncestor(node, optionNodes))
                .DistinctBy(node => $"{node.SourceJsonPath}\n{node.Parent?.Name}\n{node.AssetGamePath}\n{node.AssetLocalPath}")
                .ToList();

            int totalTargets = optionNodes.Count + mappingNodes.Count;
            if (totalTargets == 0)
                return false;

            var confirm = await ShowDialogAsync(
                "Delete Selected VFX/PAP Items",
                $"Delete {totalTargets} selected VFX/PAP item{(totalTargets == 1 ? "" : "s")}?\n\n" +
                "Files on disk will only be deleted when no group JSON still references them.",
                "Delete",
                close: "Cancel");
            if (confirm != ContentDialogResult.Primary)
                return false;

            int removedOptions = 0;
            int removedMappings = 0;
            var deletedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();

            foreach (var node in optionNodes)
            {
                try
                {
                    var result = VfxPapDeleter.DeleteOption(node.SourceJsonPath, node.Name);
                    removedOptions += result.RemovedOptions;
                    removedMappings += result.RemovedMappings;
                    AddDeleteFiles(result, deletedFiles, keptFiles);
                }
                catch (Exception ex)
                {
                    errors.Add($"{node.Name}: {ex.Message}");
                }
            }

            foreach (var node in mappingNodes)
            {
                try
                {
                    string optionName = node.Parent?.Name ?? "";
                    var result = VfxPapDeleter.DeleteMapping(
                        node.SourceJsonPath,
                        optionName,
                        node.AssetGamePath,
                        node.AssetLocalPath);
                    removedOptions += result.RemovedOptions;
                    removedMappings += result.RemovedMappings;
                    AddDeleteFiles(result, deletedFiles, keptFiles);
                }
                catch (Exception ex)
                {
                    errors.Add($"{node.Parent?.Name ?? "(unknown option)"}/{node.AssetGamePath}: {ex.Message}");
                }
            }

            PlaylistTreeView.SelectedItems.Clear();
            DeleteButton.IsEnabled = false;
            LoadCurrentContent(SearchTextBox.Text?.Trim() ?? string.Empty);

            await ShowDialogAsync(
                errors.Count == 0 ? "Selected VFX/PAP Items Deleted" : "VFX/PAP Delete Completed With Errors",
                $"Removed options: {removedOptions}\n" +
                $"Removed mappings: {removedMappings}\n" +
                $"Deleted file(s):\n{FormatDeleteFileList(deletedFiles.ToList())}\n\n" +
                $"Kept because still referenced:\n{FormatDeleteFileList(keptFiles.ToList())}" +
                (errors.Count == 0 ? "" : $"\n\nErrors:\n{string.Join("\n", errors)}"));

            return errors.Count == 0;
        }

        private static bool HasSelectedOptionAncestor(
            PlaylistNodeContent mappingNode,
            IReadOnlyCollection<PlaylistNodeContent> optionNodes)
        {
            var parent = mappingNode.Parent;
            while (parent != null)
            {
                if (optionNodes.Contains(parent))
                    return true;
                parent = parent.Parent;
            }

            return false;
        }

        private static void AddDeleteFiles(
            VfxPapDeleteResult result,
            ISet<string> deletedFiles,
            ISet<string> keptFiles)
        {
            foreach (string file in result.DeletedFiles)
                deletedFiles.Add(file);
            foreach (string file in result.KeptReferencedFiles)
                keptFiles.Add(file);
        }

        private async Task<string?> PromptForTextAsync(string title, string placeholder, string currentValue)
        {
            var box = new TextBox
            {
                Text = currentValue,
                SelectionStart = 0,
                SelectionLength = currentValue.Length,
                PlaceholderText = placeholder
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = box,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? box.Text?.Trim() : null;
        }

        private static string FormatFileList(System.Collections.Generic.IReadOnlyList<string> files)
        {
            return files.Count == 0
                ? "(No physical rename needed.)"
                : string.Join("\n", files.Select(file => file));
        }

        private static string FormatDeleteFileList(System.Collections.Generic.IReadOnlyList<string> files)
        {
            return files.Count == 0
                ? "(None.)"
                : string.Join("\n", files.Select(file => file));
        }
    }
}
