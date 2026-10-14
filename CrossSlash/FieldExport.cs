// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CrossSlash {
    public class FieldExport : ExportKind {
        private ModelBaseOptions _config = new ModelBaseOptions();
        public override string Help =>
@"Usage: CrossSlash FieldModel [SourceLGPOrFolder] [OutputFile] [HRCFile] [anim1] [anim2] [anim3...] [options]
    Valid options:
        /ConvertSRGBToLinear - convert colours to linear RGB
        /SwapWinding - reverse triangle winding order
        /BakeVertexColours - export vertex colours as a texture
    For example:
        CrossSlash FieldModel C:\games\FF7\data\field\char.lgp C:\temp\cloud.glb AAAA.HRC ACFE.a AAFF.a /SwapWinding
";

        public override object Config => _config;
        public override bool HasGui => true;
        public override string Name => "Field Model Export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            var exporter = new FieldModel(source, _config);
            var model = exporter.BuildScene(parameters.First(), parameters.Skip(1));
            model.SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new FieldExportWindow(source);
    }

    public class FieldExportWindow : Window {
        private readonly ListBox _lvHRCs, _lstAnimations;
        private readonly TextBox _txtModelSearch, _txtAnimationSearch;
        private readonly ICollectionView _hrcView;
        private readonly List<string> _hrcFiles, _animationFiles;
        private List<string> _compatibleAnimationFiles = new();
        private readonly HashSet<string> _selectedAnimationFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly CheckBox _chkAllCompatible, _chkEmbedAnimations;
        private bool _refreshingAnimationList;
        private readonly CheckBox _chkSRGB;
        private readonly CheckBox _chkSwapWinding;
        private readonly CheckBox _chkBakeColours;
        private readonly TextBlock _animationHint;
        private readonly DataSource _source;

        public FieldExportWindow(DataSource source) {
            _source = source;
            _hrcFiles = source.AllFiles
                .Where(file => Path.GetExtension(file).Equals(".hrc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _animationFiles = source.AllFiles
                .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _txtModelSearch = DesktopUi.CreateSearchTextBox("Search field models");
            _txtAnimationSearch = DesktopUi.CreateSearchTextBox("Search compatible animations");
            _hrcView = CollectionViewSource.GetDefaultView(_hrcFiles);
            _hrcView.Filter = item => item is string file &&
                (string.IsNullOrWhiteSpace(_txtModelSearch.Text) || file.Contains(_txtModelSearch.Text, StringComparison.OrdinalIgnoreCase));
            _lvHRCs = new ListBox {
                ItemsSource = _hrcView,
                MinHeight = 140,
                MaxHeight = 190,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            _txtModelSearch.TextChanged += (_, _) => _hrcView.Refresh();
            _txtAnimationSearch.TextChanged += (_, _) => RefreshAnimationList();

            _lstAnimations = new ListBox {
                ItemsSource = Array.Empty<string>(),
                SelectionMode = SelectionMode.Multiple,
                MinHeight = 140,
                MaxHeight = 190,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                IsEnabled = false,
            };
            _animationHint = DesktopUi.CreateHint("Compatible animations for the selected model will appear here.");

            _chkAllCompatible = new CheckBox {
                Content = "Use all compatible animations",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 12),
            };
            _chkEmbedAnimations = new CheckBox {
                Content = "Embed animations",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8),
            };
            _chkEmbedAnimations.Checked += (_, _) => UpdateAnimationControlState();
            _chkEmbedAnimations.Unchecked += (_, _) => UpdateAnimationControlState();
            _chkAllCompatible.Checked += (_, _) => UpdateAnimationControlState();
            _chkAllCompatible.Unchecked += (_, _) => UpdateAnimationControlState();
            UpdateAnimationControlState();
            _lstAnimations.SelectionChanged += (_, e) => {
                if (_refreshingAnimationList)
                    return;
                foreach (string animation in e.RemovedItems.Cast<string>())
                    _selectedAnimationFiles.Remove(animation);
                foreach (string animation in e.AddedItems.Cast<string>())
                    _selectedAnimationFiles.Add(animation);
            };
            _lvHRCs.SelectionChanged += (_, _) => RefreshCompatibleAnimations();
            _lvHRCs.Loaded += (_, _) => {
                if (_lvHRCs.SelectedIndex < 0 && _lvHRCs.Items.Count > 0)
                    _lvHRCs.SelectedIndex = 0;
            };

            _chkSRGB = new CheckBox { Content = "Convert colours from sRGB to linear", IsChecked = true, Margin = new Thickness(0, 0, 18, 8) };
            _chkSwapWinding = new CheckBox { Content = "Swap triangle winding", Margin = new Thickness(0, 0, 18, 8) };
            _chkBakeColours = new CheckBox { Content = "Bake vertex colours to texture", Margin = new Thickness(0, 0, 18, 8) };

            var content = new StackPanel();

            var pickerColumns = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            pickerColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pickerColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            modelColumn.Children.Add(DesktopUi.CreateSectionTitle("Models"));
            modelColumn.Children.Add(DesktopUi.CreateHint($"{_hrcFiles.Count:N0} HRC models found."));
            modelColumn.Children.Add(CreateModelPicker());
            pickerColumns.Children.Add(modelColumn);

            var animationColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            animationColumn.Children.Add(DesktopUi.CreateSectionTitle("Compatible animations"));
            animationColumn.Children.Add(CreateAnimationSearch());
            animationColumn.Children.Add(_chkAllCompatible);
            animationColumn.Children.Add(_animationHint);
            animationColumn.Children.Add(_lstAnimations);
            Grid.SetColumn(animationColumn, 1);
            pickerColumns.Children.Add(animationColumn);

            content.Children.Add(pickerColumns);
            content.Children.Add(_chkEmbedAnimations);
            content.Children.Add(new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { _chkSRGB, _chkSwapWinding, _chkBakeColours },
            });

            var export = DesktopUi.CreateButton("Export selected", primary: true);
            export.Click += (_, _) => ExportOne();
            var exportAll = DesktopUi.CreateButton("Export all + animations");
            exportAll.Click += (_, _) => ExportAllModels();
            var exportAnimations = DesktopUi.CreateButton("Export all animations separately");
            exportAnimations.Click += (_, _) => ExportAllAnimations();
            content.Children.Add(DesktopUi.CreateActionRow(export, exportAll, exportAnimations));

            DesktopUi.ConfigureWindow(
                this,
                content,
                width: 1120,
                height: 820
            );
        }

        private void ExportOne() {
            try {
                if (_lvHRCs.SelectedItem is not string hrcFile)
                    throw new InvalidOperationException("Select an HRC model first.");

                var animations = _chkAllCompatible.IsChecked == true
                    ? _compatibleAnimationFiles
                    : _selectedAnimationFiles.Where(_compatibleAnimationFiles.Contains).ToList();
                if (_chkEmbedAnimations.IsChecked == true && _chkAllCompatible.IsChecked != true && animations.Count == 0)
                    throw new InvalidOperationException("Select one or more compatible animations, or enable all compatible animations.");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var exporter = new FieldModel(_source, GetOptions());
                var model = exporter.BuildScene(
                    hrcFile,
                    _chkEmbedAnimations.IsChecked == true ? animations : Array.Empty<string>(),
                    compatibleAnimationsOnly: _chkEmbedAnimations.IsChecked != true || _chkAllCompatible.IsChecked == true,
                    out var usedAnimations,
                    includeAnimations: _chkEmbedAnimations.IsChecked == true
                );
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);

                string message = _chkEmbedAnimations.IsChecked == true
                    ? $"Export succeeded.\nAnimations included: {usedAnimations.Count}"
                    : "Export succeeded.\nAnimations were not embedded.";
                DesktopUi.ShowInfo(this, "Export complete", message);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Export failed", ex.Message);
            }
        }

        private void ExportAllModels() {
            try {
                if (_hrcFiles.Count == 0)
                    throw new InvalidOperationException("No HRC field models were found in the selected source.");

                string? outputDirectory = ChooseOutputFolder("Choose a folder for all field model exports");
                if (outputDirectory == null)
                    return;

                var animations = _chkEmbedAnimations.IsChecked == true ? GetAllAnimationFiles() : Array.Empty<string>();
                int exported = 0;
                int animationCount = 0;
                int modelsWithoutAnimations = 0;
                var failures = new List<string>();
                foreach (string hrcFile in _hrcFiles) {
                    try {
                        var exporter = new FieldModel(_source, GetOptions());
                        var model = exporter.BuildScene(
                            hrcFile,
                            animations,
                            compatibleAnimationsOnly: true,
                            out var usedAnimations,
                            includeAnimations: _chkEmbedAnimations.IsChecked == true
                        );
                        model.SaveGLB(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(hrcFile) + ".glb"));
                        exported++;
                        animationCount += usedAnimations.Count;
                        if (usedAnimations.Count == 0)
                            modelsWithoutAnimations++;
                    } catch (Exception ex) {
                        failures.Add($"{hrcFile}: {ex.Message}");
                    }
                }

                string animationSummary = _chkEmbedAnimations.IsChecked == true
                    ? $"Compatible animation clips: {animationCount}\nModels without compatible animations: {modelsWithoutAnimations}"
                    : "Animations were not embedded.";
                string result = $"Exported {exported} of {_hrcFiles.Count} field models to:\n{outputDirectory}\n\n{animationSummary}";
                DesktopUi.ShowInfo(this, "Export all field models", AppendFailures(result, failures));
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Export failed", ex.Message);
            }
        }

        private void ExportAllAnimations() {
            try {
                if (_hrcFiles.Count == 0)
                    throw new InvalidOperationException("No HRC field models were found in the selected source.");

                string? outputDirectory = ChooseOutputFolder("Choose a folder for all field animations");
                if (outputDirectory == null)
                    return;

                var animations = GetAllAnimationFiles();
                int exportedModels = 0;
                int animationCount = 0;
                int modelsWithoutAnimations = 0;
                var failures = new List<string>();
                foreach (string hrcFile in _hrcFiles) {
                    try {
                        var exporter = new FieldModel(_source, GetOptions());
                        var model = exporter.BuildScene(
                            hrcFile,
                            animations,
                            compatibleAnimationsOnly: true,
                            out var usedAnimations,
                            includeMeshes: false,
                            includeAnimations: true
                        );
                        animationCount += usedAnimations.Count;
                        if (usedAnimations.Count == 0) {
                            modelsWithoutAnimations++;
                            continue;
                        }

                        string outputFile = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(hrcFile), "Animations.glb");
                        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                        model.SaveGLB(outputFile);
                        exportedModels++;
                    } catch (Exception ex) {
                        failures.Add($"{hrcFile}: {ex.Message}");
                    }
                }

                string result = $"Exported animations for {exportedModels} field models to:\n{outputDirectory}\n\n" +
                    $"Compatible animation clips: {animationCount}\nModels without compatible animations: {modelsWithoutAnimations}";
                DesktopUi.ShowInfo(this, "Export all field animations", AppendFailures(result, failures));
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Export failed", ex.Message);
            }
        }

        private UIElement CreateModelPicker() {
            var picker = new StackPanel();
            var searchRow = DesktopUi.CreateFormRow("Search", _txtModelSearch, 80);
            searchRow.Margin = new Thickness(0, 0, 0, 6);
            picker.Children.Add(searchRow);
            picker.Children.Add(_lvHRCs);
            return picker;
        }

        private UIElement CreateAnimationSearch() {
            var searchRow = DesktopUi.CreateFormRow("Search", _txtAnimationSearch, 80);
            searchRow.Margin = new Thickness(0, 0, 0, 4);
            return searchRow;
        }

        private void RefreshCompatibleAnimations() {
            _selectedAnimationFiles.Clear();
            if (_lvHRCs.SelectedItem is not string hrcFile) {
                _compatibleAnimationFiles = new List<string>();
                RefreshAnimationList();
                _animationHint.Text = "Select a field model to find its compatible animations.";
                return;
            }

            try {
                var exporter = new FieldModel(_source, GetOptions());
                _compatibleAnimationFiles = exporter.FindCompatibleAnimations(hrcFile, _animationFiles).ToList();
                RefreshAnimationList();
                _animationHint.Text = _compatibleAnimationFiles.Count == 0
                    ? "No compatible animations were found for this model."
                    : $"{_compatibleAnimationFiles.Count:N0} compatible animations found for {hrcFile}.";
                _chkAllCompatible.Content = $"Use all compatible animations ({_compatibleAnimationFiles.Count:N0})";
            } catch (Exception ex) {
                _compatibleAnimationFiles = new List<string>();
                RefreshAnimationList();
                _animationHint.Text = $"Could not check animation compatibility: {ex.Message}";
                _chkAllCompatible.Content = "Use all compatible animations";
            }
        }

        private void RefreshAnimationList() {
            var visibleAnimations = _compatibleAnimationFiles
                .Where(file => string.IsNullOrWhiteSpace(_txtAnimationSearch.Text) ||
                    file.Contains(_txtAnimationSearch.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _refreshingAnimationList = true;
            try {
                _lstAnimations.UnselectAll();
                _lstAnimations.ItemsSource = visibleAnimations;
                foreach (string animation in visibleAnimations) {
                    if (_selectedAnimationFiles.Contains(animation))
                        _lstAnimations.SelectedItems.Add(animation);
                }
            } finally {
                _refreshingAnimationList = false;
            }
            UpdateAnimationControlState();
        }

        private void UpdateAnimationControlState() {
            bool embedding = _chkEmbedAnimations.IsChecked == true;
            bool chooseAnimations = embedding && _chkAllCompatible.IsChecked != true;
            _chkAllCompatible.IsEnabled = embedding;
            _txtAnimationSearch.IsEnabled = chooseAnimations;
            _lstAnimations.IsEnabled = chooseAnimations;
        }

        private IEnumerable<string> GetAllAnimationFiles() => _animationFiles;

        private ModelBaseOptions GetOptions() => new() {
            ConvertSRGBToLinear = _chkSRGB.IsChecked == true,
            SwapWinding = _chkSwapWinding.IsChecked == true,
            BakeVertexColours = _chkBakeColours.IsChecked == true,
        };

        private string? ChooseOutputFolder(string title) {
            var preferences = PreferencesStore.Load();
            string? path = NativeFilePicker.OpenFolder(
                title,
                preferences.LastOutputDirectory,
                this
            );
            if (path != null)
                PreferencesStore.RememberOutputDirectory(path);
            return path;
        }

        private string? ChooseGlbPath() {
            var preferences = PreferencesStore.Load();
            return NativeFilePicker.SaveGlb(preferences.LastOutputDirectory, this);
        }

        private static string AppendFailures(string result, List<string> failures) {
            if (failures.Count == 0)
                return result;
            result += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
            if (failures.Count > 5)
                result += "\n…";
            return result;
        }
    }
}
