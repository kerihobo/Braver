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
using System.Windows.Threading;

namespace CrossSlash {
    public class FieldExport : ExportKind {
        private ModelBaseOptions _config = new ModelBaseOptions();
        public override string Help =>
@"Usage: CrossSlash FieldModel [SourceLGPOrFolder] [OutputFile] [HRCFile] [anim1] [anim2] [anim3...] [options]
    Valid options:
        /ConvertSRGBToLinear - convert colours to linear RGB
        /SwapWinding - reverse triangle winding order
        /BakeVertexColours - export vertex colours as a texture
        /Scale:value - scale model up/down from default size
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

    internal sealed class FieldPreviewPointLight {
        public int Color { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public FieldPreviewPointLight Clone() => new() { Color = Color, X = X, Y = Y, Z = Z };
    }

    internal sealed class FieldPreviewLightingSettings {
        public int AmbientColor { get; set; } = 0x575455;
        public double AmbientTintMultiplier { get; set; } = 2.0;
        public double PositionMultiplier { get; set; } = 5.0;
        public double ReachMultiplier { get; set; } = 20.0;
        public double StrengthMultiplier { get; set; } = 1.0;
        public List<FieldPreviewPointLight> PointLights { get; set; } = new();

        public static FieldPreviewLightingSettings CreateWorkingDefaults() => new() {
            PointLights = new List<FieldPreviewPointLight> {
                new() { Color = 0x939091, X = 83, Y = -2098, Z = 3533 },
                new() { Color = 0x6C6767, X = 1536, Y = -2443, Z = -2910 },
                new() { Color = 0x4D4D4D, X = -2954, Y = -1463, Z = -2457 },
            },
        };

        public FieldPreviewLightingSettings Clone() => new() {
            AmbientColor = AmbientColor,
            AmbientTintMultiplier = AmbientTintMultiplier,
            PositionMultiplier = PositionMultiplier,
            ReachMultiplier = ReachMultiplier,
            StrengthMultiplier = StrengthMultiplier,
            PointLights = PointLights.Select(light => light.Clone()).ToList(),
        };
    }

    public class FieldExportWindow : Window {
        private const double FieldLightPositionScale = 512.0;
        private const double FieldLightBaseIntensity = 12.0;
        private readonly ListBox _lvHRCs, _lstAnimations;
        private readonly TextBox _txtModelSearch, _txtAnimationSearch;
        private readonly ICollectionView _hrcView;
        private readonly List<string> _hrcFiles, _animationFiles;
        private List<string> _compatibleAnimationFiles = new();
        private readonly HashSet<string> _selectedAnimationFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly CheckBox _chkEmbedAnimations;
        private readonly CheckBox _chkFirstFrameBindPose;
        private bool _refreshingAnimationList;
        private readonly CheckBox _chkSRGB;
        private readonly CheckBox _chkSwapWinding;
        private readonly CheckBox _chkBakeColours;
        private readonly TextBox _txtScale;
        private readonly TextBlock _modelTitle, _animationTitle;
        private readonly DataSource _source;
        private readonly FieldModel _compatibilityExporter;
        private readonly ModelPreviewControl _preview;
        private readonly DispatcherTimer _previewTimer;
        private long _animationLookupRequest;
        private bool _animationLookupPending;
        private string? _previewAnimationFile;
        private FieldPreviewLightingSettings _lightingSettings = FieldPreviewLightingSettings.CreateWorkingDefaults();
        private FieldLightingEditorWindow? _lightingEditorWindow;

        public FieldExportWindow(DataSource source) {
            _source = source;
            _compatibilityExporter = new FieldModel(source, new ModelBaseOptions());
            _preview = new ModelPreviewControl { Height = 340 };
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _previewTimer.Tick += (_, _) => {
                _previewTimer.Stop();
                RefreshPreview();
            };
            Closed += (_, _) => {
                _previewTimer.Stop();
                Interlocked.Increment(ref _animationLookupRequest);
            };
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
                Background = DesktopUi.InputBackgroundBrush,
                Foreground = DesktopUi.TextPrimaryBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
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
                Background = DesktopUi.InputBackgroundBrush,
                Foreground = DesktopUi.TextPrimaryBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                IsEnabled = false,
                ToolTip = "Left-click an animation to preview it. Right-click to preview it and include or exclude it from export.",
            };
            _modelTitle = DesktopUi.CreateSectionTitle($"Models ({_hrcFiles.Count:N0})");
            _animationTitle = DesktopUi.CreateSectionTitle("Compatible animations (0)");

