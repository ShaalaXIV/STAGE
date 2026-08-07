using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed partial class MainWindow
    {
        private async System.Threading.Tasks.Task OpenAvfxImportDialogAsync()
        {
            string? sourceAvfxPath = null;

            var groupPaths = VfxAvfxImporter.GetVfxSlotGroupPaths();
            string defaultGroupPath = VfxAvfxImporter.SuggestedDefaultGroupPath(1);
            if (!groupPaths.Contains(defaultGroupPath, StringComparer.OrdinalIgnoreCase))
                groupPaths.Insert(0, defaultGroupPath);

            var optionNameBox = new TextBox
            {
                Header = "Option Name",
                PlaceholderText = "Example: Flame Circle"
            };
            var sourceAvfxBox = new TextBox
            {
                IsReadOnly = true,
                PlaceholderText = "Select a VFX .avfx file"
            };
            var groupCombo = new ComboBox
            {
                Header = "Target VFX Group",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = groupPaths.Select(path => new AvfxImportGroupChoice(path)).ToList()
            };
            groupCombo.SelectedIndex = 0;

            var slotBox = new NumberBox
            {
                Header = "Slot Number",
                Minimum = 1,
                Maximum = 6,
                Value = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
            };
            var addToAllBox = new CheckBox
            {
                Content = "Add this VFX to all slots",
                IsChecked = false
            };
            var statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            };

            groupCombo.SelectionChanged += (_, _) =>
            {
                if (groupCombo.SelectedItem is AvfxImportGroupChoice groupChoice)
                    slotBox.Value = VfxAvfxImporter.GuessSlotNumberFromGroupPath(groupChoice.Path);
            };
            addToAllBox.Checked += (_, _) =>
            {
                groupCombo.IsEnabled = false;
                slotBox.IsEnabled = false;
            };
            addToAllBox.Unchecked += (_, _) =>
            {
                groupCombo.IsEnabled = true;
                slotBox.IsEnabled = true;
            };

            var pickAvfxButton = new Button { Content = "Browse...", MinWidth = 90 };
            pickAvfxButton.Click += async (_, _) =>
            {
                string? path = await PickSingleFileAsync(".avfx");
                if (string.IsNullOrWhiteSpace(path))
                    return;

                sourceAvfxPath = path;
                sourceAvfxBox.Text = path;
                if (string.IsNullOrWhiteSpace(optionNameBox.Text))
                    optionNameBox.Text = Path.GetFileNameWithoutExtension(path);
            };

            var fileGrid = new Grid { ColumnSpacing = 8 };
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileHeader = new TextBlock { Text = "VFX File", Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumnSpan(fileHeader, 2);
            Grid.SetRow(sourceAvfxBox, 1);
            Grid.SetRow(pickAvfxButton, 1);
            Grid.SetColumn(pickAvfxButton, 1);
            fileGrid.Children.Add(fileHeader);
            fileGrid.Children.Add(sourceAvfxBox);
            fileGrid.Children.Add(pickAvfxButton);

            var stack = new StackPanel { Spacing = 10, MinWidth = 540 };
            stack.Children.Add(new TextBlock
            {
                Text = "Choose a VFX file, name it, and select where it should be available.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            });
            stack.Children.Add(optionNameBox);
            stack.Children.Add(fileGrid);
            stack.Children.Add(addToAllBox);
            stack.Children.Add(groupCombo);
            stack.Children.Add(slotBox);
            stack.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = "Import VFX",
                Content = stack,
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.Content.XamlRoot
            };

            void UpdateImportEnabled()
            {
                dialog.IsPrimaryButtonEnabled =
                    !string.IsNullOrWhiteSpace(optionNameBox.Text)
                    && !string.IsNullOrWhiteSpace(sourceAvfxPath)
                    && (addToAllBox.IsChecked == true || groupCombo.SelectedItem != null);
            }

            optionNameBox.TextChanged += (_, _) => UpdateImportEnabled();
            sourceAvfxBox.TextChanged += (_, _) => UpdateImportEnabled();
            groupCombo.SelectionChanged += (_, _) => UpdateImportEnabled();
            addToAllBox.Checked += (_, _) => UpdateImportEnabled();
            addToAllBox.Unchecked += (_, _) => UpdateImportEnabled();
            UpdateImportEnabled();

            List<AvfxImportResult>? importResults = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (addToAllBox.IsChecked == true)
                    {
                        var request = new AvfxImportRequest(
                            optionNameBox.Text?.Trim() ?? "",
                            "",
                            1,
                            sourceAvfxPath ?? "");

                        importResults = VfxAvfxImporter.ImportToAllSlots(request);
                    }
                    else
                    {
                        if (groupCombo.SelectedItem is not AvfxImportGroupChoice groupChoice)
                            throw new InvalidOperationException("Pick a target VFX slot group first.");

                        var request = new AvfxImportRequest(
                            optionNameBox.Text?.Trim() ?? "",
                            groupChoice.Path,
                            ReadNumberBoxInt(slotBox, 1),
                            sourceAvfxPath ?? "");

                        importResults = new List<AvfxImportResult> { VfxAvfxImporter.Import(request) };
                    }
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    statusText.Text = $"Import failed: {ex.Message}";
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || importResults == null || importResults.Count == 0)
                return;

            LoadCurrentContent();
            var firstResult = importResults[0];
            string message = importResults.Count == 1
                ? $"Added '{firstResult.OptionName}' to {firstResult.GroupName}.\n\n" +
                  $"{firstResult.GamePath} -> {firstResult.LocalPath}\n\n" +
                  $"Copied file:\n{firstResult.CopiedFile}"
                : $"Added '{firstResult.OptionName}' to {importResults.Count} VFX slot groups.\n\n" +
                  string.Join("\n", importResults.Select(item => $"{item.GroupName}: {item.GamePath} -> {item.LocalPath}")) +
                  $"\n\nCopied file:\n{firstResult.CopiedFile}";

            await ShowDialogAsync(
                "AVFX Imported",
                message);
        }

        private sealed class AvfxImportGroupChoice
        {
            public string Path { get; }

            public AvfxImportGroupChoice(string path)
            {
                Path = path;
            }

            public override string ToString()
            {
                if (PenumbraMeta.TryParseGroupReference(Path, out _, out _, out string groupName))
                    return groupName;

                string file = System.IO.Path.GetFileName(Path);
                if (!File.Exists(Path))
                    return $"{file} (new)";

                try
                {
                    var group = Newtonsoft.Json.JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(Path));
                    return group == null ? file : $"{group.Name} ({file})";
                }
                catch
                {
                    return file;
                }
            }
        }

        private static int ReadNumberBoxInt(NumberBox box, int fallback)
        {
            if (double.IsNaN(box.Value) || double.IsInfinity(box.Value))
                return fallback;

            return (int)Math.Round(box.Value);
        }
    }
}
