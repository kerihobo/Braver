// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7;
using Ficedula.FF7.Exporters;
using Ficedula.FF7.WorldMap;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CrossSlash {
    public sealed class WorldMapExport : ExportKind {
        public override string Help =>
@"Usage: CrossSlash WorldMap [SourceLGPOrFolder] [OutputFile]
    Exports FF7 world map terrain, world landmark models, and animated vehicle models.
    Choose world_us.lgp for models and textures. Terrain maps are loaded from the matching game data folder.";

        public override object Config { get; } = new object();
        public override bool HasGui => true;
        public override string Name => "World Map Export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            DataSource terrainSource = WorldMapExportWindow.CreateTerrainSource(source, out _);
            WorldMapExportWindow.TerrainMap map = WorldMapExportWindow.FindTerrainMaps(terrainSource).FirstOrDefault()
                ?? throw new FileNotFoundException("FF7 world terrain files (WM0.MAP, WM2.MAP, WM3.MAP) could not be found beside the selected source or in a Steam installation.");
            WorldMapTerrainExporter.BuildScene(terrainSource, map.FileName, map.BlockCount, CancellationToken.None, null).SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new WorldMapExportWindow(source);
    }

    internal sealed class WorldMapExportWindow : Window {
        internal sealed record TerrainMap(string FileName, int BlockCount, int VariantCount) {
            public int MapId => Path.GetFileNameWithoutExtension(FileName).ToUpperInvariant() switch {
                "WM0" => 0,
                "WM2" => 2,
                "WM3" => 3,
                _ => throw new InvalidOperationException($"Unsupported world terrain map: {FileName}"),
            };

            public string MapName => MapId switch {
                0 => "Overworld",
                2 => "Underwater",
                3 => "Great Glacier",
                _ => "World terrain",
            };

            public string Label => VariantCount > 0
                ? $"{MapName} (WM{MapId}) · {BlockCount:N0} sections · {VariantCount:N0} variants"
                : $"{MapName} (WM{MapId}) · {BlockCount:N0} sections";
        }

        private sealed class TerrainDataSource : DataSource {
            private readonly DataSource _modelSource;
            private readonly string _terrainFolder;

            public override IEnumerable<string> AllFiles => _modelSource.AllFiles.Concat(
                Directory.GetFiles(_terrainFolder)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>());

            public TerrainDataSource(DataSource modelSource, string terrainFolder) {
                _modelSource = modelSource;
                _terrainFolder = terrainFolder;
            }

            public override bool Exists(string name) =>
                File.Exists(Path.Combine(_terrainFolder, name)) || _modelSource.Exists(name);

            public override Stream Open(string name) {
                string loosePath = Path.Combine(_terrainFolder, name);
                return File.Exists(loosePath) ? File.OpenRead(loosePath) : _modelSource.Open(name);
            }
        }

        private sealed record WorldModel(string Id, string Name, string[] AnimationIds) {
            public string HrcFile { get; set; } = string.Empty;
            public string DisplayName => $"{Name} ({Id})";
        }

        private static readonly WorldModel[] KnownVehicles = {
            new("aba", "Buggy", new[] { "age", "aha", "ahb", "ahc", "ahd", "ahe" }),
            new("aja", "Chocobo", new[] { "asc", "asd", "ase" }),
            new("cgd", "Highwind", new[] { "cic" }),
            new("cid", "Highwind (final)", new[] { "cma" }),
            new("ddd", "Submarine (grey)", new[] { "dfe" }),
            new("dga", "Submarine (red)", new[] { "dfe" }),
            new("dva", "Tiny Bronco", new[] { "dya" }),
        };

        private static readonly WorldModel[] KnownLandmarks = {
            new("aaa", "North crater barrier", new[] { "aae" }),
            new("aia", "Junon cannon", new[] { "aie" }),
            new("bkd", "Fort Condor · condor", new[] { "bld" }),
            new("ble", "Coral Key", new[] { "bme" }),
            new("bna", "Diamond Weapon", new[] { "btd", "bte", "bua", "bub", "buc" }),
            new("bud", "Emerald Weapon", new[] { "cea", "ceb" }),
            new("cec", "Gelnika", new[] { "cfb" }),
            new("cfc", "Gold Saucer", new[] { "cgc" }),
            new("cmb", "Midgar cannon · Sister Ray", new[] { "cna" }),
            new("cnb", "Ancient forest rock", new[] { "cob" }),
            new("coc", "Rocket Town rocket", new[] { "cpb" }),
            new("cpc", "Rocket launch site", new[] { "cqb" }),
            new("cqc", "Ruby Weapon", new[] { "dda", "ddb", "ddc" }),
            new("dic", "Underwater rocks", new[] { "djb" }),
            new("djc", "Glacier pole", new[] { "dkb" }),
            new("dyb", "Ultima Weapon", new[] { "eic", "eid", "eie", "eja", "ejb", "ejc", "ejd" }),
            new("eje", "Underwater reactor", new[] { "ekd" }),
            new("eke", "Cargo ship", new[] { "ena" }),
        };

        private readonly DataSource _source;
        private DataSource _mapSource;
        private string? _terrainDataFolder;
        private readonly Dictionary<string, string> _fileNames;
        private readonly ModelPreviewControl _preview;
        private readonly ModelPreviewWindowHost _previewWindowHost;
        private readonly DispatcherTimer _previewTimer;
        private readonly ModelPreviewControl _terrainPreview;
        private readonly ModelPreviewWindowHost _terrainPreviewWindowHost;
        private readonly DispatcherTimer _terrainPreviewTimer;
        private readonly ModelPreviewControl _landmarkPreview;
        private readonly ModelPreviewWindowHost _landmarkPreviewWindowHost;
        private readonly DispatcherTimer _landmarkPreviewTimer;
        private readonly ListBox _vehicleList;
        private readonly ListBox _animationList;
        private readonly TextBox _vehicleSearch;
        private readonly TextBox _animationSearch;
        private readonly TextBox _landmarkSearch;
        private readonly TextBox _landmarkAnimationSearch;
        private readonly TextBox _terrainSearch;
        private readonly ListBox _terrainList;
        private readonly ListBox _landmarkList;
        private readonly ListBox _landmarkAnimationList;
        private readonly TextBlock _terrainTitle;
        private readonly TextBlock _mapSourceLabel;
        private readonly TextBlock _landmarkTitle;
        private readonly TextBlock _landmarkAnimationTitle;
        private readonly CheckBox _embedAnimations;
        private readonly CheckBox _prefixAnimations;
        private readonly CheckBox _convertSrgb;
        private readonly CheckBox _landmarkEmbedAnimations;
        private readonly CheckBox _landmarkPrefixAnimations;
        private readonly CheckBox _landmarkConvertSrgb;
        private readonly TextBox _scale;
        private readonly TextBox _landmarkScale;
        private readonly HashSet<string> _selectedAnimations = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedLandmarkAnimations = new(StringComparer.OrdinalIgnoreCase);
        private List<WorldModel> _vehicles;
        private List<WorldModel> _landmarks;
        private List<TerrainMap> _terrainMaps = new();
        private List<string> _currentAnimations = new();
        private List<string> _currentLandmarkAnimations = new();
        private string? _previewAnimation;
        private string? _landmarkPreviewAnimation;

        public WorldMapExportWindow(DataSource source) {
            _source = source;
            _terrainDataFolder = FindTerrainDataFolder(source);
            _mapSource = _terrainDataFolder == null ? source : new TerrainDataSource(source, _terrainDataFolder);
            _fileNames = source.AllFiles
                .Select(file => Path.GetFileName(file))
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(file => file, file => file, StringComparer.OrdinalIgnoreCase);

            _vehicles = FindVehicles();
            _landmarks = FindLandmarks();
            _preview = new ModelPreviewControl { MinHeight = 300 };
            _previewWindowHost = new ModelPreviewWindowHost(this, _preview);
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _previewTimer.Tick += (_, _) => {
                _previewTimer.Stop();
                RefreshPreview();
            };
            _terrainPreview = new ModelPreviewControl { MinHeight = 300 };
            _terrainPreviewWindowHost = new ModelPreviewWindowHost(this, _terrainPreview);
            _terrainPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _terrainPreviewTimer.Tick += (_, _) => {
                _terrainPreviewTimer.Stop();
                RefreshTerrainPreview();
            };
            _landmarkPreview = new ModelPreviewControl { MinHeight = 300 };
            _landmarkPreviewWindowHost = new ModelPreviewWindowHost(this, _landmarkPreview);
            _landmarkPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _landmarkPreviewTimer.Tick += (_, _) => {
                _landmarkPreviewTimer.Stop();
                RefreshLandmarkPreview();
            };
            Closed += (_, _) => {
                _previewTimer.Stop();
                _terrainPreviewTimer.Stop();
                _landmarkPreviewTimer.Stop();
                _previewWindowHost.Close();
                _terrainPreviewWindowHost.Close();
                _landmarkPreviewWindowHost.Close();
            };

            _vehicleSearch = DesktopUi.CreateSearchTextBox("Search vehicles");
            _animationSearch = DesktopUi.CreateSearchTextBox("Search animations");
            _landmarkSearch = DesktopUi.CreateSearchTextBox("Search landmarks");
            _landmarkAnimationSearch = DesktopUi.CreateSearchTextBox("Search animations");
            _terrainSearch = DesktopUi.CreateSearchTextBox("Search maps");
            _terrainList = CreateListBox();
            _landmarkList = CreateListBox();
            _landmarkAnimationList = CreateListBox();
            _landmarkAnimationList.SelectionMode = SelectionMode.Multiple;
            _terrainTitle = DesktopUi.CreateSectionTitle("Terrain maps (0)");
            _mapSourceLabel = new TextBlock {
                Text = _terrainDataFolder ?? "Terrain data not found automatically.",
                Foreground = DesktopUi.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _landmarkTitle = DesktopUi.CreateSectionTitle($"Landmarks ({_landmarks.Count:N0})");
            _landmarkAnimationTitle = DesktopUi.CreateSectionTitle("Compatible animations (0)");
            _vehicleList = CreateListBox();
            _animationList = CreateListBox();
            _animationList.SelectionMode = SelectionMode.Multiple;

            _embedAnimations = new CheckBox { Content = "Embed animations", IsChecked = true, Margin = new Thickness(4, 4, 12, 4) };
            _prefixAnimations = new CheckBox {
                Content = "Prefix animation names",
                IsChecked = PreferencesStore.Load().PrefixAnimationNamesWithModelName,
                ToolTip = "Add the vehicle name to exported animation names and files.",
                Margin = new Thickness(4, 4, 12, 4),
            };
            _convertSrgb = new CheckBox {
                Content = "Convert colours from sRGB to linear",
                IsChecked = true,
                Margin = new Thickness(4, 4, 12, 4),
            };
            _scale = DesktopUi.CreateTextBox("1");
            _prefixAnimations.Checked += (_, _) => PreferencesStore.RememberAnimationNamePrefix(true);
            _prefixAnimations.Unchecked += (_, _) => PreferencesStore.RememberAnimationNamePrefix(false);
            _landmarkEmbedAnimations = new CheckBox { Content = "Embed animations", IsChecked = true, Margin = new Thickness(4) };
            _landmarkPrefixAnimations = new CheckBox {
                Content = "Prefix animation names",
                IsChecked = PreferencesStore.Load().PrefixAnimationNamesWithModelName,
                ToolTip = "Add the landmark name to exported animation names and files.",
                Margin = new Thickness(4),
            };
            _landmarkConvertSrgb = new CheckBox { Content = "Convert colours from sRGB to linear", IsChecked = true, Margin = new Thickness(4) };
            _landmarkScale = DesktopUi.CreateTextBox("1");
            _landmarkPrefixAnimations.Checked += (_, _) => PreferencesStore.RememberAnimationNamePrefix(true);
            _landmarkPrefixAnimations.Unchecked += (_, _) => PreferencesStore.RememberAnimationNamePrefix(false);
            _prefixAnimations.Checked += (_, _) => _landmarkPrefixAnimations.IsChecked = true;
            _prefixAnimations.Unchecked += (_, _) => _landmarkPrefixAnimations.IsChecked = false;
            _landmarkPrefixAnimations.Checked += (_, _) => _prefixAnimations.IsChecked = true;
            _landmarkPrefixAnimations.Unchecked += (_, _) => _prefixAnimations.IsChecked = false;

            var tabs = new TabControl { Margin = new Thickness(0, 4, 0, 0) };
            tabs.Items.Add(new TabItem { Header = "Terrain", Content = CreateTerrainPage() });
            tabs.Items.Add(new TabItem { Header = "Landmarks", Content = CreateLandmarkPage() });
            tabs.Items.Add(new TabItem { Header = "Vehicles", Content = CreateVehiclePage() });

            var root = new Grid();
            root.Children.Add(tabs);
            DesktopUi.ConfigureWindow(this, root, width: 1120, height: 820, scrollContent: false);
            RefreshTerrainMaps(_mapSource, selectFirst: true);
            if (_vehicles.Count == 0)
                _preview.ShowMessage("Choose world_us.lgp or an extracted world_us.lgp folder to load vehicle models.");
            if (_landmarks.Count == 0)
                _landmarkPreview.ShowMessage("Choose world_us.lgp to load world landmark models.");
        }

        private UIElement CreateTerrainPage() {
            var browse = DesktopUi.CreateButton("Locate terrain data");
            browse.Click += (_, _) => BrowseMapFolder();
            var sourceRow = new Grid { Margin = new Thickness(10, 8, 10, 4) };
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceRow.Children.Add(DesktopUi.CreateSectionTitle("Terrain data"));
            _mapSourceLabel.Margin = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(_mapSourceLabel, 1);
            sourceRow.Children.Add(_mapSourceLabel);
            Grid.SetColumn(browse, 2);
            sourceRow.Children.Add(browse);

            var columns = new Grid { Margin = new Thickness(10, 4, 10, 8) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.28, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) });
            var list = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            list.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            list.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            list.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            list.Children.Add(_terrainTitle);
            var search = DesktopUi.CreateSearchField("Search terrain maps…", _terrainSearch);
            Grid.SetRow(search, 1);
            list.Children.Add(search);
            Grid.SetRow(_terrainList, 2);
            list.Children.Add(_terrainList);
            columns.Children.Add(list);

            var preview = CreatePreviewPanel(_terrainPreview, _terrainPreviewWindowHost, "Preview");
            Grid.SetColumn(preview, 1);
            columns.Children.Add(preview);
            _terrainSearch.TextChanged += (_, _) => RefreshTerrainList();
            _terrainList.SelectionChanged += (_, _) => ScheduleTerrainPreview();
            _terrainList.Loaded += (_, _) => SelectFirstTerrainMap();

            var exportSelected = DesktopUi.CreateButton("Export selected", primary: true);
            exportSelected.Click += async (_, _) => await ExportSelectedTerrainAsync();
            var exportAll = DesktopUi.CreateButton("Export all maps");
            exportAll.Click += async (_, _) => await ExportAllTerrainAsync();
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(sourceRow);
            Grid.SetRow(columns, 1);
            content.Children.Add(columns);
            UIElement actions = DesktopUi.CreateActionRow(exportSelected, exportAll);
            Grid.SetRow(actions, 2);
            content.Children.Add(actions);
            return content;
        }

        private UIElement CreateLandmarkPage() {
            var columns = new Grid { Margin = new Thickness(10, 10, 10, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.25, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.25, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.50, GridUnitType.Star) });

            var models = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            models.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            models.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            models.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            models.Children.Add(_landmarkTitle);
            var modelSearch = DesktopUi.CreateSearchField("Search landmarks…", _landmarkSearch);
            Grid.SetRow(modelSearch, 1);
            models.Children.Add(modelSearch);
            _landmarkList.ItemsSource = _landmarks;
            _landmarkList.DisplayMemberPath = nameof(WorldModel.DisplayName);
            Grid.SetRow(_landmarkList, 2);
            models.Children.Add(_landmarkList);
            columns.Children.Add(models);

            var animations = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            animations.Children.Add(_landmarkAnimationTitle);
            var animationSearch = DesktopUi.CreateSearchField("Search animations…", _landmarkAnimationSearch);
            Grid.SetRow(animationSearch, 1);
            animations.Children.Add(animationSearch);
            var animationHint = DesktopUi.CreateHint("Left-click to preview; right-click to select or clear for export.");
            animationHint.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(animationHint, 2);
            animations.Children.Add(animationHint);
            Grid.SetRow(_landmarkAnimationList, 3);
            animations.Children.Add(_landmarkAnimationList);
            Grid.SetColumn(animations, 1);
            columns.Children.Add(animations);

            var preview = CreatePreviewPanel(_landmarkPreview, _landmarkPreviewWindowHost, "Preview");
            Grid.SetColumn(preview, 2);
            columns.Children.Add(preview);

            _landmarkSearch.TextChanged += (_, _) => FilterLandmarks();
            _landmarkAnimationSearch.TextChanged += (_, _) => RefreshLandmarkAnimationList();
            _landmarkList.SelectionChanged += (_, _) => {
                _selectedLandmarkAnimations.Clear();
                _landmarkPreviewAnimation = null;
                RefreshLandmarkAnimations();
                ScheduleLandmarkPreview();
            };
            _landmarkList.Loaded += (_, _) => {
                if (_landmarkList.SelectedIndex < 0 && _landmarkList.Items.Count > 0)
                    _landmarkList.SelectedIndex = 0;
            };
            _landmarkAnimationList.PreviewMouseLeftButtonDown += (_, e) => {
                if (TryGetListItem(_landmarkAnimationList, e.OriginalSource as DependencyObject, out string animation)) {
                    PreviewLandmarkAnimation(animation);
                    e.Handled = true;
                }
            };
            _landmarkAnimationList.PreviewMouseRightButtonDown += (_, e) => {
                if (!TryGetListItem(_landmarkAnimationList, e.OriginalSource as DependencyObject, out string animation))
                    return;
                PreviewLandmarkAnimation(animation);
                if (_selectedLandmarkAnimations.Remove(animation))
                    _landmarkAnimationList.SelectedItems.Remove(animation);
                else {
                    _selectedLandmarkAnimations.Add(animation);
                    _landmarkAnimationList.SelectedItems.Add(animation);
                }
                e.Handled = true;
            };
            RefreshLandmarkAnimations();

            var options = CreateExportOptions();
            var exportSelected = DesktopUi.CreateButton("Export selected", primary: true);
            exportSelected.Click += async (_, _) => await ExportSelectedLandmarkAsync();
            var exportAll = DesktopUi.CreateButton("Export all landmarks");
            exportAll.Click += async (_, _) => await ExportAllLandmarksAsync();
            var exportAnimations = DesktopUi.CreateButton("Export all landmark animations separately");
            exportAnimations.Click += async (_, _) => await ExportLandmarkAnimationsAsync();
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(columns);
            var placementNote = DesktopUi.CreateHint("Landmark GLBs contain the individual world models and their animations; in-game placement on the terrain is not included.");
            Grid.SetRow(placementNote, 1);
            panel.Children.Add(placementNote);
            Grid.SetRow(options, 2);
            panel.Children.Add(options);
            UIElement actions = DesktopUi.CreateActionRow(exportSelected, exportAll, exportAnimations);
            Grid.SetRow(actions, 3);
            panel.Children.Add(actions);
            return panel;
        }

        private static Grid CreatePreviewPanel(ModelPreviewControl preview, ModelPreviewWindowHost host, string title) {
            var panel = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(DesktopUi.CreateSectionTitle(title));
            var previewHost = new Grid { MinHeight = 300, ClipToBounds = true };
            previewHost.Children.Add(preview);
            host.AttachTo(previewHost);
            Grid.SetRow(previewHost, 1);
            panel.Children.Add(previewHost);
            return panel;
        }

        private UniformGrid CreateExportOptions() {
            var options = new UniformGrid { Columns = 4, Margin = new Thickness(8, 2, 8, 2) };
            FrameworkElement[] optionControls = {
                _landmarkEmbedAnimations,
                _landmarkPrefixAnimations,
                _landmarkConvertSrgb,
                (FrameworkElement)DesktopUi.CreateScaleOption(_landmarkScale),
            };
            foreach (FrameworkElement option in optionControls) {
                option.Margin = new Thickness(4);
                option.VerticalAlignment = VerticalAlignment.Center;
                options.Children.Add(option);
            }
            return options;
        }

        private UIElement CreateVehiclePage() {
            var columns = new Grid { Margin = new Thickness(10, 10, 10, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.24, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.24, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.52, GridUnitType.Star) });

            var models = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            models.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            models.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            models.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var modelTitle = DesktopUi.CreateSectionTitle($"Vehicles ({_vehicles.Count:N0})");
            models.Children.Add(modelTitle);
            var modelSearch = DesktopUi.CreateSearchField("Search vehicles…", _vehicleSearch);
            Grid.SetRow(modelSearch, 1);
            models.Children.Add(modelSearch);
            _vehicleList.ItemsSource = _vehicles;
            _vehicleList.DisplayMemberPath = nameof(WorldModel.DisplayName);
            Grid.SetRow(_vehicleList, 2);
            models.Children.Add(_vehicleList);
            columns.Children.Add(models);

            var animations = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            animations.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var animationTitle = DesktopUi.CreateSectionTitle("Compatible animations (0)");
            animations.Children.Add(animationTitle);
            var animationSearch = DesktopUi.CreateSearchField("Search animations…", _animationSearch);
            Grid.SetRow(animationSearch, 1);
            animations.Children.Add(animationSearch);
            var animationHint = DesktopUi.CreateHint("Left-click to preview; right-click to select or clear for export.");
            animationHint.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(animationHint, 2);
            animations.Children.Add(animationHint);
            Grid.SetRow(_animationList, 3);
            animations.Children.Add(_animationList);
            Grid.SetColumn(animations, 1);
            columns.Children.Add(animations);

            var preview = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            preview.Children.Add(DesktopUi.CreateSectionTitle("Preview"));
            var previewHost = new Grid { MinHeight = 300, ClipToBounds = true };
            previewHost.Children.Add(_preview);
            _previewWindowHost.AttachTo(previewHost);
            Grid.SetRow(previewHost, 1);
            preview.Children.Add(previewHost);
            Grid.SetColumn(preview, 2);
            columns.Children.Add(preview);

            _vehicleSearch.TextChanged += (_, _) => FilterVehicles(modelTitle);
            _animationSearch.TextChanged += (_, _) => RefreshAnimationList(animationTitle);
            _vehicleList.SelectionChanged += (_, _) => {
                _selectedAnimations.Clear();
                _previewAnimation = null;
                RefreshVehicleAnimations(animationTitle);
                SchedulePreview();
            };
            _vehicleList.Loaded += (_, _) => {
                if (_vehicleList.SelectedIndex < 0 && _vehicleList.Items.Count > 0)
                    _vehicleList.SelectedIndex = 0;
            };
            _animationList.PreviewMouseLeftButtonDown += (_, e) => {
                if (TryGetAnimationItem(e.OriginalSource as DependencyObject, out string animation)) {
                    PreviewAnimation(animation);
                    e.Handled = true;
                }
            };
            _animationList.PreviewMouseRightButtonDown += (_, e) => {
                if (!TryGetAnimationItem(e.OriginalSource as DependencyObject, out string animation))
                    return;
                PreviewAnimation(animation);
                if (_selectedAnimations.Remove(animation))
                    _animationList.SelectedItems.Remove(animation);
                else {
                    _selectedAnimations.Add(animation);
                    _animationList.SelectedItems.Add(animation);
                }
                e.Handled = true;
            };

            RefreshVehicleAnimations(animationTitle);

            var options = new UniformGrid { Columns = 4, Margin = new Thickness(8, 2, 8, 2) };
            FrameworkElement[] optionControls = {
                _embedAnimations,
                _prefixAnimations,
                _convertSrgb,
                (FrameworkElement)DesktopUi.CreateScaleOption(_scale),
            };
            foreach (FrameworkElement option in optionControls) {
                option.Margin = new Thickness(4);
                option.VerticalAlignment = VerticalAlignment.Center;
                options.Children.Add(option);
            }

            var exportSelected = DesktopUi.CreateButton("Export selected", primary: true);
            exportSelected.Click += async (_, _) => await ExportSelectedVehicleAsync();
            var exportAll = DesktopUi.CreateButton("Export all vehicles + animations");
            exportAll.Click += async (_, _) => await ExportAllVehiclesAsync();
            var exportAnimations = DesktopUi.CreateButton("Export all vehicle animations separately");
            exportAnimations.Click += async (_, _) => await ExportVehicleAnimationsAsync();

            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(columns);
            Grid.SetRow(options, 1);
            panel.Children.Add(options);
            UIElement actions = DesktopUi.CreateActionRow(exportSelected, exportAll, exportAnimations);
            Grid.SetRow(actions, 2);
            panel.Children.Add(actions);
            return panel;
        }

        private static ListBox CreateListBox() => new() {
            MinHeight = 0,
            Background = DesktopUi.InputBackgroundBrush,
            Foreground = DesktopUi.TextPrimaryBrush,
            BorderBrush = DesktopUi.ControlBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };

        private List<WorldModel> FindVehicles() => FindModels(KnownVehicles);

        private List<WorldModel> FindLandmarks() => FindModels(KnownLandmarks);

        private List<WorldModel> FindModels(IEnumerable<WorldModel> knownModels) {
            var files = _fileNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = knownModels
                .Select(model => new { Model = model, File = files.FirstOrDefault(file => file.Equals(model.Id + ".hrc", StringComparison.OrdinalIgnoreCase)) })
                .Where(item => item.File != null)
                .ToList();
            var animations = _fileNames.Keys
                .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var compatibility = new FieldModel(_source, new ModelBaseOptions());
            var result = new List<WorldModel>();
            foreach (var candidate in candidates) {
                string hrc = _fileNames[candidate.File!];
                string[] mapped = candidate.Model.AnimationIds
                    .Select(id => animations.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .Where(file => file != null)
                    .Cast<string>()
                    .ToArray();
                string[] compatible;
                try {
                    compatible = compatibility.FindCompatibleAnimations(hrc, mapped).ToArray();
                } catch {
                    compatible = mapped;
                }
                result.Add(candidate.Model with { HrcFile = hrc, AnimationIds = compatible });
            }
            return result;
        }

        internal static List<TerrainMap> FindTerrainMaps(DataSource source) {
            var maps = new List<TerrainMap>();
            foreach (string file in source.AllFiles) {
                string name = Path.GetFileName(file);
                if (!Path.GetExtension(name).Equals(".map", StringComparison.OrdinalIgnoreCase))
                    continue;
                string mapStem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
                (int mapId, int sectionCount) = mapStem switch {
                    "WM0" => (0, 63),
                    "WM2" => (2, 12),
                    "WM3" => (3, 4),
                    _ => (-1, 0),
                };
                if (mapId < 0)
                    continue;
                try {
                    using Stream stream = source.Open(file);
                    long storedSectionCount = stream.Length / WorldMapTerrainExporter.BlockByteLength;
                    if (storedSectionCount >= sectionCount && stream.Length % WorldMapTerrainExporter.BlockByteLength == 0) {
                        int variantCount = mapId == 0 ? (int)(storedSectionCount - sectionCount) : 0;
                        maps.Add(new TerrainMap(file, sectionCount, variantCount));
                    }
                } catch {
                    // Ignore entries that cannot be opened as a terrain map.
                }
            }
            return maps.OrderBy(map => map.MapId).ToList();
        }

        internal static DataSource CreateTerrainSource(DataSource source, out string? terrainFolder) {
            terrainFolder = FindTerrainDataFolder(source);
            return terrainFolder == null ? source : new TerrainDataSource(source, terrainFolder);
        }

        private static string? FindTerrainDataFolder(DataSource source) {
            string? sourcePath = source.SourcePath;
            if (!string.IsNullOrWhiteSpace(sourcePath)) {
                string candidate = Directory.Exists(sourcePath)
                    ? sourcePath
                    : Path.GetDirectoryName(sourcePath) ?? string.Empty;
                if (HasTerrainMaps(candidate))
                    return candidate;
            }

            foreach (string library in FindSteamLibraries()) {
                string manifestPath = Path.Combine(library, "steamapps", "appmanifest_39140.acf");
                if (!File.Exists(manifestPath))
                    continue;
                try {
                    string manifest = File.ReadAllText(manifestPath);
                    Match installDirMatch = Regex.Match(manifest, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    string installDir = installDirMatch.Success ? installDirMatch.Groups[1].Value : "FINAL FANTASY VII";
                    string candidate = Path.Combine(library, "steamapps", "common", installDir, "data", "wm");
                    if (HasTerrainMaps(candidate))
                        return candidate;
                } catch {
                    // Ignore Steam libraries with unreadable or malformed app manifests.
                }
            }

            return null;
        }

        private static bool HasTerrainMaps(string folder) {
            if (!Directory.Exists(folder))
                return false;
            try {
                return Directory.EnumerateFiles(folder, "wm*.map", SearchOption.TopDirectoryOnly)
                    .Any(file => {
                        long length = new FileInfo(file).Length;
                        return length > 0 && length % WorldMapTerrainExporter.BlockByteLength == 0;
                    });
            } catch {
                return false;
            }
        }

        private static IEnumerable<string> FindSteamLibraries() {
            var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSteamRoot(steamRoots, ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            AddSteamRoot(steamRoots, ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));
            AddSteamRoot(steamRoots, ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            AddSteamRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            AddSteamRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

            foreach (string drive in Directory.GetLogicalDrives()) {
                AddSteamRoot(steamRoots, Path.Combine(drive, "Steam"));
                AddSteamRoot(steamRoots, Path.Combine(drive, "SteamLibrary"));
                AddSteamRoot(steamRoots, Path.Combine(drive, "Program Files (x86)", "Steam"));
                AddSteamRoot(steamRoots, Path.Combine(drive, "Program Files", "Steam"));
            }

            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in steamRoots) {
                libraries.Add(root);
                string foldersFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(foldersFile))
                    continue;
                try {
                    string text = File.ReadAllText(foldersFile);
                    foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)) {
                        string path = match.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(path))
                            libraries.Add(path);
                    }
                } catch {
                    // Keep checking other Steam roots when a library list cannot be read.
                }
            }

            return libraries;
        }

        private static void AddSteamRoot(ISet<string> roots, string? path) {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    roots.Add(fullPath);
            } catch {
                // Ignore invalid registry or environment paths.
            }
        }

        private static string? ReadRegistryString(RegistryKey root, string subKey, string valueName) {
            try {
                return root.OpenSubKey(subKey)?.GetValue(valueName) as string;
            } catch {
                return null;
            }
        }

        private void FilterVehicles(TextBlock title) {
            var selectedId = (_vehicleList.SelectedItem as WorldModel)?.Id;
            var visible = _vehicles.Where(vehicle => vehicle.DisplayName.Contains(_vehicleSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            title.Text = $"Vehicles ({visible.Count:N0})";
            _vehicleList.ItemsSource = visible;
            if (selectedId != null)
                _vehicleList.SelectedItem = visible.FirstOrDefault(vehicle => vehicle.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (_vehicleList.SelectedIndex < 0 && visible.Count > 0)
                _vehicleList.SelectedIndex = 0;
        }

        private void RefreshVehicleAnimations(TextBlock title) {
            WorldModel? vehicle = _vehicleList.SelectedItem as WorldModel;
            _currentAnimations = vehicle?.AnimationIds.ToList() ?? new List<string>();
            title.Text = $"Compatible animations ({_currentAnimations.Count:N0})";
            RefreshAnimationList(title);
        }

        private void RefreshAnimationList(TextBlock title) {
            List<string> visible = _currentAnimations
                .Where(file => file.Contains(_animationSearch.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _animationList.UnselectAll();
            _animationList.ItemsSource = visible;
            foreach (string animation in visible) {
                if (_selectedAnimations.Contains(animation))
                    _animationList.SelectedItems.Add(animation);
            }
            title.Text = $"Compatible animations ({_currentAnimations.Count:N0})";
        }

        private bool TryGetAnimationItem(DependencyObject? source, out string animation) {
            animation = string.Empty;
            if (source == null)
                return false;
            ListBoxItem? item = ItemsControl.ContainerFromElement(_animationList, source) as ListBoxItem;
            if (item?.Content is not string file)
                return false;
            animation = file;
            return true;
        }

        private void SchedulePreview() {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview() {
            if (_vehicleList.SelectedItem is not WorldModel vehicle) {
                _preview.ShowMessage(_vehicles.Count == 0
                    ? "No supported world vehicle HRC models were found in this source."
                    : "Select a vehicle to preview it.");
                return;
            }
            string animation = _previewAnimation != null && _currentAnimations.Contains(_previewAnimation, StringComparer.OrdinalIgnoreCase)
                ? _previewAnimation
                : _currentAnimations.FirstOrDefault() ?? string.Empty;
            _previewAnimation = animation.Length == 0 ? null : animation;
            string[] animations = animation.Length == 0 ? Array.Empty<string>() : new[] { animation };
            ModelBaseOptions options = GetOptions();
            DataSource source = _source;
            _preview.ShowModel(() => BuildVehicleScene(source, vehicle, animations, options, includeMeshes: true, includeAnimations: true),
                animation.Length == 0 ? vehicle.DisplayName : $"{vehicle.DisplayName} · {Path.GetFileNameWithoutExtension(animation)}");
        }

        private void PreviewAnimation(string animation) {
            _previewAnimation = animation;
            if (_vehicleList.SelectedItem is not WorldModel vehicle)
                return;
            DataSource source = _source;
            ModelBaseOptions options = GetOptions();
            _preview.ShowAnimation(() => BuildVehicleScene(source, vehicle, new[] { animation }, options, includeMeshes: true, includeAnimations: true),
                Path.GetFileNameWithoutExtension(animation));
        }

        private ModelBaseOptions GetOptions() => new() {
            ConvertSRGBToLinear = _convertSrgb.IsChecked == true,
            Scale = TryGetScale(out float scale) ? scale : 1f,
        };

        private ModelBaseOptions GetLandmarkOptions() => new() {
            ConvertSRGBToLinear = _landmarkConvertSrgb.IsChecked == true,
            Scale = TryGetLandmarkScale(out float scale) ? scale : 1f,
        };

        private bool TryGetScale(out float scale) =>
            float.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out scale) &&
            float.IsFinite(scale) && scale > 0;

        private bool TryGetLandmarkScale(out float scale) =>
            float.TryParse(_landmarkScale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out scale) &&
            float.IsFinite(scale) && scale > 0;

        private ModelRoot BuildVehicleScene(DataSource source, WorldModel vehicle, IEnumerable<string> animations, ModelBaseOptions options,
            bool includeMeshes, bool includeAnimations) {
            var exporter = new FieldModel(source, options);
            string[] animationFiles = animations.ToArray();
            string[] poseAnimations = animationFiles.Length > 0 ? animationFiles : vehicle.AnimationIds.Take(1).ToArray();
            ModelRoot model = exporter.BuildScene(
                vehicle.HrcFile,
                poseAnimations,
                compatibleAnimationsOnly: true,
                out _,
                includeMeshes: includeMeshes,
                includeAnimations: includeAnimations,
                useFirstAnimationFrameAsBindPose: true,
                useSkinnedMeshes: false
            );
            FieldExportWindow.AddRootContainerMarker(model);
            return model;
        }

        private void BrowseMapFolder() {
            string? startFolder = _terrainDataFolder ?? _source.SourcePath ?? PreferencesStore.Load().LastOutputDirectory;
            string? folder = NativeFilePicker.OpenFolder("Choose FF7's data\\wm folder containing WM*.MAP files", startFolder, this);
            if (folder == null)
                return;
            try {
                _terrainDataFolder = folder;
                _mapSourceLabel.Text = folder;
                RefreshTerrainMaps(new TerrainDataSource(_source, folder), selectFirst: true);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Could not load terrain maps", ex.Message);
            }
        }

        private void RefreshTerrainMaps(DataSource source, bool selectFirst) {
            string? selectedFile = (_terrainList.SelectedItem as TerrainMap)?.FileName;
            _mapSource = source;
            _terrainMaps = FindTerrainMaps(source);
            _mapSourceLabel.Text = _terrainMaps.Count > 0
                ? _terrainDataFolder ?? _source.SourcePath ?? "Current source"
                : _terrainDataFolder == null
                    ? "Terrain data not found automatically."
                    : $"No WM*.MAP files found in {_terrainDataFolder}";
            _terrainTitle.Text = $"Terrain maps ({_terrainMaps.Count:N0})";
            RefreshTerrainList();
            if (selectFirst && _terrainList.Items.Count > 0) {
                _terrainList.SelectedItem = _terrainMaps.FirstOrDefault(map => map.FileName.Equals(selectedFile, StringComparison.OrdinalIgnoreCase))
                    ?? _terrainList.Items[0];
            }
            if (_terrainMaps.Count == 0)
                _terrainPreview.ShowMessage("FF7 terrain maps are separate WM*.MAP files. CrossSlash looked beside the selected archive and in Steam libraries. Use Locate terrain data to select the game's data\\wm folder.");
        }

        private void RefreshTerrainList() {
            string? selectedFile = (_terrainList.SelectedItem as TerrainMap)?.FileName;
            List<TerrainMap> visible = _terrainMaps
                .Where(map => map.Label.Contains(_terrainSearch.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _terrainList.ItemsSource = visible;
            _terrainList.DisplayMemberPath = nameof(TerrainMap.Label);
            if (selectedFile != null)
                _terrainList.SelectedItem = visible.FirstOrDefault(map => map.FileName.Equals(selectedFile, StringComparison.OrdinalIgnoreCase));
            if (_terrainList.SelectedIndex < 0 && visible.Count > 0)
                _terrainList.SelectedIndex = 0;
        }

        private void SelectFirstTerrainMap() {
            if (_terrainList.SelectedIndex < 0 && _terrainList.Items.Count > 0)
                _terrainList.SelectedIndex = 0;
        }

        private void ScheduleTerrainPreview() {
            _terrainPreviewTimer.Stop();
            _terrainPreviewTimer.Start();
        }

        private void RefreshTerrainPreview() {
            if (_terrainList.SelectedItem is not TerrainMap map) {
                _terrainPreview.ShowMessage(_terrainMaps.Count == 0
                    ? "No WM*.MAP files were found. Use Locate terrain data to select FF7's data\\wm folder."
                    : "Select a terrain map to preview it.");
                return;
            }
            DataSource source = _mapSource;
            _terrainPreview.ShowModel(
                () => WorldMapTerrainExporter.BuildScene(source, map.FileName, map.BlockCount, CancellationToken.None, null),
                Path.GetFileNameWithoutExtension(map.FileName).ToUpperInvariant(),
                cameraZoom: 1.0f);
        }

        private async Task ExportSelectedTerrainAsync() {
            try {
                if (_terrainList.SelectedItem is not TerrainMap map)
                    throw new InvalidOperationException("Select a terrain map first.");
                string? outputFile = NativeFilePicker.SaveGlb(PreferencesStore.Load().LastOutputDirectory, this);
                if (outputFile == null)
                    return;
                DataSource source = _mapSource;
                string result = await BatchExportRunner.RunAsync(this, $"Exporting {map.Label}", map.BlockCount,
                    (progress, cancellationToken) => {
                        ModelRoot model = WorldMapTerrainExporter.BuildScene(source, map.FileName, map.BlockCount, cancellationToken, progress);
                        cancellationToken.ThrowIfCancellationRequested();
                        model.SaveGLB(outputFile);
                        return $"Exported {Path.GetFileNameWithoutExtension(map.FileName).ToUpperInvariant()} terrain to:\n{outputFile}";
                    });
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Terrain export", result);
            } catch (OperationCanceledException) {
                DesktopUi.ShowInfo(this, "Terrain export", "Export cancelled.");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Terrain export failed", ex.Message);
            }
        }

        private async Task ExportAllTerrainAsync() {
            try {
                TerrainMap[] maps = _terrainMaps.ToArray();
                if (maps.Length == 0)
                    throw new InvalidOperationException("No terrain maps are available. Use Locate terrain data to select FF7's data\\wm folder.");
                string? outputDirectory = NativeFilePicker.OpenFolder("Choose a folder for all world terrain exports", PreferencesStore.Load().LastOutputDirectory, this);
                if (outputDirectory == null)
                    return;
                DataSource source = _mapSource;
                string result = await BatchExportRunner.RunAsync(this, "Exporting all world terrain maps", maps.Length,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < maps.Length; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            TerrainMap map = maps[index];
                            progress.Report(new BatchProgressUpdate(index, maps.Length, $"Exporting {map.Label}…"));
                            try {
                                ModelRoot model = WorldMapTerrainExporter.BuildScene(source, map.FileName, map.BlockCount, cancellationToken, null);
                                cancellationToken.ThrowIfCancellationRequested();
                                string outputFile = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(map.FileName).ToUpperInvariant()}.glb");
                                model.SaveGLB(outputFile);
                                exported++;
                            } catch (OperationCanceledException) {
                                throw;
                            } catch (Exception ex) {
                                failures.Add($"{map.Label}: {ex.Message}");
                            }
                            progress.Report(new BatchProgressUpdate(index + 1, maps.Length, $"Processed {map.Label}"));
                        }
                        string status = cancellationToken.IsCancellationRequested ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {maps.Length} terrain maps to:\n{outputDirectory}";
                        return failures.Count == 0 ? summary : summary + "\n\nFailed:\n" + string.Join("\n", failures.Take(20));
                    });
                PreferencesStore.RememberOutputDirectory(outputDirectory);
                DesktopUi.ShowInfo(this, "Export all terrain maps", result);
            } catch (OperationCanceledException) {
                DesktopUi.ShowInfo(this, "Export all terrain maps", "Export cancelled.");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Terrain export failed", ex.Message);
            }
        }

        private void FilterLandmarks() {
            WorldModel? selected = _landmarkList.SelectedItem as WorldModel;
            List<WorldModel> visible = _landmarks
                .Where(model => model.DisplayName.Contains(_landmarkSearch.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _landmarkTitle.Text = $"Landmarks ({visible.Count:N0})";
            _landmarkList.ItemsSource = visible;
            if (selected != null)
                _landmarkList.SelectedItem = visible.FirstOrDefault(model => model.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
            if (_landmarkList.SelectedIndex < 0 && visible.Count > 0)
                _landmarkList.SelectedIndex = 0;
        }

        private void RefreshLandmarkAnimations() {
            WorldModel? model = _landmarkList.SelectedItem as WorldModel;
            _currentLandmarkAnimations = model?.AnimationIds.ToList() ?? new List<string>();
            _landmarkAnimationTitle.Text = $"Compatible animations ({_currentLandmarkAnimations.Count:N0})";
            RefreshLandmarkAnimationList();
        }

        private void RefreshLandmarkAnimationList() {
            List<string> visible = _currentLandmarkAnimations
                .Where(file => file.Contains(_landmarkAnimationSearch.Text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _landmarkAnimationList.UnselectAll();
            _landmarkAnimationList.ItemsSource = visible;
            foreach (string animation in visible) {
                if (_selectedLandmarkAnimations.Contains(animation))
                    _landmarkAnimationList.SelectedItems.Add(animation);
            }
        }

        private static bool TryGetListItem(ListBox list, DependencyObject? source, out string value) {
            value = string.Empty;
            if (source == null)
                return false;
            ListBoxItem? item = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
            if (item?.Content is not string content)
                return false;
            value = content;
            return true;
        }

        private void ScheduleLandmarkPreview() {
            _landmarkPreviewTimer.Stop();
            _landmarkPreviewTimer.Start();
        }

        private void RefreshLandmarkPreview() {
            if (_landmarkList.SelectedItem is not WorldModel model) {
                _landmarkPreview.ShowMessage(_landmarks.Count == 0
                    ? "No supported landmark models were found. Choose world_us.lgp or a folder containing its extracted models."
                    : "Select a landmark to preview it.");
                return;
            }
            string animation = _landmarkPreviewAnimation != null && _currentLandmarkAnimations.Contains(_landmarkPreviewAnimation, StringComparer.OrdinalIgnoreCase)
                ? _landmarkPreviewAnimation
                : _currentLandmarkAnimations.FirstOrDefault() ?? string.Empty;
            _landmarkPreviewAnimation = animation.Length == 0 ? null : animation;
            string[] animations = animation.Length == 0 ? Array.Empty<string>() : new[] { animation };
            ModelBaseOptions options = GetLandmarkOptions();
            DataSource source = _source;
            _landmarkPreview.ShowModel(() => BuildLandmarkScene(source, model, animations, options, includeMeshes: true, includeAnimations: true),
                animation.Length == 0 ? model.DisplayName : $"{model.DisplayName} · {Path.GetFileNameWithoutExtension(animation)}");
        }

        private void PreviewLandmarkAnimation(string animation) {
            _landmarkPreviewAnimation = animation;
            if (_landmarkList.SelectedItem is not WorldModel model)
                return;
            DataSource source = _source;
            ModelBaseOptions options = GetLandmarkOptions();
            _landmarkPreview.ShowAnimation(() => BuildLandmarkScene(source, model, new[] { animation }, options, includeMeshes: true, includeAnimations: true),
                Path.GetFileNameWithoutExtension(animation));
        }

        private ModelRoot BuildLandmarkScene(DataSource source, WorldModel landmark, IEnumerable<string> animations, ModelBaseOptions options,
            bool includeMeshes, bool includeAnimations) {
            var exporter = new FieldModel(source, options);
            string[] animationFiles = animations.ToArray();
            string[] poseAnimations = animationFiles.Length > 0 ? animationFiles : landmark.AnimationIds.Take(1).ToArray();
            ModelRoot model = exporter.BuildScene(landmark.HrcFile, poseAnimations, compatibleAnimationsOnly: true, out _,
                includeMeshes: includeMeshes, includeAnimations: includeAnimations,
                useFirstAnimationFrameAsBindPose: true, useSkinnedMeshes: false);
            FieldExportWindow.AddRootContainerMarker(model);
            return model;
        }

        private IEnumerable<string> GetSelectedOrAllLandmarkAnimations() {
            string[] selected = _selectedLandmarkAnimations.Where(_currentLandmarkAnimations.Contains).ToArray();
            return selected.Length > 0 ? selected : _currentLandmarkAnimations;
        }

        private async Task ExportSelectedLandmarkAsync() {
            try {
                if (_landmarkList.SelectedItem is not WorldModel landmark)
                    throw new InvalidOperationException("Select a landmark first.");
                if (!TryGetLandmarkScale(out _))
                    throw new InvalidOperationException("Enter a valid positive scale.");
                string? outputFile = NativeFilePicker.SaveGlb(PreferencesStore.Load().LastOutputDirectory, this);
                if (outputFile == null)
                    return;
                string[] animations = _landmarkEmbedAnimations.IsChecked == true ? GetSelectedOrAllLandmarkAnimations().ToArray() : Array.Empty<string>();
                bool prefix = _landmarkPrefixAnimations.IsChecked == true;
                bool embed = _landmarkEmbedAnimations.IsChecked == true;
                ModelBaseOptions options = GetLandmarkOptions();
                DataSource source = _source;
                ModelRoot model = await Task.Run(() => BuildLandmarkScene(source, landmark, animations, options, true, embed));
                AnimationExportNaming.PrefixAnimationNames(model, Path.GetFileNameWithoutExtension(landmark.HrcFile), prefix);
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Landmark export", $"Exported {landmark.Name} to:\n{outputFile}\nAnimations included: {model.LogicalAnimations.Count}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Landmark export failed", ex.Message);
            }
        }

        private async Task ExportAllLandmarksAsync() {
            try {
                if (_landmarks.Count == 0)
                    throw new InvalidOperationException("No supported landmark models were found in the selected source.");
                if (!TryGetLandmarkScale(out _))
                    throw new InvalidOperationException("Enter a valid positive scale.");
                string? outputDirectory = NativeFilePicker.OpenFolder("Choose a folder for all world landmark exports", PreferencesStore.Load().LastOutputDirectory, this);
                if (outputDirectory == null)
                    return;
                WorldModel[] landmarks = _landmarks.ToArray();
                ModelBaseOptions options = GetLandmarkOptions();
                bool prefix = _landmarkPrefixAnimations.IsChecked == true;
                bool embed = _landmarkEmbedAnimations.IsChecked == true;
                DataSource source = _source;
                string result = await BatchExportRunner.RunAsync(this, "Exporting world landmarks", landmarks.Length,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < landmarks.Length; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            WorldModel landmark = landmarks[index];
                            progress.Report(new BatchProgressUpdate(index, landmarks.Length, $"Exporting {landmark.Name}…"));
                            try {
                                ModelRoot model = BuildLandmarkScene(source, landmark, embed ? landmark.AnimationIds : Array.Empty<string>(), options, true, embed);
                                AnimationExportNaming.PrefixAnimationNames(model, landmark.Id, prefix);
                                model.SaveGLB(Path.Combine(outputDirectory, $"{landmark.Name} ({landmark.Id}).glb"));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{landmark.Name}: {ex.Message}");
                            }
                            progress.Report(new BatchProgressUpdate(index + 1, landmarks.Length, $"Processed {landmark.Name}"));
                        }
                        string status = cancellationToken.IsCancellationRequested ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {landmarks.Length} landmarks to:\n{outputDirectory}";
                        return failures.Count == 0 ? summary : summary + "\n\nFailed:\n" + string.Join("\n", failures.Take(20));
                    });
                PreferencesStore.RememberOutputDirectory(outputDirectory);
                DesktopUi.ShowInfo(this, "Export all landmarks", result);
            } catch (OperationCanceledException) {
                DesktopUi.ShowInfo(this, "Export all landmarks", "Export cancelled.");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Landmark export failed", ex.Message);
            }
        }

        private async Task ExportLandmarkAnimationsAsync() {
            try {
                if (_landmarks.Count == 0)
                    throw new InvalidOperationException("No supported landmark models were found in the selected source.");
                string? outputDirectory = NativeFilePicker.OpenFolder("Choose a folder for separate landmark animations", PreferencesStore.Load().LastOutputDirectory, this);
                if (outputDirectory == null)
                    return;
                string animationDirectory = Path.Combine(outputDirectory, "LandmarkAnimations");
                WorldModel[] landmarks = _landmarks.ToArray();
                var exports = landmarks.SelectMany(landmark => landmark.AnimationIds.Select(animation => (Landmark: landmark, Animation: animation))).ToArray();
                if (exports.Length == 0)
                    throw new InvalidOperationException("No compatible landmark animations were found.");
                ModelBaseOptions options = GetLandmarkOptions();
                bool prefix = _landmarkPrefixAnimations.IsChecked == true;
                DataSource source = _source;
                string result = await BatchExportRunner.RunAsync(this, "Exporting landmark animations", exports.Length,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        var failures = new List<string>();
                        Directory.CreateDirectory(animationDirectory);
                        for (int index = 0; index < exports.Length; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            (WorldModel landmark, string animation) = exports[index];
                            progress.Report(new BatchProgressUpdate(index, exports.Length, $"Exporting {landmark.Name} · {animation}…"));
                            try {
                                ModelRoot model = BuildLandmarkScene(source, landmark, new[] { animation }, options, includeMeshes: false, includeAnimations: true);
                                AnimationExportNaming.PrefixAnimationNames(model, landmark.Id, prefix);
                                string name = Path.GetFileNameWithoutExtension(animation);
                                string filename = prefix ? $"{landmark.Id}_{name}.glb" : $"{name}.glb";
                                model.SaveGLB(Path.Combine(animationDirectory, filename));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{landmark.Name}/{animation}: {ex.Message}");
                            }
                            progress.Report(new BatchProgressUpdate(index + 1, exports.Length, $"Processed {landmark.Name} · {animation}"));
                        }
                        string status = cancellationToken.IsCancellationRequested ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {exports.Length} landmark animations to:\n{animationDirectory}";
                        return failures.Count == 0 ? summary : summary + "\n\nFailed:\n" + string.Join("\n", failures.Take(20));
                    });
                PreferencesStore.RememberOutputDirectory(outputDirectory);
                DesktopUi.ShowInfo(this, "Export landmark animations", result);
            } catch (OperationCanceledException) {
                DesktopUi.ShowInfo(this, "Export landmark animations", "Export cancelled.");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Landmark animation export failed", ex.Message);
            }
        }
        private async Task ExportSelectedVehicleAsync() {
            try {
                if (_vehicleList.SelectedItem is not WorldModel vehicle)
                    throw new InvalidOperationException("Select a vehicle first.");
                if (!TryGetScale(out _))
                    throw new InvalidOperationException("Enter a valid positive scale.");
                string? outputFile = NativeFilePicker.SaveGlb(PreferencesStore.Load().LastOutputDirectory, this);
                if (outputFile == null)
                    return;
                string[] animations = _embedAnimations.IsChecked == true ? GetSelectedOrAllAnimations().ToArray() : Array.Empty<string>();
                bool prefix = _prefixAnimations.IsChecked == true;
                bool embed = _embedAnimations.IsChecked == true;
                ModelBaseOptions options = GetOptions();
                DataSource source = _source;
                ModelRoot model = await Task.Run(() => BuildVehicleScene(source, vehicle, animations, options, true, embed));
                AnimationExportNaming.PrefixAnimationNames(model, Path.GetFileNameWithoutExtension(vehicle.HrcFile), prefix);
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Vehicle export", $"Exported {vehicle.Name} to:\n{outputFile}\nAnimations included: {model.LogicalAnimations.Count}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Vehicle export failed", ex.Message);
            }
        }

        private async Task ExportAllVehiclesAsync() {
            try {
                if (_vehicles.Count == 0)
                    throw new InvalidOperationException("No supported world vehicle models were found in the selected source.");
                string? outputDirectory = NativeFilePicker.OpenFolder("Choose a folder for all world vehicle exports", PreferencesStore.Load().LastOutputDirectory, this);
                if (outputDirectory == null)
                    return;
                DataSource source = _source;
                WorldModel[] vehicles = _vehicles.ToArray();
                ModelBaseOptions options = GetOptions();
                bool prefix = _prefixAnimations.IsChecked == true;
                bool embed = _embedAnimations.IsChecked == true;
                string result = await BatchExportRunner.RunAsync(this, "Exporting world vehicles", vehicles.Length,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < vehicles.Length; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            WorldModel vehicle = vehicles[index];
                            progress.Report(new BatchProgressUpdate(index, vehicles.Length, $"Exporting {vehicle.Name}…"));
                            try {
                                ModelRoot model = BuildVehicleScene(source, vehicle, embed ? vehicle.AnimationIds : Array.Empty<string>(), options, true, embed);
                                AnimationExportNaming.PrefixAnimationNames(model, Path.GetFileNameWithoutExtension(vehicle.HrcFile), prefix);
                                model.SaveGLB(Path.Combine(outputDirectory, $"{vehicle.Name} ({vehicle.Id}).glb"));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{vehicle.Name}: {ex.Message}");
                            }
                            progress.Report(new BatchProgressUpdate(index + 1, vehicles.Length, $"Processed {vehicle.Name}"));
                        }
                        string status = cancellationToken.IsCancellationRequested ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {vehicles.Length} vehicles to:\n{outputDirectory}";
                        return failures.Count == 0 ? summary : summary + "\n\nFailed:\n" + string.Join("\n", failures.Take(20));
                    });
                PreferencesStore.RememberOutputDirectory(outputDirectory);
                DesktopUi.ShowInfo(this, "Export all world vehicles", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Vehicle export failed", ex.Message);
            }
        }

        private async Task ExportVehicleAnimationsAsync() {
            try {
                var exports = _vehicles.SelectMany(vehicle => vehicle.AnimationIds.Select(animation => (Vehicle: vehicle, Animation: animation))).ToArray();
                if (exports.Length == 0)
                    throw new InvalidOperationException("No compatible vehicle animations were found in the selected source.");
                if (!TryGetScale(out _))
                    throw new InvalidOperationException("Enter a valid positive scale.");
                string? outputDirectory = NativeFilePicker.OpenFolder("Choose a folder for standalone vehicle animations", PreferencesStore.Load().LastOutputDirectory, this);
                if (outputDirectory == null)
                    return;
                string animationDirectory = Path.Combine(outputDirectory, "Animations");
                Directory.CreateDirectory(animationDirectory);
                DataSource source = _source;
                ModelBaseOptions options = GetOptions();
                bool prefix = _prefixAnimations.IsChecked == true;
                string result = await BatchExportRunner.RunAsync(this, "Exporting vehicle animations", exports.Length,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < exports.Length; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            (WorldModel vehicle, string animation) = exports[index];
                            progress.Report(new BatchProgressUpdate(index, exports.Length, $"Exporting {vehicle.Name}: {Path.GetFileNameWithoutExtension(animation)}…"));
                            try {
                                ModelRoot model = BuildVehicleScene(source, vehicle, new[] { animation }, options, includeMeshes: false, includeAnimations: true);
                                string vehicleName = Path.GetFileNameWithoutExtension(vehicle.HrcFile);
                                AnimationExportNaming.PrefixAnimationNames(model, vehicleName, prefix);
                                string vehicleAnimationDirectory = Path.Combine(animationDirectory, vehicleName);
                                Directory.CreateDirectory(vehicleAnimationDirectory);
                                string animationName = Path.GetFileNameWithoutExtension(animation);
                                string filename = prefix ? $"{vehicleName}_{animationName}.glb" : $"{animationName}.glb";
                                model.SaveGLB(Path.Combine(vehicleAnimationDirectory, filename));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{vehicle.Name}/{animation}: {ex.Message}");
                            }
                            progress.Report(new BatchProgressUpdate(index + 1, exports.Length, $"Processed {vehicle.Name}"));
                        }
                        string status = cancellationToken.IsCancellationRequested ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {exports.Length} vehicle animations to:\n{animationDirectory}";
                        return failures.Count == 0 ? summary : summary + "\n\nFailed:\n" + string.Join("\n", failures.Take(20));
                    });
                PreferencesStore.RememberOutputDirectory(outputDirectory);
                DesktopUi.ShowInfo(this, "Export vehicle animations", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Vehicle animation export failed", ex.Message);
            }
        }

        private IEnumerable<string> GetSelectedOrAllAnimations() {
            string[] selected = _selectedAnimations.Where(_currentAnimations.Contains).ToArray();
            return selected.Length > 0 ? selected : _currentAnimations;
        }
    }

    internal static class WorldMapTerrainExporter {
        public const int BlockByteLength = 0xB800;
        private const int BlockSize = 32768;

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

        public static ModelRoot BuildScene(DataSource source, string mapFile, int blockCount,
            CancellationToken cancellationToken, IProgress<BatchProgressUpdate>? progress) {
            string mapName = Path.GetFileNameWithoutExtension(mapFile).ToUpperInvariant();
            int mapId = mapName switch {
                "WM0" => 0,
                "WM2" => 2,
                "WM3" => 3,
                _ => throw new ArgumentException($"Unsupported world terrain map '{mapName}'. Expected WM0, WM2 or WM3.", nameof(mapFile)),
            };
            int sectionsAcross = mapId switch { 0 => 9, 2 => 3, 3 => 2, _ => 0 };
            int maxSections = mapId switch { 0 => 63, 2 => 12, 3 => 4, _ => 0 };
            int sectionsToExport = Math.Min(blockCount, maxSections);
            var allFiles = source.AllFiles
                .Select(file => Path.GetFileName(file))
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(file => file, file => file, StringComparer.OrdinalIgnoreCase);
            var textureImages = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var materialBuilders = new Dictionary<string, MaterialBuilder>(StringComparer.OrdinalIgnoreCase);
            var meshBuilders = new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>>(StringComparer.OrdinalIgnoreCase);
            using (var map = new WorldMapMesh(source.Open(mapFile))) {
                for (int block = 0; block < sectionsToExport; block++) {
                    cancellationToken.ThrowIfCancellationRequested();
                    int blockX = block % sectionsAcross;
                    int blockZ = block / sectionsAcross;
                    foreach (MapSector sector in map.ExportBlock(block)) {
                        foreach (MapTri triangle in sector.Triangles) {
                            if (triangle.Vert0 >= sector.Vertices.Count || triangle.Vert1 >= sector.Vertices.Count || triangle.Vert2 >= sector.Vertices.Count)
                                continue;

                            string textureName;
                            Vector2 uv0, uv1, uv2;
                            bool underwaterOutside = mapId == 2 && triangle.Texture == 0 && triangle.U0 == 254;
                            if (underwaterOutside) {
                                // Landscaper leaves these cltr triangles untextured; U=254 marks
                                // the water outside the underwater map's normal textured area.
                                textureName = "underwater-outside";
                                uv0 = uv1 = uv2 = Vector2.Zero;
                            } else try {
                                triangle.GetCorrectedTextureInfo(mapId, out textureName, out uv0, out uv1, out uv2);
                            } catch {
                                textureName = $"missing-texture-{triangle.Texture}";
                                uv0 = new Vector2(triangle.U0 / 256f, triangle.V0 / 256f);
                                uv1 = new Vector2(triangle.U1 / 256f, triangle.V1 / 256f);
                                uv2 = new Vector2(triangle.U2 / 256f, triangle.V2 / 256f);
                            }
                            if (!meshBuilders.TryGetValue(textureName, out var mesh)) {
                                mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>($"World terrain · {textureName}");
                                meshBuilders.Add(textureName, mesh);
                                materialBuilders[textureName] = CreateMaterial(source, textureName, allFiles, textureImages);
                            }

                            var primitive = mesh.UsePrimitive(materialBuilders[textureName]);
                            MapVert v0 = sector.Vertices[triangle.Vert0];
                            MapVert v1 = sector.Vertices[triangle.Vert1];
                            MapVert v2 = sector.Vertices[triangle.Vert2];
                            MapVert n0 = GetNormal(sector, triangle.Vert0);
                            MapVert n1 = GetNormal(sector, triangle.Vert1);
                            MapVert n2 = GetNormal(sector, triangle.Vert2);
                            primitive.AddTriangle(
                                MakeVertex(v0, n0, uv0, sector, blockX, blockZ),
                                MakeVertex(v1, n1, uv1, sector, blockX, blockZ),
                                MakeVertex(v2, n2, uv2, sector, blockX, blockZ));
                        }
                    }
                    progress?.Report(new BatchProgressUpdate(block + 1, sectionsToExport, $"Read {mapName} terrain section {block + 1} of {sectionsToExport}"));
                }
            }

            var builder = new SceneBuilder();
            foreach (var mesh in meshBuilders)
                builder.AddRigidMesh(mesh.Value, new NodeBuilder(mesh.Key));
            ModelRoot model = builder.ToGltf2();
            model.DefaultScene = model.LogicalScenes.FirstOrDefault() ?? model.UseScene($"FF7 World Map {mapName} Terrain");
            model.Asset.Generator = "CrossSlash";
            return model;
        }

        private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin> MakeVertex(
            MapVert vertex, MapVert sourceNormal, Vector2 uv, MapSector sector, int blockX, int blockZ) {
            Vector3 normal = new(sourceNormal.X, sourceNormal.Y, sourceNormal.Z);
            normal = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.UnitY;
            Vector3 position = new(
                vertex.X + sector.OffsetX + blockX * BlockSize,
                vertex.Y,
                vertex.Z + sector.OffsetY + blockZ * BlockSize);
            return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexNoSkin>(
                new VertexPositionNormal(position, normal),
                new VertexTexture1(uv));
        }

        private static MapVert GetNormal(MapSector sector, int vertexIndex) =>
            vertexIndex < sector.Normals.Count ? sector.Normals[vertexIndex] : new MapVert();

        private static MaterialBuilder CreateMaterial(DataSource source, string textureName, IReadOnlyDictionary<string, string> files,
            IDictionary<string, byte[]> images) {
            var material = new MaterialBuilder($"World map · {textureName}")
                .WithUnlitShader()
                .WithDoubleSide(true)
                .WithAlpha(SharpGLTF.Materials.AlphaMode.MASK);
            if (textureName.Equals("underwater-outside", StringComparison.OrdinalIgnoreCase))
                return material.WithAlpha(SharpGLTF.Materials.AlphaMode.OPAQUE).WithBaseColor(Vector4.One);
            string textureFile = textureName + ".tex";
            if (!files.TryGetValue(textureFile, out string? resolved))
                return material;
            try {
                if (!images.TryGetValue(textureName, out byte[]? png)) {
                    using Stream stream = source.Open(resolved);
                    using var bitmap = new TexFile(stream).ToBitmap(0);
                    using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    png = encoded.ToArray();
                    images[textureName] = png;
                }
                material.WithChannelImage(KnownChannel.BaseColor, new MemoryImage(png));
                material.GetChannel(KnownChannel.BaseColor).Texture.WithSampler(
                    TextureWrapMode.REPEAT,
                    TextureWrapMode.REPEAT,
                    TextureMipMapFilter.NEAREST,
                    TextureInterpolationFilter.NEAREST);
            } catch {
                // Keep the triangles visible with the material's default colour if a texture is malformed.
            }
            return material;
        }
    }

}