            _chkEmbedAnimations = new CheckBox {
                Content = "Embed animations",
                IsChecked = true,
                Margin = new Thickness(0, 0, 18, 8),
            };
            _chkEmbedAnimations.Checked += (_, _) => { UpdateAnimationControlState(); SchedulePreview(); };
            _chkEmbedAnimations.Unchecked += (_, _) => { UpdateAnimationControlState(); SchedulePreview(); };
            _chkFirstFrameBindPose = new CheckBox {
                Content = "Use first animation frame as bind pose",
                IsChecked = false,
                ToolTip = "Use the first frame of the first compatible animation as the field model's bind pose.",
                Margin = new Thickness(0, 0, 18, 8),
            };
            _chkFirstFrameBindPose.Checked += (_, _) => SchedulePreview();
            _chkFirstFrameBindPose.Unchecked += (_, _) => SchedulePreview();
            UpdateAnimationControlState();
            _lstAnimations.SelectionChanged += (_, e) => {
                if (_refreshingAnimationList)
                    return;
                foreach (string animation in e.RemovedItems.Cast<string>())
                    _selectedAnimationFiles.Remove(animation);
                foreach (string animation in e.AddedItems.Cast<string>())
                    _selectedAnimationFiles.Add(animation);
                SchedulePreview();
            };
            _lstAnimations.PreviewMouseLeftButtonDown += (sender, e) => {
                if (!TryGetAnimationItem(e.OriginalSource as DependencyObject, out _, out string animation))
                    return;
                PreviewAnimation(animation);
                e.Handled = true;
            };
            _lstAnimations.PreviewMouseRightButtonDown += (_, e) => {
                if (!TryGetAnimationItem(e.OriginalSource as DependencyObject, out ListBoxItem? item, out string animation))
                    return;
                PreviewAnimation(animation);
                item!.IsSelected = !item.IsSelected;
                e.Handled = true;
            };
            _lvHRCs.SelectionChanged += (_, _) => {
                RefreshCompatibleAnimations();
                SchedulePreview();
            };
            _lvHRCs.Loaded += (_, _) => {
                if (_lvHRCs.SelectedIndex < 0 && _lvHRCs.Items.Count > 0)
                    _lvHRCs.SelectedIndex = 0;
            };

            _chkSRGB = new CheckBox { Content = "Convert colours from sRGB to linear", IsChecked = true, Margin = new Thickness(0, 0, 18, 8) };
            _chkSwapWinding = new CheckBox { Content = "Swap triangle winding", Margin = new Thickness(0, 0, 18, 8) };
            _chkBakeColours = new CheckBox { Content = "Bake vertex colours to texture", Margin = new Thickness(0, 0, 18, 8) };
            _txtScale = DesktopUi.CreateTextBox("1");
            _txtScale.TextChanged += (_, _) => SchedulePreview();
            _chkSRGB.Checked += (_, _) => SchedulePreview();
            _chkSRGB.Unchecked += (_, _) => SchedulePreview();
            _chkSwapWinding.Checked += (_, _) => SchedulePreview();
            _chkSwapWinding.Unchecked += (_, _) => SchedulePreview();
            _chkBakeColours.Checked += (_, _) => SchedulePreview();
            _chkBakeColours.Unchecked += (_, _) => SchedulePreview();

            var content = new StackPanel();

