// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using Ficedula.FF7.Field;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace CrossSlash {
    using MaterialAlphaMode = SharpGLTF.Materials.AlphaMode;

    public sealed class FieldLocationExport : ExportKind {
        public override string Help =>
@"Usage: CrossSlash FieldLocation [SourceLGPOrFolder] [OutputFile] [FieldCode]
    Exports a field location as a GLB containing its background, walkmesh and FF7 camera.
    For example:
        CrossSlash FieldLocation C:\games\FF7\data\field\flevel.lgp C:\temp\md1stin.glb md1stin";

        public override object Config { get; } = new object();
        public override bool HasGui => true;
        public override string Name => "Field Location Export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            string code = parameters.FirstOrDefault()
                ?? throw new ArgumentException("Choose a field code to export.");
            FieldLocationSceneExporter.BuildScene(source, code).SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new FieldLocationExportWindow(source);
    }

    internal sealed class FieldLocationExportWindow : Window {
        private static readonly Brush DiagnosticLabelBrush = new SolidColorBrush(Color.FromRgb(143, 198, 207));
        private static readonly Brush DiagnosticValueBrush = new SolidColorBrush(Color.FromRgb(220, 203, 137));
        private static readonly Brush DiagnosticSectionBrush = new SolidColorBrush(Color.FromRgb(193, 151, 202));

        private readonly DataSource _source;
        private readonly List<string> _fieldCodes;
        private readonly TextBox _search;
        private readonly ListBox _fieldList;
        private readonly ICollectionView _fieldView;
        private readonly ModelPreviewControl _preview;
        private readonly ModelPreviewWindowHost _previewWindowHost;
        private readonly DispatcherTimer _previewTimer;
        private readonly RichTextBox _cameraDiagnostics;
        private readonly Border _cameraHelpOverlay;
        private string? _previewCode;

        public FieldLocationExportWindow(DataSource source) {
            _source = source;
            _fieldCodes = FindFieldCodes(source);

            _search = DesktopUi.CreateSearchTextBox("Search field locations");
            _fieldView = CollectionViewSource.GetDefaultView(_fieldCodes);
            _fieldView.Filter = item => item is string code &&
                (string.IsNullOrWhiteSpace(_search.Text) || code.Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
            _search.TextChanged += (_, _) => _fieldView.Refresh();

            _fieldList = new ListBox {
                ItemsSource = _fieldView,
                MinHeight = 0,
                Background = DesktopUi.InputBackgroundBrush,
                Foreground = DesktopUi.TextPrimaryBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_fieldList, ScrollBarVisibility.Auto);
            _preview = new ModelPreviewControl {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            _previewWindowHost = new ModelPreviewWindowHost(this, _preview);
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _previewTimer.Tick += (_, _) => {
                _previewTimer.Stop();
                RefreshPreview();
            };
            Closed += (_, _) => {
                _previewTimer.Stop();
                _previewWindowHost.Close();
            };
            _fieldList.SelectionChanged += (_, _) => SchedulePreview();

            var left = new Grid { Margin = new Thickness(0, 0, 12, 0) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.Children.Add(DesktopUi.CreateSectionTitle($"Field locations ({_fieldCodes.Count:N0})"));
            var searchField = DesktopUi.CreateSearchField("Search field locations…", _search);
            Grid.SetRow(searchField, 1);
            left.Children.Add(searchField);
            Grid.SetRow(_fieldList, 2);
            left.Children.Add(_fieldList);

            var previewPage = new Grid { Margin = new Thickness(12, 10, 12, 0) };
            var previewHost = new Grid { MinHeight = 280, ClipToBounds = true };
            previewHost.Children.Add(_preview);
            previewPage.Children.Add(previewHost);

            var cameraDiagnosticsPage = CreateCameraDiagnosticsPage(out _cameraDiagnostics, out _cameraHelpOverlay, out Button cameraHelpButton);
            var previewTabs = new TabControl { Margin = new Thickness(12, 0, 0, 0) };
            previewTabs.Items.Add(new TabItem {
                Header = "Preview",
                Content = previewPage,
            });
            previewTabs.Items.Add(new TabItem {
                Header = "Camera & alignment",
                Content = cameraDiagnosticsPage,
            });
            previewTabs.PreviewMouseDown += (_, e) => {
                if (_cameraHelpOverlay.Visibility != Visibility.Visible)
                    return;
                var clickedElement = e.OriginalSource as DependencyObject;
                if (IsWithinElement(clickedElement, _cameraHelpOverlay) || IsWithinElement(clickedElement, cameraHelpButton))
                    return;
                _cameraHelpOverlay.Visibility = Visibility.Collapsed;
            };

            var columns = new Grid { Margin = new Thickness(10, 10, 10, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.24, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.76, GridUnitType.Star) });
            columns.Children.Add(left);
            Grid.SetColumn(previewTabs, 1);
            columns.Children.Add(previewTabs);

            var selectedExport = DesktopUi.CreateButton("Export selected", primary: true);
            selectedExport.Click += async (_, _) => await ExportSelectedAsync();
            var exportAll = DesktopUi.CreateButton("Export all field locations");
            exportAll.Click += async (_, _) => await ExportAllAsync();

            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(columns);
            var exportOptions = new StackPanel();
            exportOptions.Children.Add(new Border {
                Height = 1,
                Background = DesktopUi.ControlBorderBrush,
                Margin = new Thickness(10, 0, 10, 0),
            });
            var view = new Border {
                Background = DesktopUi.SurfaceBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Child = panel,
            };
            exportOptions.Children.Add(DesktopUi.CreateActionRow(selectedExport, exportAll));
            Grid.SetRow(exportOptions, 1);
            panel.Children.Add(exportOptions);
            var content = new Grid();
            content.Children.Add(view);
            if (_fieldCodes.Count == 0) {
                _preview.ShowMessage("No field locations were found. Choose the game's flevel.lgp archive or an extracted field folder.");
            }

            DesktopUi.ConfigureWindow(this, content, width: 1120, height: 820, scrollContent: false);
            _preview.SetFieldLocationWireframe(true);
            _preview.SetFieldLocationWireframeToggleVisible(true);
            if (_fieldList.Items.Count > 0)
                _fieldList.SelectedIndex = 0;
            else
                _preview.ShowMessage("Choose the game's flevel.lgp archive or an extracted field folder.");
        }

        private static List<string> FindFieldCodes(DataSource source) {
            try {
                if (source.Exists("maplist")) {
                    using Stream mapListStream = source.Open("maplist");
                    var mapList = new MapList(mapListStream);
                    return mapList.Items
                        .Where(source.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            } catch {
                // Fall through to filename discovery for extracted or partially copied sources.
            }

            return source.AllFiles
                .Where(file => Path.GetExtension(file).Length == 0 || Path.GetExtension(file).Equals(".map", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetExtension(file).Equals(".map", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(file, null)!
                    : file)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static FrameworkElement CreateCameraDiagnosticsPage(out RichTextBox diagnostics, out Border helpOverlay, out Button helpButton) {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 0) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock {
                Text = "Values used for this field",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesktopUi.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            helpButton = DesktopUi.CreateButton("?");
            helpButton.Width = 30;
            helpButton.Height = 30;
            helpButton.Padding = new Thickness(0);
            helpButton.Margin = new Thickness(0, 1, 6, 1);
            helpButton.HorizontalAlignment = HorizontalAlignment.Right;
            helpButton.VerticalAlignment = VerticalAlignment.Center;
            helpButton.ToolTip = "What these values mean";
            Grid.SetColumn(helpButton, 1);
            header.Children.Add(helpButton);
            page.Children.Add(header);

            var dataArea = new Grid();
            diagnostics = new RichTextBox {
                IsReadOnly = true,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = DesktopUi.TextPrimaryBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            diagnostics.Document.PagePadding = new Thickness(2);
            SetDiagnosticsText(diagnostics, "Select a field location to load its camera, background and walkmesh values.");
            dataArea.Children.Add(diagnostics);

            var helpContent = new StackPanel();
            helpContent.Children.Add(new TextBlock {
                Text = "How this view is assembled",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesktopUi.TextPrimaryBrush,
                Margin = new Thickness(0, 0, 0, 8),
            });
            helpContent.Children.Add(CreateHelpSection(
                "Camera and coordinates",
                "The first camera matrix in the field file supplies the camera position and orientation. FF7 coordinates are converted to GLB coordinates as (X, Y, Z) → (X, Z, −Y); the camera position is multiplied by 4096 before conversion. The converted forward and up axes are normalized, then right and up are rebuilt to form a perpendicular camera frame."
            ));
            helpContent.Children.Add(CreateHelpSection(
                "Zoom and field of view",
                "The field zoom value controls the base vertical field of view using FF7’s 240-pixel reference. The assembled background’s pixel height adjusts that FOV to match the whole image, and its width-to-height ratio sets the camera aspect. The GLB contains this perspective camera, with a near clip of 1 and a far clip extended to include the background plane."
            ));
            helpContent.Children.Add(CreateHelpSection(
                "Background tiles",
                "The background is rebuilt from its four sprite layers, texture pages, palettes, and blend modes. Initial layer states are read from field init scripts so inactive tiles are omitted. The renderer uses 16-pixel tiles for layers 0–1 and 32-pixel tiles for layers 2–3. Tile destinations determine the composite image bounds, including its offset from (0, 0)."
            ));
            helpContent.Children.Add(CreateHelpSection(
                "Background and walkmesh alignment",
                "The walkmesh uses the same coordinate conversion as the camera. The background image is scaled from the base FOV and placed on a plane beyond the farthest walkmesh point along the camera’s forward direction. Its image offset is applied along the camera’s right and up axes, keeping the reconstructed pixels aligned with the field camera."
            ));
            helpContent.Children.Add(CreateHelpSection(
                "Preview and export",
                "The data dump shows each input and intermediate value used by these calculations, followed by the formulas. Preview zoom and left-drag pan adjust the view projection temporarily; they do not change the exported camera. Right-drag orbits for inspection, and double-click resets the preview."
            ));
            var overlay = new Border {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                MaxWidth = 560,
                MaxHeight = 480,
                Padding = new Thickness(12, 10, 12, 10),
                Background = new SolidColorBrush(Color.FromArgb(232, 29, 36, 49)),
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                Child = new ScrollViewer {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = helpContent,
                },
            };
            helpOverlay = overlay;
            Panel.SetZIndex(overlay, 1);
            dataArea.Children.Add(overlay);
            helpButton.Click += (_, _) => overlay.Visibility =
                overlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetRow(dataArea, 1);
            page.Children.Add(dataArea);
            return page;
        }

        private static StackPanel CreateHelpSection(string title, string description) {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            section.Children.Add(new TextBlock {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = DiagnosticLabelBrush,
                Margin = new Thickness(0, 0, 0, 2),
            });
            section.Children.Add(new TextBlock {
                Text = description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = DesktopUi.TextSecondaryBrush,
                LineHeight = 15,
            });
            return section;
        }

        private static bool IsWithinElement(DependencyObject? element, DependencyObject ancestor) {
            while (element != null) {
                if (ReferenceEquals(element, ancestor))
                    return true;
                DependencyObject? parent;
                try {
                    parent = VisualTreeHelper.GetParent(element);
                } catch (InvalidOperationException) {
                    parent = null;
                }
                element = parent ?? LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private static void SetDiagnosticsText(RichTextBox target, string text) {
            var document = new FlowDocument { PagePadding = new Thickness(2) };
            foreach (string line in text.Replace("\r", string.Empty).Split('\n')) {
                var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };
                if (string.IsNullOrEmpty(line)) {
                    paragraph.LineHeight = 8;
                    paragraph.Inlines.Add(new Run(" "));
                } else {
                    int separator = line.IndexOf(": ", StringComparison.Ordinal);
                    if (separator >= 0) {
                        paragraph.Inlines.Add(new Run(line[..separator]) {
                            FontFamily = new FontFamily("Segoe UI"),
                            FontWeight = FontWeights.SemiBold,
                            Foreground = DiagnosticLabelBrush,
                        });
                        paragraph.Inlines.Add(new Run(": ") {
                            FontFamily = new FontFamily("Segoe UI"),
                            Foreground = DiagnosticLabelBrush,
                        });
                        paragraph.Inlines.Add(new Run(line[(separator + 2)..]) {
                            FontFamily = new FontFamily("Consolas"),
                            Foreground = DiagnosticValueBrush,
                        });
                    } else if (line == line.ToUpperInvariant()) {
                        paragraph.Margin = new Thickness(0, 7, 0, 2);
                        paragraph.LineHeight = 19;
                        paragraph.Inlines.Add(new Run(line) {
                            FontFamily = new FontFamily("Segoe UI"),
                            FontWeight = FontWeights.Bold,
                            Foreground = DiagnosticSectionBrush,
                        });
                    } else {
                        paragraph.Inlines.Add(new Run(line) {
                            FontFamily = new FontFamily("Consolas"),
                            Foreground = DesktopUi.TextPrimaryBrush,
                        });
                    }
                }
                document.Blocks.Add(paragraph);
            }
            target.Document = document;
            target.CaretPosition = target.Document.ContentStart;
            target.ScrollToHome();
        }

        private void SchedulePreview() {
            if (_fieldList.SelectedItem is not string code) {
                _previewCode = null;
                _preview.ShowMessage("Select a field location to preview it.");
                SetDiagnosticsText(_cameraDiagnostics, "Select a field location to load its camera, background and walkmesh values.");
                return;
            }
            _previewCode = code;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview() {
            string? code = _previewCode;
            if (code == null)
                return;
            SetDiagnosticsText(_cameraDiagnostics, $"Loading camera, background and walkmesh values for {code}…");
            _preview.ShowModel(
                () => {
                    try {
                        ModelRoot model = FieldLocationSceneExporter.BuildScene(_source, code, out string diagnostics);
                        Dispatcher.BeginInvoke(new Action(() => {
                            if (_previewCode == code)
                                SetDiagnosticsText(_cameraDiagnostics, diagnostics);
                        }));
                        return model;
                    } catch (Exception ex) {
                        Dispatcher.BeginInvoke(new Action(() => {
                            if (_previewCode == code)
                                SetDiagnosticsText(_cameraDiagnostics, $"Could not calculate diagnostics for {code}:\n{ex.Message}");
                        }));
                        throw;
                    }
                },
                code,
                cameraZoom: 1,
                cameraMode: "field",
                hideControls: true
            );
        }

        private async Task ExportSelectedAsync() {
            if (_fieldList.SelectedItem is not string code) {
                DesktopUi.ShowError(this, "Export field location", "Select a field location first.");
                return;
            }

            string? outputFile = ChooseGlbPath();
            if (outputFile == null)
                return;

            try {
                await Task.Run(() => FieldLocationSceneExporter.BuildScene(_source, code).SaveGLB(outputFile));
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Export complete", $"Exported {code} with its background, walkmesh and camera to:\n{outputFile}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Field location export failed", ex.Message);
            }
        }

        private async Task ExportAllAsync() {
            if (_fieldCodes.Count == 0) {
                DesktopUi.ShowError(this, "Export field locations", "No field locations are available in this source.");
                return;
            }

            string? outputFolder = ChooseOutputFolder();
            if (outputFolder == null)
                return;

            try {
                var result = await BatchExportRunner.RunAsync(
                    this,
                    "Exporting field locations",
                    _fieldCodes.Count,
                    (progress, cancellationToken) => {
                        int completed = 0;
                        int exported = 0;
                        var failures = new List<string>();
                        foreach (string code in _fieldCodes) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            try {
                                FieldLocationSceneExporter.BuildScene(_source, code)
                                    .SaveGLB(Path.Combine(outputFolder, code + ".glb"));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{code}: {ex.Message}");
                            }
                            completed++;
                            progress.Report(new BatchProgressUpdate(completed, _fieldCodes.Count, code));
                        }
                        return (exported, failures, cancelled: cancellationToken.IsCancellationRequested);
                    }
                );

                string summary = result.cancelled
                    ? $"Export cancelled after {result.exported:N0} field locations."
                    : $"Exported {result.exported:N0} field locations to:\n{outputFolder}";
                if (result.failures.Count > 0) {
                    summary += $"\n\nFailed: {result.failures.Count:N0}\n" + string.Join("\n", result.failures.Take(8));
                    if (result.failures.Count > 8)
                        summary += "\n…";
                }
                if (result.failures.Count > 0)
                    DesktopUi.ShowError(this, "Field location export finished", summary);
                else
                    DesktopUi.ShowInfo(this, "Field location export finished", summary);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Field location export failed", ex.Message);
            }
        }

        private string? ChooseOutputFolder() {
            var preferences = PreferencesStore.Load();
            string? path = NativeFilePicker.OpenFolder("Choose an export folder", preferences.LastOutputDirectory, this);
            if (path != null)
                PreferencesStore.RememberOutputDirectory(path);
            return path;
        }

        private string? ChooseGlbPath() {
            var preferences = PreferencesStore.Load();
            return NativeFilePicker.SaveGlb(preferences.LastOutputDirectory, this);
        }
    }

    internal static class FieldLocationSceneExporter {
        private const int MaxBackgroundDimension = 8192;
        private const float WalkmeshLineWidth = 3f;
        private static readonly Vector4 WalkmeshColor = new(0.118f, 0.161f, 0.231f, 0.5f);
        private static readonly Vector4 EdgeColor = new(1f, 1f, 1f, 1f);
        private static readonly Vector4 BlockedEdgeColor = new(1f, 0.733f, 0.4f, 1f);
        private static readonly Vector4 GatewayColor = new(1f, 0.2f, 0.2f, 0.9f);

        private sealed record Tile(Sprite Sprite, int Layer);
        private sealed class BackgroundRenderStats {
            public required int[] SourceTilesPerLayer { get; init; }
            public required int[] OrderedTilesPerLayer { get; init; }
            public required int[] DrawnTilesPerLayer { get; init; }
            public int SkippedInactiveTiles { get; set; }
            public int MissingTexturePages { get; set; }
            public int InvalidPaletteTiles { get; set; }
            public int DimensionSpritesForBounds { get; set; }
            public int BlendedTiles { get; set; }
            public required int[] BlendModeCounts { get; init; }
            public required HashSet<int> UsedTexturePages { get; init; }
            public required HashSet<int> UsedPalettes { get; init; }
            public required HashSet<short> UsedTextureDepths { get; init; }
            public int InitialStateParameterCount { get; init; }
        }
        private sealed record BackgroundImage(byte[] Png, int Width, int Height, int MinX, int MinY, BackgroundRenderStats Stats);
        private sealed record BackgroundPlaneMetrics(float WalkmeshSpan, float BaseDistance, float FarthestWalkmeshDepth, float SafetyMargin, float Distance, bool UsedFallback);

        public static ModelRoot BuildScene(DataSource source, string fieldCode) => BuildScene(source, fieldCode, out _);

        public static ModelRoot BuildScene(DataSource source, string fieldCode, out string diagnostics) {
            using Stream fieldStream = source.Open(fieldCode);
            var field = new FieldFile(fieldStream);
            Background background = field.GetBackground();
            Palettes palettes = field.GetPalettes();
            Walkmesh walkmesh = field.GetWalkmesh();
            var cameras = field.GetCameraMatrices().ToList();
            if (cameras.Count == 0)
                throw new InvalidDataException($"Field {fieldCode} does not contain a camera.");

            CameraMatrix cameraData = cameras[0];
            CameraFrame cameraFrame = GetCameraFrame(cameraData);
            var tiles = GetTilesInRenderOrder(background);
            IReadOnlyDictionary<int, int> initialParamStates = GetInitialBackgroundStates(field);
            BackgroundImage backgroundImage = RenderBackground(background, palettes, tiles, initialParamStates);
            var builder = new SceneBuilder();
            BackgroundPlaneMetrics planeMetrics = new(0, 0, 0, 0, 0, false);
            float pixelSize = 0;

            if (backgroundImage.Width > 0 && backgroundImage.Height > 0) {
                planeMetrics = GetBackgroundPlaneMetrics(walkmesh, cameraFrame);

                float baseFov = GetCameraFov(cameraData.Zoom);
                pixelSize = 2f * planeMetrics.Distance * MathF.Tan(baseFov / 2f) / 240f;
                AddBackgroundMesh(builder, backgroundImage, cameraFrame, planeMetrics.Distance, pixelSize);
            }

            TriggersAndGateways triggerData = field.GetTriggersAndGateways();
            if (walkmesh.Triangles.Count > 0)
                AddWalkmeshMeshes(builder, walkmesh, triggerData);

            ModelRoot model = builder.ToGltf2();
            Scene scene = model.LogicalScenes.FirstOrDefault() ?? model.UseScene("Field location");
            model.DefaultScene = scene;
            var camera = model.CreateCamera("FF7 Field Camera");
            float cameraAspect = backgroundImage.Height > 0
                ? backgroundImage.Width / (float)backgroundImage.Height
                : 4f / 3f;
            float backgroundFov = GetBackgroundFov(cameraData.Zoom, backgroundImage.Height);
            float cameraFar = MathF.Max(32000f, planeMetrics.Distance + MathF.Max(1000f, planeMetrics.Distance * 0.01f));
            camera.SetPerspectiveMode(cameraAspect, backgroundFov, 1f, cameraFar);
            var cameraNode = scene.CreateNode("FF7 Field Camera");
            cameraNode.Camera = camera;
            cameraNode.WorldMatrix = Matrix4x4.CreateWorld(cameraFrame.Eye, cameraFrame.Forward, cameraFrame.Up);

            diagnostics = CreateDiagnostics(
                fieldCode, cameras.Count, background, palettes, walkmesh, cameraData, cameraFrame,
                backgroundImage, planeMetrics, pixelSize, cameraAspect, backgroundFov,
                cameraFar, triggerData, initialParamStates, tiles.Count
            );

            return model;
        }

        private static List<Tile> GetTilesInRenderOrder(Background background) {
            var tiles = background.Layers
                .SelectMany((layer, index) => layer.Select(sprite => new Tile(sprite, index)))
                .Where(tile => Math.Abs((int)tile.Sprite.DestX) < 1024 && Math.Abs((int)tile.Sprite.DestY) < 1024)
                .Select(tile => new {
                    tile.Sprite,
                    tile.Layer,
                    Key = tile.Layer switch {
                        0 => 4095,
                        1 => (int)(ushort)tile.Sprite.Flags,
                        2 => 4096,
                        3 => 0,
                        _ => (int)(ushort)tile.Sprite.Flags,
                    },
                    SubKey = unchecked((uint)tile.Sprite.ZParam),
                })
                .OrderByDescending(tile => tile.Key)
                .ThenByDescending(tile => tile.SubKey)
                .Select(tile => new Tile(tile.Sprite, tile.Layer))
                .ToList();
            return tiles;
        }

        private static BackgroundImage RenderBackground(
            Background background,
            Palettes palettes,
            List<Tile> orderedTiles,
            IReadOnlyDictionary<int, int> initialParamStates) {
            int[] sourceTilesPerLayer = background.Layers.Select(layer => layer.Count()).ToArray();
            int[] orderedTilesPerLayer = CountTilesByLayer(orderedTiles);
            int[] drawnTilesPerLayer = new int[4];
            var stats = new BackgroundRenderStats {
                SourceTilesPerLayer = sourceTilesPerLayer,
                OrderedTilesPerLayer = orderedTilesPerLayer,
                DrawnTilesPerLayer = drawnTilesPerLayer,
                UsedTexturePages = new HashSet<int>(),
                UsedPalettes = new HashSet<int>(),
                UsedTextureDepths = new HashSet<short>(),
                BlendModeCounts = new int[5],
                InitialStateParameterCount = initialParamStates.Count,
            };
            var dimensionSprites = background.Layers
                .SelectMany((layer, index) => layer.Select(sprite => new Tile(sprite, index)))
                .Where(tile => tile.Sprite.DestX < 9999 && tile.Sprite.DestY < 9999)
                .ToList();
            stats.DimensionSpritesForBounds = dimensionSprites.Count;
            if (dimensionSprites.Count == 0)
                return new BackgroundImage(Array.Empty<byte>(), 0, 0, 0, 0, stats);

            int minX = dimensionSprites.Min(tile => (int)tile.Sprite.DestX);
            int minY = dimensionSprites.Min(tile => (int)tile.Sprite.DestY);
            int maxX = dimensionSprites.Max(tile => (int)tile.Sprite.DestX + TileSize(tile.Layer));
            int maxY = dimensionSprites.Max(tile => (int)tile.Sprite.DestY + TileSize(tile.Layer));
            int width = maxX - minX;
            int height = maxY - minY;
            if (width <= 0 || height <= 0)
                return new BackgroundImage(Array.Empty<byte>(), 0, 0, minX, minY, stats);
            if (width > MaxBackgroundDimension || height > MaxBackgroundDimension)
                throw new InvalidDataException($"Background dimensions {width}×{height} are too large to export safely.");

            byte[] rgba = new byte[checked(width * height * 4)];
            for (int alphaOffset = 3; alphaOffset < rgba.Length; alphaOffset += 4)
                rgba[alphaOffset] = 255;

            foreach (Tile tile in orderedTiles) {
                Sprite sprite = tile.Sprite;
                if (tile.Layer > 0 && sprite.Param != 0) {
                    int activeStates = initialParamStates.TryGetValue(sprite.Param, out int mask) ? mask : 0;
                    if ((sprite.State & activeStates) == 0) {
                        stats.SkippedInactiveTiles++;
                        continue;
                    }
                }

                bool useBlending = tile.Layer != 0 && sprite.Blending != 0;
                int textureId = useBlending ? sprite.TextureID2 : sprite.PrimaryTextureID;
                int sourceX = useBlending ? sprite.SrcX2 : sprite.PrimarySrcX;
                int sourceY = useBlending ? sprite.SrcY2 : sprite.PrimarySrcY;
                if (!background.Pages.TryGetValue(textureId, out TexturePage? page)) {
                    stats.MissingTexturePages++;
                    continue;
                }
                int paletteId = (ushort)sprite.PaletteID;
                bool directColor = page.Depth == 2;
                if (!directColor && (paletteId >= palettes.PaletteData.Count || paletteId >= background.Palettes.Count)) {
                    stats.InvalidPaletteTiles++;
                    continue;
                }

                drawnTilesPerLayer[tile.Layer]++;
                if (useBlending) {
                    stats.BlendedTiles++;
                    int blendModeIndex = sprite.TypeTrans <= 3 ? sprite.TypeTrans : 4;
                    stats.BlendModeCounts[blendModeIndex]++;
                }
                stats.UsedTexturePages.Add(textureId);
                stats.UsedTextureDepths.Add(page.Depth);
                if (!directColor)
                    stats.UsedPalettes.Add(paletteId);

                ushort[]? palette = directColor ? null : palettes.PaletteData[paletteId];
                bool paletteZeroTransparent = !directColor && background.Palettes[paletteId].Colours[0] == 0;
                int tileSize = TileSize(tile.Layer);
                int dstX = sprite.DestX - minX;
                int dstY = sprite.DestY - minY;
                byte blendType = sprite.TypeTrans;

                for (int y = 0; y < tileSize; y++) {
                    int srcY = sourceY + y;
                    int outputY = dstY + y;
                    if ((uint)srcY >= 256 || (uint)outputY >= (uint)height)
                        continue;
                    byte[]? row = directColor ? null : page.Data[srcY];
                    ushort[]? directRow = directColor ? page.DirectData[srcY] : null;
                    for (int x = 0; x < tileSize; x++) {
                        int srcX = sourceX + x;
                        int outputX = dstX + x;
                        if ((uint)srcX >= 256 || (uint)outputX >= (uint)width)
                            continue;
                        int pixel = (outputY * width + outputX) * 4;
                        int r;
                        int g;
                        int b;
                        if (directColor) {
                            ushort packed = directRow![srcX];
                            if (packed == 0)
                                continue;
                            r = ExpandFiveBit((packed >> 11) & 0x1f);
                            g = ExpandFiveBit((packed >> 6) & 0x1f);
                            b = ExpandFiveBit(packed & 0x1f);
                        } else {
                            int paletteIndex = row![srcX];
                            ushort packed = palette![paletteIndex];
                            if (paletteIndex == 0 && paletteZeroTransparent)
                                continue;
                            r = ExpandFiveBit(packed & 0x1f);
                            g = ExpandFiveBit((packed >> 5) & 0x1f);
                            b = ExpandFiveBit((packed >> 10) & 0x1f);
                        }

                        if (useBlending) {
                            switch (blendType) {
                                case 0:
                                    rgba[pixel] = (byte)((rgba[pixel] + r) >> 1);
                                    rgba[pixel + 1] = (byte)((rgba[pixel + 1] + g) >> 1);
                                    rgba[pixel + 2] = (byte)((rgba[pixel + 2] + b) >> 1);
                                    break;
                                case 1:
                                    rgba[pixel] = (byte)Math.Min(255, rgba[pixel] + r);
                                    rgba[pixel + 1] = (byte)Math.Min(255, rgba[pixel + 1] + g);
                                    rgba[pixel + 2] = (byte)Math.Min(255, rgba[pixel + 2] + b);
                                    break;
                                case 2:
                                    rgba[pixel] = (byte)Math.Max(0, rgba[pixel] - r);
                                    rgba[pixel + 1] = (byte)Math.Max(0, rgba[pixel + 1] - g);
                                    rgba[pixel + 2] = (byte)Math.Max(0, rgba[pixel + 2] - b);
                                    break;
                                case 3:
                                    rgba[pixel] = (byte)Math.Min(255, rgba[pixel] + (r >> 2));
                                    rgba[pixel + 1] = (byte)Math.Min(255, rgba[pixel + 1] + (g >> 2));
                                    rgba[pixel + 2] = (byte)Math.Min(255, rgba[pixel + 2] + (b >> 2));
                                    break;
                                default:
                                    rgba[pixel] = (byte)r;
                                    rgba[pixel + 1] = (byte)g;
                                    rgba[pixel + 2] = (byte)b;
                                    break;
                            }
                        } else {
                            rgba[pixel] = (byte)r;
                            rgba[pixel + 1] = (byte)g;
                            rgba[pixel + 2] = (byte)b;
                        }
                    }
                }
            }

            using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
            Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return new BackgroundImage(encoded.ToArray(), width, height, minX, minY, stats);
        }

        private static int[] CountTilesByLayer(IEnumerable<Tile> tiles) {
            int[] counts = new int[4];
            foreach (Tile tile in tiles) {
                if ((uint)tile.Layer < (uint)counts.Length)
                    counts[tile.Layer]++;
            }
            return counts;
        }

        private static IReadOnlyDictionary<int, int> GetInitialBackgroundStates(FieldFile field) {
            var result = new Dictionary<int, int>();
            DialogEvent dialog = field.GetDialogEvent();
            byte[] bytecode = dialog.ScriptBytecode;

            foreach (Entity entity in dialog.Entities) {
                if (entity.Scripts.Count == 0)
                    continue;
                int initStart = entity.Scripts[0];
                if (initStart < 0 || initStart >= bytecode.Length)
                    continue;
                int[] offsets = entity.Scripts
                    .Where(offset => offset >= 0 && offset < bytecode.Length)
                    .Distinct()
                    .OrderBy(offset => offset)
                    .ToArray();
                int nextOffset = offsets.FirstOrDefault(offset => offset > initStart, bytecode.Length);
                if (nextOffset <= initStart)
                    continue;

                byte[] initScript = bytecode[initStart..nextOffset];
                try {
                    foreach (DisassembledOp operation in VMOpcodes.Disassemble(initScript, 0)) {
                        if (operation.Opcode == 0x00)
                            break;
                        if (operation.Opcode != 0xe0 || operation.Arguments.Count < 3 || operation.Arguments[0].value != 0)
                            continue;
                        int param = operation.Arguments[1].value;
                        int stateBit = operation.Arguments[2].value;
                        if ((uint)stateBit >= 8)
                            continue;
                        result[param] = result.GetValueOrDefault(param) | (1 << stateBit);
                    }
                } catch {
                    // A malformed unrelated script should not prevent the location export.
                }
            }
            return result;
        }

        private static void AddBackgroundMesh(SceneBuilder builder, BackgroundImage image, CameraFrame camera, float distance, float pixelSize) {
            float planeWidth = image.Width * pixelSize;
            float planeHeight = image.Height * pixelSize;
            float centerX = (image.MinX + image.Width / 2f) * pixelSize;
            float centerY = -(image.MinY + image.Height / 2f) * pixelSize;
            Vector3 right = camera.Right;
            Vector3 center = camera.Eye
                + camera.Forward * distance
                + right * centerX
                + camera.Up * centerY;
            Vector3 halfRight = right * (planeWidth / 2f);
            Vector3 halfUp = camera.Up * (planeHeight / 2f);
            Vector3 bottomLeft = center - halfRight - halfUp;
            Vector3 bottomRight = center + halfRight - halfUp;
            Vector3 topRight = center + halfRight + halfUp;
            Vector3 topLeft = center - halfRight + halfUp;
            Vector3 normal = -camera.Forward;

            var material = new MaterialBuilder("FF7 Field Background")
                .WithUnlitShader()
                .WithDoubleSide(true)
                .WithAlpha(MaterialAlphaMode.OPAQUE);
            material.WithChannelImage(KnownChannel.BaseColor, new MemoryImage(image.Png));
            material.GetChannel(KnownChannel.BaseColor).Texture.WithSampler(
                TextureWrapMode.CLAMP_TO_EDGE,
                TextureWrapMode.CLAMP_TO_EDGE,
                TextureMipMapFilter.NEAREST,
                TextureInterpolationFilter.NEAREST
            );

            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>("FF7 Field Background");
            var primitive = mesh.UsePrimitive(material);
            var a = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>(
                new VertexPositionNormal(bottomLeft, normal), new VertexTexture1(new Vector2(0, 1)));
            var b = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>(
                new VertexPositionNormal(bottomRight, normal), new VertexTexture1(new Vector2(1, 1)));
            var c = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>(
                new VertexPositionNormal(topRight, normal), new VertexTexture1(new Vector2(1, 0)));
            var d = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>(
                new VertexPositionNormal(topLeft, normal), new VertexTexture1(new Vector2(0, 0)));
            primitive.AddTriangle(a, b, c);
            primitive.AddTriangle(a, c, d);
            builder.AddRigidMesh(mesh, new NodeBuilder("Background plane"));
        }

        private static void AddWalkmeshMeshes(SceneBuilder builder, Walkmesh walkmesh, TriggersAndGateways triggerData) {
            var faceMaterial = new MaterialBuilder("FF7 Walkmesh Surface")
                .WithUnlitShader()
                .WithDoubleSide(true)
                .WithAlpha(MaterialAlphaMode.BLEND);
            var faceMesh = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexNoSkin>("FF7 Walkmesh");
            var facePrimitive = faceMesh.UsePrimitive(faceMaterial);

            var edgeMaterial = new MaterialBuilder("FF7 Walkmesh Edges")
                .WithUnlitShader()
                .WithDoubleSide(true)
                .WithAlpha(MaterialAlphaMode.BLEND);
            var edgeMesh = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexNoSkin>("FF7 Walkmesh Edges");
            var edgePrimitive = edgeMesh.UsePrimitive(edgeMaterial);

            foreach (WalkmeshTriangle triangle in walkmesh.Triangles) {
                Vector3 a = ToViewerCoordinates(triangle.V0);
                Vector3 b = ToViewerCoordinates(triangle.V1);
                Vector3 c = ToViewerCoordinates(triangle.V2);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-8f)
                    continue;
                Vector3 normal = Vector3.Normalize(cross);
                facePrimitive.AddTriangle(
                    WalkmeshVertex(a, normal, WalkmeshColor),
                    WalkmeshVertex(b, normal, WalkmeshColor),
                    WalkmeshVertex(c, normal, WalkmeshColor)
                );

                AddRibbon(edgePrimitive, a, b, normal, triangle.V01Tri == null ? BlockedEdgeColor : EdgeColor, WalkmeshLineWidth);
                AddRibbon(edgePrimitive, b, c, normal, triangle.V12Tri == null ? BlockedEdgeColor : EdgeColor, WalkmeshLineWidth);
                AddRibbon(edgePrimitive, c, a, normal, triangle.V20Tri == null ? BlockedEdgeColor : EdgeColor, WalkmeshLineWidth);
            }

            builder.AddRigidMesh(faceMesh, new NodeBuilder("Walkmesh surfaces"));
            builder.AddRigidMesh(edgeMesh, new NodeBuilder("Walkmesh edges"));

            var gateways = triggerData.Gateways
                .Where(gateway => gateway.Destination.DestinationFieldID != 0)
                .ToList();
            if (gateways.Count == 0)
                return;

            var gatewayMaterial = new MaterialBuilder("FF7 Gateways")
                .WithUnlitShader()
                .WithDoubleSide(true)
                .WithAlpha(MaterialAlphaMode.BLEND);
            var gatewayMesh = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexNoSkin>("FF7 Gateways");
            var gatewayPrimitive = gatewayMesh.UsePrimitive(gatewayMaterial);
            foreach (Gateway gateway in gateways) {
                Vector3 a = ToViewerCoordinates(gateway.V0);
                Vector3 b = ToViewerCoordinates(gateway.V1);
                AddRibbon(gatewayPrimitive, a, b, Vector3.UnitY, GatewayColor, WalkmeshLineWidth * 2f);
            }
            builder.AddRigidMesh(gatewayMesh, new NodeBuilder("Gateways"));
        }

        private static VertexBuilder<VertexPositionNormal, VertexColor1, VertexNoSkin> WalkmeshVertex(Vector3 position, Vector3 normal, Vector4 color) =>
            new(new VertexPositionNormal(position, normal), new VertexColor1(color));

        private static void AddRibbon(
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1, VertexNoSkin> primitive,
            Vector3 start,
            Vector3 end,
            Vector3 normal,
            Vector4 color,
            float width) {
            Vector3 direction = end - start;
            if (direction.LengthSquared() < 1e-8f)
                return;
            direction = Vector3.Normalize(direction);
            normal = normal.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(normal);
            Vector3 side = Vector3.Cross(normal, direction);
            if (side.LengthSquared() < 1e-8f)
                side = Vector3.Cross(Vector3.UnitY, direction);
            if (side.LengthSquared() < 1e-8f)
                return;
            side = Vector3.Normalize(side) * (width / 2f);

            Vector3 a = start - side;
            Vector3 b = end - side;
            Vector3 c = end + side;
            Vector3 d = start + side;
            primitive.AddTriangle(WalkmeshVertex(a, normal, color), WalkmeshVertex(b, normal, color), WalkmeshVertex(c, normal, color));
            primitive.AddTriangle(WalkmeshVertex(a, normal, color), WalkmeshVertex(c, normal, color), WalkmeshVertex(d, normal, color));
        }

        private static float GetWalkmeshExtent(Walkmesh walkmesh, bool useY) {
            if (walkmesh.Triangles.Count == 0)
                return 0;
            IEnumerable<int> values = walkmesh.Triangles
                .SelectMany(triangle => triangle.AllVerts())
                .Select(vertex => (int)(useY ? vertex.Y : vertex.X));
            return values.Max() - values.Min();
        }

        private static BackgroundPlaneMetrics GetBackgroundPlaneMetrics(Walkmesh walkmesh, CameraFrame camera) {
            float span = MathF.Max(
                GetWalkmeshExtent(walkmesh, useY: false),
                GetWalkmeshExtent(walkmesh, useY: true)
            );
            float baseDistance = span * 2f;
            float planeDistance = baseDistance;
            bool usedFallback = false;
            if (!float.IsFinite(planeDistance) || planeDistance < 1f)
            {
                planeDistance = 5000f;
                usedFallback = true;
            }

            float farthestWalkmeshDepth = walkmesh.Triangles
                .SelectMany(triangle => triangle.AllVerts())
                .Select(vertex => Vector3.Dot(ToViewerCoordinates(vertex) - camera.Eye, camera.Forward))
                .Where(float.IsFinite)
                .DefaultIfEmpty(0f)
                .Max();
            float safetyMargin = 0;
            if (farthestWalkmeshDepth > 0f) {
                safetyMargin = MathF.Max(100f, span * 0.05f);
                planeDistance = MathF.Max(planeDistance, farthestWalkmeshDepth + safetyMargin);
            }
            return new BackgroundPlaneMetrics(span, baseDistance, farthestWalkmeshDepth, safetyMargin, planeDistance, usedFallback);
        }

        private sealed record CameraFrame(Vector3 Eye, Vector3 Forward, Vector3 Up, Vector3 Right);

        private static CameraFrame GetCameraFrame(CameraMatrix camera) {
            Vector3 eye = ToViewerCoordinates(camera.CameraPosition * 4096f);
            Vector3 forward = ToViewerCoordinates(camera.Forwards);
            Vector3 up = ToViewerCoordinates(camera.Up);
            if (forward.LengthSquared() < 1e-8f || up.LengthSquared() < 1e-8f)
                throw new InvalidDataException("The field camera has an invalid orientation matrix.");
            forward = Vector3.Normalize(forward);
            up = Vector3.Normalize(up);
            Vector3 right = Vector3.Cross(forward, up);
            if (right.LengthSquared() < 1e-8f)
                throw new InvalidDataException("The field camera has parallel forward and up vectors.");
            right = Vector3.Normalize(right);
            up = Vector3.Normalize(Vector3.Cross(right, forward));
            return new CameraFrame(eye, forward, up, right);
        }

        private static Vector3 ToViewerCoordinates(Vector3 fieldCoordinates) =>
            new(fieldCoordinates.X, fieldCoordinates.Z, -fieldCoordinates.Y);

        private static Vector3 ToViewerCoordinates(FieldVertex vertex) =>
            new(vertex.X, vertex.Z, -vertex.Y);

        private static float GetCameraFov(short zoom) {
            float safeZoom = Math.Max(Math.Abs((int)zoom), 1);
            return 2f * MathF.Atan(240f / (2f * safeZoom));
        }

        private static float GetBackgroundFov(short zoom, int backgroundHeight) {
            float scale = backgroundHeight > 0 ? backgroundHeight / 240f : 1f;
            return 2f * MathF.Atan(MathF.Tan(GetCameraFov(zoom) / 2f) * scale);
        }

        private static string CreateDiagnostics(
            string fieldCode,
            int cameraCount,
            Background background,
            Palettes palettes,
            Walkmesh walkmesh,
            CameraMatrix cameraData,
            CameraFrame camera,
            BackgroundImage image,
            BackgroundPlaneMetrics plane,
            float pixelSize,
            float cameraAspect,
            float backgroundFov,
            float cameraFar,
            TriggersAndGateways triggerData,
            IReadOnlyDictionary<int, int> initialParamStates,
            int orderedTileCount) {
            var lines = new List<string> {
                $"FIELD: {fieldCode}",
                $"Camera matrices in field: {cameraCount:N0}",
                "Camera matrix used: index 0",
                "",
                "CAMERA INPUT (DECODED FIELD VALUES)",
                $"Position before export scale: {FormatVector(cameraData.CameraPosition)}",
                "Position scale applied by exporter: 4096",
                $"Position after scale, FF7 axes: {FormatVector(cameraData.CameraPosition * 4096f)}",
                $"Position in GLB axes (X, Z, -Y): {FormatVector(camera.Eye)}",
                $"Raw Right axis (FF7): {FormatVector(cameraData.Right)}",
                $"Raw Up axis (FF7): {FormatVector(cameraData.Up)}",
                $"Raw Forward axis (FF7): {FormatVector(cameraData.Forwards)}",
                $"Converted normalized Right: {FormatVector(camera.Right)}",
                $"Converted normalized Up: {FormatVector(camera.Up)}",
                $"Converted normalized Forward: {FormatVector(camera.Forward)}",
                $"Coordinate conversion for points/vectors: (X, Y, Z) → (X, Z, -Y)",
                $"Camera zoom stored in field: {cameraData.Zoom}",
                $"Safe zoom used in FOV formula: {Math.Max(Math.Abs((int)cameraData.Zoom), 1)}",
                $"Base vertical FOV: {FormatDegrees(GetCameraFov(cameraData.Zoom))}° = 2 × atan(240 / (2 × safeZoom))",
                $"Background-height FOV scale: {(image.Height > 0 ? FormatFloat(image.Height / 240f) : "1 (no composite image; fallback)")}",
                $"Exported vertical FOV: {FormatDegrees(backgroundFov)}°",
                $"Exported camera aspect (width / height): {FormatFloat(cameraAspect)}",
                "Exported camera near clip: 1",
                $"Exported camera far clip: {FormatFloat(cameraFar)}",
                "",
                "BACKGROUND AND TILE COMPOSITE",
                $"Background header dimensions: {background.Width} × {background.Height} pixels",
                $"Layer sprites in file: {FormatLayerCounts(image.Stats.SourceTilesPerLayer)} (L0–L3)",
                $"Tiles after destination filter (|DestX|, |DestY| < 1024): {FormatLayerCounts(image.Stats.OrderedTilesPerLayer)} (L0–L3), total {orderedTileCount:N0}",
                $"Sprites used to calculate image bounds (DestX/Y < 9999): {image.Stats.DimensionSpritesForBounds:N0}",
                $"Tiles drawn after active-state/page/palette checks: {FormatLayerCounts(image.Stats.DrawnTilesPerLayer)} (L0–L3)",
                $"Inactive-state tiles skipped: {image.Stats.SkippedInactiveTiles:N0}; missing texture page: {image.Stats.MissingTexturePages:N0}; invalid palette: {image.Stats.InvalidPaletteTiles:N0}",
                $"Drawn tiles using blending: {image.Stats.BlendedTiles:N0}; blend modes 0/1/2/3/other: {FormatCounts(image.Stats.BlendModeCounts)} (average/add/subtract/quarter-add/replace)",
                $"Initial background state parameters read from init scripts: {image.Stats.InitialStateParameterCount:N0}",
                $"Initial parameter masks: {FormatParameterStates(initialParamStates)}",
                "Tile size used by renderer: L0/L1 = 16 × 16; L2/L3 = 32 × 32 pixels",
                "Tile source size comes from layer; Sprite.Width/Height are not used by this renderer.",
                "Sort: primary layer key descending, then ZParam descending; L0=4095, L1=Flags, L2=4096, L3=0.",
                $"Palette data origin (PalX, PalY): ({palettes.PalX}, {palettes.PalY}); palette count: {palettes.PaletteData.Count:N0} × 256 colors",
                $"Texture pages available: {background.Pages.Count:N0}; page IDs used by drawn tiles: {FormatIds(image.Stats.UsedTexturePages)}",
                $"Palette IDs used by drawn indexed tiles: {FormatIds(image.Stats.UsedPalettes)}",
                $"Texture bit depths used (0=4bpp, 1=8bpp, 2=direct color): {FormatIds(image.Stats.UsedTextureDepths.Select(depth => (int)depth))}",
                $"Composite image bounds: origin ({image.MinX}, {image.MinY}), max ({image.MinX + image.Width}, {image.MinY + image.Height}), size {image.Width} × {image.Height} pixels",
                $"Composite texture dimension limit: {MaxBackgroundDimension:N0} × {MaxBackgroundDimension:N0} pixels",
            };

            if (image.Width > 0 && image.Height > 0) {
                float planeWidth = image.Width * pixelSize;
                float planeHeight = image.Height * pixelSize;
                float centerX = (image.MinX + image.Width / 2f) * pixelSize;
                float centerY = -(image.MinY + image.Height / 2f) * pixelSize;
                Vector3 planeCenter = camera.Eye + camera.Forward * plane.Distance + camera.Right * centerX + camera.Up * centerY;
                lines.Add($"Pixel-to-world scale: {FormatFloat(pixelSize)} world units/pixel = 2 × plane distance × tan(base FOV / 2) / 240");
                lines.Add($"Background plane distance from camera: {FormatFloat(plane.Distance)}");
                lines.Add($"Background plane size: {FormatFloat(planeWidth)} × {FormatFloat(planeHeight)} world units");
                lines.Add($"Background center offset along camera Right/Up: ({FormatFloat(centerX)}, {FormatFloat(centerY)})");
                lines.Add($"Background plane center in GLB axes: {FormatVector(planeCenter)}");
            } else {
                lines.Add("Background plane: not created because no composite image dimensions were available.");
            }

            var walkVertices = walkmesh.Triangles.SelectMany(triangle => triangle.AllVerts()).ToArray();
            Vector3[] fieldCoordinates = walkVertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();
            Vector3[] viewerCoordinates = walkVertices.Select(ToViewerCoordinates).ToArray();
            int validTriangles = walkmesh.Triangles.Count(triangle => {
                Vector3 a = ToViewerCoordinates(triangle.V0);
                Vector3 b = ToViewerCoordinates(triangle.V1);
                Vector3 c = ToViewerCoordinates(triangle.V2);
                return Vector3.Cross(b - a, c - a).LengthSquared() >= 1e-8f;
            });
            int exportedGateways = triggerData.Gateways.Count(gateway => gateway.Destination.DestinationFieldID != 0);
            lines.AddRange(new[] {
                "",
                "WALKMESH AND PLANE PLACEMENT",
                $"Walkmesh triangles in file: {walkmesh.Triangles.Count:N0}; surface triangles exported: {validTriangles:N0}; vertices (with repeats): {walkVertices.Length:N0}",
                $"Walkmesh field-coordinate bounds: {FormatBounds(fieldCoordinates)}",
                $"Walkmesh GLB-coordinate bounds: {FormatBounds(viewerCoordinates)}",
                $"Extent used for plane base distance (max X span, Y span): X={FormatFloat(GetWalkmeshExtent(walkmesh, false))}, Y={FormatFloat(GetWalkmeshExtent(walkmesh, true))}; span={FormatFloat(plane.WalkmeshSpan)}",
                $"Base plane distance: span × 2 = {FormatFloat(plane.BaseDistance)}{(plane.UsedFallback ? "; replaced with fallback 5000 because distance was non-finite or < 1" : "")}",
                $"Farthest walkmesh depth along camera Forward: {FormatFloat(plane.FarthestWalkmeshDepth)}",
                $"Depth safety margin: {FormatFloat(plane.SafetyMargin)} (max(100, span × 0.05) when depth > 0)",
                $"Final plane distance: max(base distance, farthest depth + margin) = {FormatFloat(plane.Distance)}",
                $"Camera far clip: max(32000, plane distance + max(1000, plane distance × 0.01)) = {FormatFloat(cameraFar)}",
                $"Gateways in field data: {triggerData.Gateways.Count:N0}; gateways exported (destination field ID ≠ 0): {exportedGateways:N0}",
            });

            float safeZoom = Math.Max(Math.Abs((int)cameraData.Zoom), 1);
            float fovScale = image.Height > 0 ? image.Height / 240f : 1f;
            float baseFov = GetCameraFov(cameraData.Zoom);
            float compositeCenterX = (image.MinX + image.Width / 2f) * pixelSize;
            float compositeCenterY = -(image.MinY + image.Height / 2f) * pixelSize;
            Vector3 backgroundCenter = camera.Eye
                + camera.Forward * plane.Distance
                + camera.Right * compositeCenterX
                + camera.Up * compositeCenterY;
            lines.AddRange(new[] {
                "",
                "FORMULAE",
                $"Point/vector coordinate conversion: (x, y, z)FF7 → (x, z, -y)GLB; camera eye = convert(camera position × 4096) = {FormatVector(camera.Eye)}",
                $"Camera basis: forward=normalize(convert(Forwards)); right=normalize(cross(forward, convert(Up))); up=normalize(cross(right, forward)) = R{FormatVector(camera.Right)} U{FormatVector(camera.Up)} F{FormatVector(camera.Forward)}",
                $"Safe zoom z = max(abs(field zoom), 1) = {FormatFloat(safeZoom)}",
                $"Base vertical FOV = 2 × atan(240 / (2 × z)) = {FormatDegrees(baseFov)}°",
                $"Background FOV scale s = background pixel height / 240 = {FormatFloat(fovScale)}",
                $"Exported vertical FOV = 2 × atan(tan(base FOV / 2) × s) = {FormatDegrees(backgroundFov)}°",
                $"Camera aspect = composite width / composite height = {FormatFloat(cameraAspect)} (fallback 4/3 when height is zero)",
                $"Composite bounds: minX/minY are minimum tile destinations; maxX/maxY are maximum destination + layer tile size; result origin ({image.MinX}, {image.MinY}), size {image.Width} × {image.Height} px",
                $"World units per background pixel = 2 × plane distance × tan(base FOV / 2) / 240 = {FormatFloat(pixelSize)}",
                $"Plane width/height = composite width/height × pixel scale = {FormatFloat(image.Width * pixelSize)} × {FormatFloat(image.Height * pixelSize)}",
                $"Image center offset: right=(minX + width/2) × pixel scale = {FormatFloat(compositeCenterX)}; up=−(minY + height/2) × pixel scale = {FormatFloat(compositeCenterY)}",
                $"Plane center = eye + forward × distance + right × centerX + up × centerY = {FormatVector(backgroundCenter)}",
                $"Walkmesh span = max(range(field X), range(field Y)) = {FormatFloat(plane.WalkmeshSpan)}; base distance = span × 2 = {FormatFloat(plane.BaseDistance)} (fallback 5000 if non-finite or < 1)",
                $"Farthest depth = max(dot(convert(vertex) − eye, forward)) = {FormatFloat(plane.FarthestWalkmeshDepth)}; margin = max(100, span × 0.05) = {FormatFloat(plane.SafetyMargin)} when depth > 0",
                $"Final plane distance = max(base distance, farthest depth + margin) = {FormatFloat(plane.Distance)}",
                $"Camera far clip = max(32000, plane distance + max(1000, plane distance × 0.01)) = {FormatFloat(cameraFar)}",
            });
            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatVector(Vector3 value) =>
            $"({FormatFloat(value.X)}, {FormatFloat(value.Y)}, {FormatFloat(value.Z)})";

        private static string FormatFloat(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string FormatDegrees(float radians) =>
            FormatFloat(radians * (180f / MathF.PI));

        private static string FormatLayerCounts(IReadOnlyList<int> counts) =>
            string.Join(" / ", Enumerable.Range(0, 4).Select(index => index < counts.Count ? counts[index].ToString("N0", CultureInfo.InvariantCulture) : "0"));

        private static string FormatCounts(IReadOnlyList<int> counts) =>
            string.Join(" / ", counts.Select(count => count.ToString("N0", CultureInfo.InvariantCulture)));

        private static string FormatParameterStates(IReadOnlyDictionary<int, int> states) => states.Count == 0
            ? "(none found)"
            : string.Join(", ", states.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}=0x{pair.Value:X2}"));

        private static string FormatIds(IEnumerable<int> ids) {
            int[] values = ids.Distinct().OrderBy(id => id).ToArray();
            return values.Length == 0 ? "(none)" : string.Join(", ", values);
        }

        private static string FormatBounds(IReadOnlyList<Vector3> points) {
            if (points.Count == 0)
                return "(no vertices)";
            var min = new Vector3(points.Min(point => point.X), points.Min(point => point.Y), points.Min(point => point.Z));
            var max = new Vector3(points.Max(point => point.X), points.Max(point => point.Y), points.Max(point => point.Z));
            return $"min {FormatVector(min)}, max {FormatVector(max)}";
        }

        private struct VertexNoSkin : IVertexSkinning {
            public int MaxBindings => 0;
            public Vector4 JointsLow => Vector4.Zero;
            public Vector4 JointsHigh => Vector4.Zero;
            public Vector4 WeightsLow => Vector4.Zero;
            public Vector4 WeightsHigh => Vector4.Zero;
            public (int, float) GetBinding(int index) => (0, 0f);
            public void SetBindings(in SparseWeight8 bindings) { }
            public void SetBindings(params (int, float)[] bindings) { }
            public SparseWeight8 GetBindings() => default;
        }

        private static int TileSize(int layer) => layer <= 1 ? 16 : 32;

        private static byte ExpandFiveBit(int channel) => (byte)((channel << 3) | (channel >> 2));
    }
}
