using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed partial class MainWindow
    {
        private async System.Threading.Tasks.Task OpenPapImportDialogAsync(PapImportMode mode)
        {
            string? singlePapPath = null;
            string? startPapPath = null;
            string? loopPapPath = null;
            string? templatePapPath = File.Exists(Settings.DefaultDonorPapPath)
                ? Settings.DefaultDonorPapPath
                : null;

            var groupPaths = VfxPapImporter.GetPapImportGroupPaths();
            string defaultGroupPath = VfxPapImporter.SuggestedDefaultGroupPath(mode);
            if (groupPaths.Count == 0 && !groupPaths.Contains(defaultGroupPath, StringComparer.OrdinalIgnoreCase))
                groupPaths.Insert(0, defaultGroupPath);

            var optionNameBox = new TextBox
            {
                Header = "Option Name",
                PlaceholderText = mode == PapImportMode.StartAndLoop ? "Example: Vibin'" : "Example: Breakdance"
            };
            var groupCombo = new ComboBox
            {
                Header = "Target Group JSON",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = groupPaths.Select(path => new PapImportGroupChoice(path)).ToList()
            };
            groupCombo.SelectedIndex = 0;

            var singlePapBox = new TextBox
            {
                IsReadOnly = true,
                PlaceholderText = "Select an animation .pap file"
            };
            var singlePapNameBox = new TextBox
            {
                Header = "Destination File Name",
                PlaceholderText = "Example: breakdance_loop"
            };
            var startPapBox = new TextBox
            {
                IsReadOnly = true,
                PlaceholderText = "Select the startup .pap file"
            };
            var startPapNameBox = new TextBox
            {
                Header = "Startup Destination Name",
                PlaceholderText = "Example: vibin_start"
            };
            var loopPapBox = new TextBox
            {
                IsReadOnly = true,
                PlaceholderText = "Select the loop .pap file"
            };
            var loopPapNameBox = new TextBox
            {
                Header = "Loop Destination Name",
                PlaceholderText = "Example: vibin_loop"
            };
            var statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            };
            var openAnimationSettingsButton = new Button
            {
                Content = "Open Animation Settings",
                Visibility = templatePapPath == null ? Visibility.Visible : Visibility.Collapsed
            };

            async System.Threading.Tasks.Task PickPapAsync(Action<string> setPath, TextBox fileNameBox)
            {
                string? path = await PickSingleFileAsync(".pap");
                if (string.IsNullOrWhiteSpace(path))
                    return;

                setPath(path);
                if (string.IsNullOrWhiteSpace(optionNameBox.Text))
                    optionNameBox.Text = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(fileNameBox.Text))
                    fileNameBox.Text = Path.GetFileNameWithoutExtension(path);
            }

            var pickSingleButton = new Button { Content = "Browse...", MinWidth = 90 };
            pickSingleButton.Click += async (_, _) => await PickPapAsync(path =>
            {
                singlePapPath = path;
                singlePapBox.Text = path;
            }, singlePapNameBox);

            var pickStartButton = new Button { Content = "Browse...", MinWidth = 90 };
            pickStartButton.Click += async (_, _) => await PickPapAsync(path =>
            {
                startPapPath = path;
                startPapBox.Text = path;
            }, startPapNameBox);

            var pickLoopButton = new Button { Content = "Browse...", MinWidth = 90 };
            pickLoopButton.Click += async (_, _) => await PickPapAsync(path =>
            {
                loopPapPath = path;
                loopPapBox.Text = path;
            }, loopPapNameBox);

            static Grid FileRow(string label, TextBox box, Button button)
            {
                var grid = new Grid { ColumnSpacing = 8 };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var header = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) };
                Grid.SetColumnSpan(header, 2);
                Grid.SetRow(box, 1);
                Grid.SetRow(button, 1);
                Grid.SetColumn(button, 1);
                grid.Children.Add(header);
                grid.Children.Add(box);
                grid.Children.Add(button);
                return grid;
            }

            var stack = new StackPanel { Spacing = 10, MinWidth = 560 };
            stack.Children.Add(new TextBlock
            {
                Text = mode == PapImportMode.StartAndLoop
                    ? "Import a startup and looping animation as one option. The loop inherits the donor animation's TMB setup."
                    : "Import one looping animation using the donor configured in Animation Settings.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            });
            stack.Children.Add(optionNameBox);
            stack.Children.Add(groupCombo);

            if (mode == PapImportMode.StartAndLoop)
            {
                stack.Children.Add(FileRow("Startup Animation", startPapBox, pickStartButton));
                stack.Children.Add(startPapNameBox);
                stack.Children.Add(FileRow("Loop Animation", loopPapBox, pickLoopButton));
                stack.Children.Add(loopPapNameBox);
            }
            else
            {
                stack.Children.Add(FileRow("Animation File", singlePapBox, pickSingleButton));
                stack.Children.Add(singlePapNameBox);
                stack.Children.Add(new TextBlock
                {
                    Text = templatePapPath == null
                        ? "Donor animation is not configured. Set one under Settings → Animation."
                        : $"Donor animation: {Path.GetFileName(templatePapPath)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = templatePapPath == null
                        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                    : null
                });
                stack.Children.Add(openAnimationSettingsButton);
            }
            if (mode == PapImportMode.StartAndLoop)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = templatePapPath == null
                        ? "Donor animation is not configured. Set one under Settings → Animation."
                        : $"Loop donor animation: {Path.GetFileName(templatePapPath)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = templatePapPath == null
                        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                        : null
                });
                stack.Children.Add(openAnimationSettingsButton);
            }

            stack.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = mode == PapImportMode.StartAndLoop
                    ? "Import Animation w/ Startup"
                    : "Import Animation",
                Content = stack,
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.Content.XamlRoot
            };
            openAnimationSettingsButton.Click += async (_, _) =>
            {
                dialog.Hide();
                await OpenSettingsAsync();
            };

            void UpdateImportEnabled()
            {
                bool commonReady = !string.IsNullOrWhiteSpace(optionNameBox.Text)
                    && groupCombo.SelectedItem != null;
                dialog.IsPrimaryButtonEnabled = mode == PapImportMode.StartAndLoop
                    ? commonReady
                      && !string.IsNullOrWhiteSpace(startPapPath)
                      && !string.IsNullOrWhiteSpace(startPapNameBox.Text)
                      && !string.IsNullOrWhiteSpace(loopPapPath)
                      && !string.IsNullOrWhiteSpace(loopPapNameBox.Text)
                      && !string.IsNullOrWhiteSpace(templatePapPath)
                    : commonReady
                      && !string.IsNullOrWhiteSpace(singlePapPath)
                      && !string.IsNullOrWhiteSpace(singlePapNameBox.Text)
                      && !string.IsNullOrWhiteSpace(templatePapPath);
            }

            optionNameBox.TextChanged += (_, _) => UpdateImportEnabled();
            singlePapBox.TextChanged += (_, _) => UpdateImportEnabled();
            singlePapNameBox.TextChanged += (_, _) => UpdateImportEnabled();
            startPapBox.TextChanged += (_, _) => UpdateImportEnabled();
            startPapNameBox.TextChanged += (_, _) => UpdateImportEnabled();
            loopPapBox.TextChanged += (_, _) => UpdateImportEnabled();
            loopPapNameBox.TextChanged += (_, _) => UpdateImportEnabled();
            groupCombo.SelectionChanged += (_, _) => UpdateImportEnabled();
            UpdateImportEnabled();

            PapImportResult? importResult = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (groupCombo.SelectedItem is not PapImportGroupChoice groupChoice)
                        throw new InvalidOperationException("Pick a target group JSON first.");

                    var request = new PapImportRequest(
                        mode,
                        optionNameBox.Text?.Trim() ?? "",
                        groupChoice.Path,
                        singlePapPath,
                        singlePapNameBox.Text?.Trim(),
                        startPapPath,
                        startPapNameBox.Text?.Trim(),
                        loopPapNameBox.Text?.Trim(),
                        loopPapPath,
                        templatePapPath);

                    importResult = VfxPapImporter.Import(request);
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    statusText.Text = $"Import failed: {ex.Message}";
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || importResult == null)
                return;

            LoadCurrentContent();
            await ShowDialogAsync(
                "PAP Imported",
                $"Added '{importResult.OptionName}' to {importResult.GroupName}.\n\n" +
                $"Mappings: {importResult.MappingCount}\n" +
                $"Copied file(s):\n{string.Join("\n", importResult.CopiedFiles)}");
        }

        private sealed class PapImportGroupChoice
        {
            public string Path { get; }

            public PapImportGroupChoice(string path)
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
    }
}