            var pickerColumns = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            pickerColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.31, GridUnitType.Star) });
            pickerColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.31, GridUnitType.Star) });
            pickerColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.38, GridUnitType.Star) });

            var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            modelColumn.Children.Add(_modelTitle);
            modelColumn.Children.Add(CreateModelPicker());
            pickerColumns.Children.Add(modelColumn);

            var animationColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            animationColumn.Children.Add(_animationTitle);
            animationColumn.Children.Add(CreateAnimationSearch());
            animationColumn.Children.Add(_lstAnimations);
            Grid.SetColumn(animationColumn, 1);
            pickerColumns.Children.Add(animationColumn);

            var previewColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            var previewHeader = new Grid { Height = 24, Margin = new Thickness(0, 0, 0, 12) };
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var previewTitle = DesktopUi.CreateSectionTitle("Preview");
            previewTitle.Margin = new Thickness(0);
            previewTitle.VerticalAlignment = VerticalAlignment.Center;
            previewHeader.Children.Add(previewTitle);
            var editLighting = DesktopUi.CreateButton("Edit lights…");
            editLighting.Height = 24;
            editLighting.Padding = new Thickness(8, 0, 8, 0);
            editLighting.Margin = new Thickness(8, 0, 0, 0);
            editLighting.VerticalAlignment = VerticalAlignment.Center;
            editLighting.Click += (_, _) => OpenLightingEditorWindow();
            Grid.SetColumn(editLighting, 1);
            previewHeader.Children.Add(editLighting);
            previewColumn.Children.Add(previewHeader);
            previewColumn.Children.Add(_preview);
            Grid.SetColumn(previewColumn, 2);
            pickerColumns.Children.Add(previewColumn);

            content.Children.Add(pickerColumns);
            content.Children.Add(new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { DesktopUi.CreateScaleOption(_txtScale), _chkSRGB, _chkSwapWinding, _chkBakeColours, _chkEmbedAnimations, _chkFirstFrameBindPose },
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

                var animations = GetSelectedOrAllCompatibleAnimations();

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var exporter = new FieldModel(_source, GetOptions());
                var model = exporter.BuildScene(
                    hrcFile,
                    _chkEmbedAnimations.IsChecked == true || _chkFirstFrameBindPose.IsChecked == true
                        ? animations
                        : Array.Empty<string>(),
                    compatibleAnimationsOnly: true,
                    out var usedAnimations,
                    includeAnimations: _chkEmbedAnimations.IsChecked == true,
                    useFirstAnimationFrameAsBindPose: _chkFirstFrameBindPose.IsChecked == true
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

                var animations = _chkEmbedAnimations.IsChecked == true || _chkFirstFrameBindPose.IsChecked == true
                    ? GetSelectedOrAllAnimationFiles()
                    : Array.Empty<string>();
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
                            includeAnimations: _chkEmbedAnimations.IsChecked == true,
                            useFirstAnimationFrameAsBindPose: _chkFirstFrameBindPose.IsChecked == true
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

                var animations = GetSelectedOrAllAnimationFiles();
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
                            includeAnimations: true,
                            useFirstAnimationFrameAsBindPose: _chkFirstFrameBindPose.IsChecked == true
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
            picker.Children.Add(DesktopUi.CreateSearchField("Search models…", _txtModelSearch));
            picker.Children.Add(_lvHRCs);
            return picker;
        }

        private UIElement CreateAnimationSearch() {
            return DesktopUi.CreateSearchField("Search animations…", _txtAnimationSearch);
        }

        private void RefreshCompatibleAnimations() {
            long request = Interlocked.Increment(ref _animationLookupRequest);
            _selectedAnimationFiles.Clear();
            _previewAnimationFile = null;
            if (_lvHRCs.SelectedItem is not string hrcFile) {
                _animationLookupPending = false;
                _compatibleAnimationFiles = new List<string>();
                RefreshAnimationList();
                _animationTitle.Text = "Compatible animations (0)";
                _animationTitle.ToolTip = "Select a field model to see compatible animations.";
                return;
            }

            _animationLookupPending = true;
            _compatibleAnimationFiles = new List<string>();
            RefreshAnimationList();
            _animationTitle.Text = "Compatible animations (…)";
            _animationTitle.ToolTip = $"Checking compatible animations for {hrcFile}.";
            _preview.PrepareViewer();
            _ = LoadCompatibleAnimationsAsync(hrcFile, request);
        }

        private async Task LoadCompatibleAnimationsAsync(string hrcFile, long request) {
            try {
                IReadOnlyList<string> compatible = await Task.Run(() =>
                    _compatibilityExporter.FindCompatibleAnimations(hrcFile, _animationFiles)
                );
                if (request != Interlocked.Read(ref _animationLookupRequest))
                    return;

                _compatibleAnimationFiles = compatible.ToList();
                _previewAnimationFile = _compatibleAnimationFiles.FirstOrDefault();
                _animationTitle.Text = $"Compatible animations ({_compatibleAnimationFiles.Count:N0})";
                _animationTitle.ToolTip = _compatibleAnimationFiles.Count == 0
                    ? $"No compatible animations were found for {hrcFile}."
                    : $"Compatible animations for {hrcFile}.";
            } catch (Exception ex) {
                if (request != Interlocked.Read(ref _animationLookupRequest))
                    return;
                _compatibleAnimationFiles = new List<string>();
                _previewAnimationFile = null;
                _animationTitle.Text = "Compatible animations (0)";
                _animationTitle.ToolTip = $"Could not check animation compatibility: {ex.Message}";
            } finally {
                if (request == Interlocked.Read(ref _animationLookupRequest)) {
                    _animationLookupPending = false;
                    RefreshAnimationList();
                    SchedulePreview();
                }
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

        private void SchedulePreview() {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview() {
            if (_animationLookupPending) {
                _preview.ShowMessage("Finding compatible animations…");
                return;
            }
            if (_lvHRCs.SelectedItem is not string hrcFile) {
                _preview.ShowMessage("Select a field model to preview");
                return;
            }
            if (!TryGetScale(out _)) {
                _preview.ShowMessage("Enter a valid positive scale to preview this field model");
                return;
            }

            string? previewAnimation = _previewAnimationFile != null && _compatibleAnimationFiles.Contains(_previewAnimationFile)
                ? _previewAnimationFile
                : _compatibleAnimationFiles.FirstOrDefault();
            string[] previewAnimations = previewAnimation == null ? Array.Empty<string>() : new[] { previewAnimation };
            DataSource source = _source;
            ModelBaseOptions options = GetOptions();
            bool useFirstAnimationFrameAsBindPose = _chkFirstFrameBindPose.IsChecked == true;

            _preview.ShowModel(() => {
                var exporter = new FieldModel(source, options);
                return exporter.BuildScene(
                    hrcFile,
                    previewAnimations,
                    compatibleAnimationsOnly: true,
                    out _,
                    includeAnimations: true,
                    maxAnimationCount: 1,
                    useFirstAnimationFrameAsBindPose: useFirstAnimationFrameAsBindPose
                );
            }, hrcFile, CreateFieldPointLighting(_lightingSettings));
        }

        private static object CreateFieldPointLighting(FieldPreviewLightingSettings settings) {
            double safePositionMultiplier = Math.Clamp(settings.PositionMultiplier, 0.25, 10.0);
            double safeReachMultiplier = Math.Clamp(settings.ReachMultiplier, 1.0, 100.0);
            double safeStrengthMultiplier = Math.Clamp(settings.StrengthMultiplier, 0.1, 4.0);
            double safeAmbientTintMultiplier = Math.Clamp(settings.AmbientTintMultiplier, 0.0, 10.0);
            var pointLights = settings.PointLights.Select(light => new {
                color = light.Color,
                x = light.X,
                y = light.Y,
                z = light.Z,
                intensity = FieldLightBaseIntensity * safeStrengthMultiplier,
                decay = 1.0 / safeReachMultiplier,
            }).ToArray();
            return new {
                ambientColor = settings.AmbientColor,
                ambientIntensity = safeAmbientTintMultiplier,
                // FF7 field light positions use 512 coordinate units per model unit.
                positionScale = FieldLightPositionScale / safePositionMultiplier,
                pointLights,
            };
        }

        private void OpenLightingEditorWindow() {
            if (_lightingEditorWindow is { IsVisible: true }) {
                _lightingEditorWindow.Activate();
                return;
            }

            var editorWindow = new FieldLightingEditorWindow(
                _lightingSettings.Clone(),
                settings => {
                    _lightingSettings = settings.Clone();
                    _preview.UpdateLighting(CreateFieldPointLighting(_lightingSettings));
                }
            );
            if (DesktopUi.MainWindow is { IsVisible: true } owner) {
                editorWindow.Owner = owner;
                editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            } else {
                editorWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            _lightingEditorWindow = editorWindow;
            _lightingEditorWindow.Closed += (_, _) => _lightingEditorWindow = null;
            _lightingEditorWindow.Show();
        }

        private void UpdateAnimationControlState() {
            bool hasModel = _lvHRCs.SelectedItem is string;
            _txtAnimationSearch.IsEnabled = hasModel;
            _lstAnimations.IsEnabled = hasModel && !_animationLookupPending;
        }

        private IEnumerable<string> GetAllAnimationFiles() => _animationFiles;

        private IEnumerable<string> GetSelectedOrAllCompatibleAnimations() {
            var selected = _selectedAnimationFiles.Where(_compatibleAnimationFiles.Contains).ToList();
            return selected.Count > 0 ? selected : _compatibleAnimationFiles;
        }

        private IEnumerable<string> GetSelectedOrAllAnimationFiles() => _selectedAnimationFiles.Count > 0
            ? _animationFiles.Where(_selectedAnimationFiles.Contains).ToList()
            : GetAllAnimationFiles();

        private void PreviewAnimation(string animation) {
            _previewAnimationFile = animation;
            SchedulePreview();
        }

        private bool TryGetAnimationItem(DependencyObject? source, out ListBoxItem? item, out string animation) {
            item = null;
            animation = string.Empty;
            if (source == null)
                return false;

            item = ItemsControl.ContainerFromElement(_lstAnimations, source) as ListBoxItem;
            if (item == null || item.Content is not string file)
                return false;
            animation = file;
            return true;
        }

        private ModelBaseOptions GetOptions() {
            if (!TryGetScale(out float scale))
                throw new InvalidOperationException("Enter a valid positive scale.");
            return new() {
                ConvertSRGBToLinear = _chkSRGB.IsChecked == true,
                SwapWinding = _chkSwapWinding.IsChecked == true,
                BakeVertexColours = _chkBakeColours.IsChecked == true,
                Scale = scale,
            };
        }

        private bool TryGetScale(out float scale) =>
            float.TryParse(_txtScale.Text, out scale) && float.IsFinite(scale) && scale > 0;

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
