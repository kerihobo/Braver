// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace CrossSlash {

    public class BattleExport : ExportKind {
        private BattleModelOptions _options = new BattleModelOptions();

        public override string Help =>
@"Usage: CrossSlash BattleExport [SourceLGPOrFolder] [OutputFile] [ModelCode] [options]
        ModelCode can be a two letter battle model code (e.g. RV), a battlefield code/file (e.g. px or pxaa), or a magic model code (e.g. cyvadat)
    Valid options:
        /ConvertSRGBToLinear - convert colours to linear RGB
        /SwapWinding - reverse triangle winding order
        /BakeVertexColours - export vertex colours as a texture
        /Scale:value - scale model up/down from default size
    For example:
        CrossSlash BattleModel C:\games\FF7\data\battle\magic.lgp C:\temp\shiva.glb cyvadat /Scale:0.1
";

        public override object Config => _options;
        public override bool HasGui => true;
        public override string Name => "Battle Model Export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            var exporter = new BattleModel(source, _options);
            var model = exporter.BuildSceneAuto(parameters.First());
            model.SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new BattleExportWindow(source);
    }

    public class BattleExportWindow : Window {
        private DataSource _source;
        private bool _magicModels;
        private CheckBox _chkSRGB, _chkSwapWinding, _chkBakeColours, _chkEmbedAnimations;
        private readonly WrapPanel _exportOptions;
        private StackPanel? _optionsPanel;
        private readonly ModelPreviewControl _modelPreview;
        private readonly DispatcherTimer _previewTimer;
        private Grid? _previewHost;
        private Grid? _battleModelPreviewHost, _battlefieldPreviewHost;
        private bool _previewBattlefield;
        private readonly TextBox _txtModelSearch, _txtScale, _txtBattlefield;
        private readonly ListBox _lstModels, _lstBattlefields;
        private readonly AnimationPickerState _battleAnimations;
        private AnimationPickerState? _activeAnimations;
        private readonly List<string> _modelCodes;
        private readonly List<string> _battlefieldCodes;
        private readonly ICollectionView _modelView;
        private ListBox? _activeMagicModelList;
        private IReadOnlyList<string> _activeMagicModelCodes = Array.Empty<string>();
        private string _activeMagicModelGroup = "Magic";

        public BattleExportWindow(DataSource source, bool magicModels = false) {
            _source = source;
            _magicModels = magicModels;
            _modelPreview = new ModelPreviewControl { Height = 340 };
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _previewTimer.Tick += (_, _) => {
                _previewTimer.Stop();
                RefreshPreview();
            };
            Closed += (_, _) => _previewTimer.Stop();
            _modelCodes = magicModels
                ? ModelBatchDiscovery.FindMagicModels(source)
                : ModelBatchDiscovery.FindBattleModels(source);
            _battlefieldCodes = magicModels ? new List<string>() : ModelBatchDiscovery.FindBattleLocations(source);
            _txtModelSearch = DesktopUi.CreateSearchTextBox("Search available models");
            _modelView = CollectionViewSource.GetDefaultView(_modelCodes);
            _modelView.Filter = item => item is string code &&
                (string.IsNullOrWhiteSpace(_txtModelSearch.Text) || code.Contains(_txtModelSearch.Text, StringComparison.OrdinalIgnoreCase));
            _txtModelSearch.TextChanged += (_, _) => _modelView.Refresh();
            _txtScale = DesktopUi.CreateTextBox(magicModels ? "0.1" : "1");
            _txtBattlefield = DesktopUi.CreateTextBox();
            _lstModels = new ListBox {
                ItemsSource = _modelView,
                MinHeight = 150,
                MaxHeight = 230,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            if (_lstModels.Items.Count > 0)
                _lstModels.SelectedIndex = 0;
            _battleAnimations = CreateAnimationPickerState();
            _lstBattlefields = new ListBox {
                ItemsSource = _battlefieldCodes,
                MinHeight = 150,
                MaxHeight = 230,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            _lstBattlefields.SelectionChanged += (_, _) => {
                if (_lstBattlefields.SelectedItem is string code)
                    _txtBattlefield.Text = code;
            };
            if (_battlefieldCodes.Count > 0) {
                _lstBattlefields.SelectedIndex = 0;
                _txtBattlefield.Text = _battlefieldCodes[0];
            }
            _chkSRGB = new CheckBox { Content = "Convert colours from sRGB to linear", IsChecked = true, Margin = new Thickness(0, 0, 18, 8) };
            _chkSwapWinding = new CheckBox { Content = "Swap triangle winding", Margin = new Thickness(0, 0, 18, 8) };
            _chkBakeColours = new CheckBox { Content = "Bake vertex colours to texture", Margin = new Thickness(0, 0, 18, 8) };
            _chkEmbedAnimations = new CheckBox { Content = "Embed animations", IsChecked = true, Margin = new Thickness(0, 0, 18, 8) };
            _chkSRGB.Checked += (_, _) => SchedulePreview();
            _chkSRGB.Unchecked += (_, _) => SchedulePreview();
            _chkSwapWinding.Checked += (_, _) => SchedulePreview();
            _chkSwapWinding.Unchecked += (_, _) => SchedulePreview();
            _chkBakeColours.Checked += (_, _) => SchedulePreview();
            _chkBakeColours.Unchecked += (_, _) => SchedulePreview();
            _txtScale.TextChanged += (_, _) => SchedulePreview();
            _exportOptions = new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { DesktopUi.CreateScaleOption(_txtScale), _chkSRGB, _chkSwapWinding, _chkBakeColours, _chkEmbedAnimations },
            };

            var content = new StackPanel();

            if (_magicModels)
            {
                var magicTabs = CreateMagicModelContent();
                magicTabs.SelectionChanged += (_, _) => ActivateMagicModelTab(magicTabs);
                content.Children.Add(magicTabs);
                ActivateMagicModelTab(magicTabs);
            }
            else {
                var tabs = new TabControl { MinHeight = 430 };
                tabs.Items.Add(new TabItem { Header = "Battle Models", Content = CreateBattleModelContent() });
                tabs.Items.Add(new TabItem { Header = "Battlefields", Content = CreateBattlefieldContent() });
                tabs.SelectionChanged += (_, _) => MoveExportOptionsToSelectedTab(tabs);
                content.Children.Add(tabs);
                MoveExportOptionsToSelectedTab(tabs);
            }

            DesktopUi.ConfigureWindow(
                this,
                content,
                width: 1120,
                height: 820
            );
        }

        private UIElement CreateBattleModelContent() {
            var panel = new StackPanel();

            var columns = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.28, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.30, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
            var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            modelColumn.Children.Add(DesktopUi.CreateSectionTitle($"Models ({_modelCodes.Count:N0})"));
            modelColumn.Children.Add(CreateModelPicker());
            columns.Children.Add(modelColumn);

            var animationColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            animationColumn.Children.Add(_battleAnimations.Title);
            animationColumn.Children.Add(DesktopUi.CreateSearchField("Search animations…", _battleAnimations.Search));
            animationColumn.Children.Add(_battleAnimations.List);
            Grid.SetColumn(animationColumn, 1);
            columns.Children.Add(animationColumn);

            var previewColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            previewColumn.Children.Add(DesktopUi.CreateSectionTitle("Preview"));
            var previewHost = new Grid { Height = 340 };
            previewColumn.Children.Add(previewHost);
            Grid.SetColumn(previewColumn, 2);
            columns.Children.Add(previewColumn);
            _battleModelPreviewHost = previewHost;
            MovePreviewTo(previewHost);

            _lstModels.SelectionChanged += (_, _) => {
                RefreshAnimationPicker(_battleAnimations, _lstModels.SelectedItem as string);
                SchedulePreview();
            };
            if (_lstModels.SelectedItem is string) {
                RefreshAnimationPicker(_battleAnimations, _lstModels.SelectedItem as string);
                SchedulePreview();
            }
            panel.Children.Add(CreateContentContainer(columns));

            var export = DesktopUi.CreateButton("Export model", primary: true);
            export.Click += (_, _) => BtnExport_Clicked();
            var exportAll = DesktopUi.CreateButton("Export all battle models");
            exportAll.Click += (_, _) => BtnExportAll_Clicked();
            var exportAnimations = DesktopUi.CreateButton("Export all animations");
            exportAnimations.Click += (_, _) => BtnExportAllAnimations_Clicked();
            panel.Children.Add(DesktopUi.CreateActionRow(export, exportAll, exportAnimations));
            return panel;
        }

        private UIElement CreateBattlefieldContent() {
            var panel = new StackPanel();

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.36, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.64, GridUnitType.Star) });
            var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            modelColumn.Children.Add(DesktopUi.CreateSectionTitle($"Battlefields ({_battlefieldCodes.Count:N0})"));
            modelColumn.Children.Add(_lstBattlefields);
            modelColumn.Children.Add(DesktopUi.CreateFormRow("Location code", _txtBattlefield));
            columns.Children.Add(modelColumn);

            var previewColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            previewColumn.Children.Add(DesktopUi.CreateSectionTitle("Preview"));
            var previewHost = new Grid { Height = 340 };
            previewColumn.Children.Add(previewHost);
            Grid.SetColumn(previewColumn, 1);
            columns.Children.Add(previewColumn);
            _battlefieldPreviewHost = previewHost;
            _txtBattlefield.TextChanged += (_, _) => {
                if (_previewBattlefield)
                    SchedulePreview();
            };
            panel.Children.Add(CreateContentContainer(columns));

            var export = DesktopUi.CreateButton("Export battlefield", primary: true);
            export.Click += (_, _) => BtnExportBattlefield_Clicked();
            var exportAll = DesktopUi.CreateButton("Export all battlefields");
            exportAll.Click += (_, _) => BtnExportAllBattlefields_Clicked();
            panel.Children.Add(DesktopUi.CreateActionRow(export, exportAll));
            return panel;
        }

        private TabControl CreateMagicModelContent() {
            var tabs = new TabControl { MinHeight = 430 };
            tabs.Items.Add(CreateMagicModelTab("Magic", ModelBatchDiscovery.FindNonSummonMagicModels(_modelCodes)));
            tabs.Items.Add(CreateMagicModelTab("Summon", ModelBatchDiscovery.FindSummonModels(_modelCodes)));
            return tabs;
        }

        private TabItem CreateMagicModelTab(string group, List<string> modelCodes) {
            var search = DesktopUi.CreateSearchTextBox($"Search {group.ToLowerInvariant()} models");
            var view = CollectionViewSource.GetDefaultView(modelCodes);
            view.Filter = item => item is string code &&
                (string.IsNullOrWhiteSpace(search.Text) || code.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            search.TextChanged += (_, _) => view.Refresh();

            var modelList = new ListBox {
                ItemsSource = view,
                MinHeight = 150,
                MaxHeight = 230,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            if (modelList.Items.Count > 0)
                modelList.SelectedIndex = 0;

            AnimationPickerState animations = CreateAnimationPickerState();
            var panel = new StackPanel();
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.28, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.30, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });

            var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            modelColumn.Children.Add(DesktopUi.CreateSectionTitle($"Models ({modelCodes.Count:N0})"));
            modelColumn.Children.Add(DesktopUi.CreateSearchField($"Search {group.ToLowerInvariant()} models…", search));
            modelColumn.Children.Add(modelList);
            columns.Children.Add(modelColumn);

            var animationColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            animationColumn.Children.Add(animations.Title);
            animationColumn.Children.Add(DesktopUi.CreateSearchField("Search animations…", animations.Search));
            animationColumn.Children.Add(animations.List);
            Grid.SetColumn(animationColumn, 1);
            columns.Children.Add(animationColumn);

            var previewColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            previewColumn.Children.Add(DesktopUi.CreateSectionTitle("Preview"));
            var previewHost = new Grid { Height = 340 };
            previewColumn.Children.Add(previewHost);
            Grid.SetColumn(previewColumn, 2);
            columns.Children.Add(previewColumn);
            panel.Children.Add(CreateContentContainer(columns));
            modelList.SelectionChanged += (_, _) => {
                if (ReferenceEquals(_activeAnimations, animations)) {
                    RefreshAnimationPicker(animations, modelList.SelectedItem as string);
                    SchedulePreview();
                }
            };

            var export = DesktopUi.CreateButton("Export selected", primary: true);
            export.Click += (_, _) => BtnExport_Clicked();
            var exportAll = DesktopUi.CreateButton($"Export all {group.ToLowerInvariant()} models");
            exportAll.Click += (_, _) => BtnExportAll_Clicked(modelCodes, group);
            var exportAnimations = DesktopUi.CreateButton($"Export all {group.ToLowerInvariant()} animations");
            exportAnimations.Click += (_, _) => BtnExportAllAnimations_Clicked(modelCodes, group);
            panel.Children.Add(DesktopUi.CreateActionRow(export, exportAll, exportAnimations));

            return new TabItem {
                Header = group,
                Content = panel,
                Tag = new MagicModelTabState(group, modelCodes, modelList, animations, panel, previewHost),
            };
        }

        private void ActivateMagicModelTab(TabControl tabs) {
            if (tabs.SelectedItem is not TabItem { Tag: MagicModelTabState state })
                return;

            _activeMagicModelGroup = state.Group;
            _activeMagicModelCodes = state.ModelCodes;
            _activeMagicModelList = state.ModelList;
            _activeAnimations = state.Animations;
            _previewBattlefield = false;
            MovePreviewTo(state.PreviewHost);
            RefreshAnimationPicker(state.Animations, state.ModelList.SelectedItem as string);
            MoveExportOptionsTo(state.Panel);
            SchedulePreview();
        }

        private static Border CreateContentContainer(UIElement content) => new() {
            Background = DesktopUi.SurfaceBrush,
            BorderBrush = DesktopUi.ControlBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content,
        };

        private sealed class AnimationPickerState {
            public AnimationPickerState(TextBlock title, TextBox search, ListBox list) {
                Title = title;
                Search = search;
                List = list;
            }

            public TextBlock Title { get; }
            public TextBox Search { get; }
            public ListBox List { get; }
            public IReadOnlyList<string> Names { get; set; } = Array.Empty<string>();
            public HashSet<int> SelectedIndices { get; } = new();
            public string? ModelCode { get; set; }
            public long LookupRequest;
            public bool LookupPending;
            public bool Loaded;
            public bool Refreshing;
            public int? PreviewIndex;
        }

        private sealed record MagicModelTabState(string Group, List<string> ModelCodes, ListBox ModelList, AnimationPickerState Animations, StackPanel Panel, Grid PreviewHost);

        private void MovePreviewTo(Grid host) {
            if (ReferenceEquals(_previewHost, host))
                return;
            if (_previewHost != null)
                _previewHost.Children.Remove(_modelPreview);
            host.Children.Add(_modelPreview);
            _previewHost = host;
        }

        private void MoveExportOptionsToSelectedTab(TabControl tabs) {
            if (tabs.SelectedItem is TabItem { Content: StackPanel panel } tab) {
                _previewBattlefield = Equals(tab.Header, "Battlefields");
                _chkEmbedAnimations.Visibility = _previewBattlefield
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                if (_previewBattlefield && _battlefieldPreviewHost != null) {
                    _activeAnimations = null;
                    MovePreviewTo(_battlefieldPreviewHost);
                } else if (_battleModelPreviewHost != null) {
                    MovePreviewTo(_battleModelPreviewHost);
                    _activeAnimations = _battleAnimations;
                    RefreshAnimationPicker(_battleAnimations, _lstModels.SelectedItem as string);
                }
                MoveExportOptionsTo(panel);
                SchedulePreview();
            }
        }

        private void MoveExportOptionsTo(StackPanel panel) {
            if (ReferenceEquals(_optionsPanel, panel))
                return;
            if (_optionsPanel != null)
                _optionsPanel.Children.Remove(_exportOptions);
            panel.Children.Insert(Math.Max(0, panel.Children.Count - 1), _exportOptions);
            _optionsPanel = panel;
        }

        private AnimationPickerState CreateAnimationPickerState() {
            var state = new AnimationPickerState(
                DesktopUi.CreateSectionTitle("Animations (0)"),
                DesktopUi.CreateSearchTextBox("Search animations"),
                new ListBox {
                    ItemsSource = Array.Empty<string>(),
                    SelectionMode = SelectionMode.Multiple,
                    MinHeight = 150,
                    MaxHeight = 230,
                    Background = DesktopUi.InputBackgroundBrush,
                    Foreground = DesktopUi.TextPrimaryBrush,
                    BorderBrush = DesktopUi.ControlBorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    IsEnabled = false,
                    ToolTip = "Left-click an animation to preview it. Right-click to preview it and include or exclude it from export.",
                }
            );
            state.Search.TextChanged += (_, _) => RefreshAnimationList(state);
            state.List.SelectionChanged += (_, e) => {
                if (state.Refreshing)
                    return;
                foreach (string name in e.RemovedItems.Cast<string>())
                    if (TryGetAnimationIndex(name, out int index))
                        state.SelectedIndices.Remove(index);
                foreach (string name in e.AddedItems.Cast<string>())
                    if (TryGetAnimationIndex(name, out int index))
                        state.SelectedIndices.Add(index);
            };
            state.List.PreviewMouseLeftButtonDown += (sender, e) => {
                if (!TryGetAnimationItem(state.List, e.OriginalSource as DependencyObject, out _, out string name))
                    return;
                PreviewAnimation(state, name);
                e.Handled = true;
            };
            state.List.PreviewMouseRightButtonDown += (_, e) => {
                if (!TryGetAnimationItem(state.List, e.OriginalSource as DependencyObject, out ListBoxItem? item, out string name))
                    return;
                PreviewAnimation(state, name);
                item!.IsSelected = !item.IsSelected;
                e.Handled = true;
            };
            return state;
        }

        private void RefreshAnimationPicker(AnimationPickerState state, string? modelCode) {
            if (string.Equals(state.ModelCode, modelCode, StringComparison.OrdinalIgnoreCase) && (state.LookupPending || state.Loaded))
                return;

            state.ModelCode = modelCode;
            state.SelectedIndices.Clear();
            state.PreviewIndex = null;
            state.Names = Array.Empty<string>();
            state.Loaded = false;
            long request = Interlocked.Increment(ref state.LookupRequest);
            if (modelCode == null) {
                state.LookupPending = false;
                state.Title.Text = "Animations (0)";
                state.Title.ToolTip = "Select a model to see its animations.";
                RefreshAnimationList(state);
                UpdateAnimationPickerControlState(state);
                return;
            }

            state.LookupPending = true;
            state.Title.Text = "Animations (…)";
            state.Title.ToolTip = $"Finding animations for {modelCode}.";
            RefreshAnimationList(state);
            UpdateAnimationPickerControlState(state);
            _ = LoadAnimationsAsync(state, modelCode, request);
        }

        private async Task LoadAnimationsAsync(AnimationPickerState state, string modelCode, long request) {
            try {
                DataSource source = _source;
                IReadOnlyList<string> names = await Task.Run(() => {
                    var exporter = new BattleModel(source, new BattleModelOptions());
                    return exporter.BuildSceneAuto(modelCode, includeMeshes: false, includeAnimations: true)
                        .LogicalAnimations
                        .Select(animation => animation.Name ?? string.Empty)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList();
                });
                if (request != Interlocked.Read(ref state.LookupRequest))
                    return;

                state.Names = names;
                state.PreviewIndex = state.Names
                    .Select(name => TryGetAnimationIndex(name, out int index) ? (int?)index : null)
                    .FirstOrDefault(index => index.HasValue);
                state.Title.Text = $"Animations ({state.Names.Count:N0})";
                state.Title.ToolTip = state.Names.Count == 0
                    ? $"No animations were found for {modelCode}."
                    : $"Animations for {modelCode}. Left-click to preview; right-click to include or exclude from export.";
            } catch (Exception ex) {
                if (request != Interlocked.Read(ref state.LookupRequest))
                    return;
                state.Names = Array.Empty<string>();
                state.PreviewIndex = null;
                state.Title.Text = "Animations (0)";
                state.Title.ToolTip = $"Could not load animations: {ex.Message}";
            } finally {
                if (request == Interlocked.Read(ref state.LookupRequest)) {
                    state.LookupPending = false;
                    state.Loaded = true;
                    RefreshAnimationList(state);
                    UpdateAnimationPickerControlState(state);
                    if (ReferenceEquals(state, _activeAnimations))
                        SchedulePreview();
                }
            }
        }

        private void RefreshAnimationList(AnimationPickerState state) {
            state.Refreshing = true;
            try {
                string search = state.Search.Text;
                state.List.ItemsSource = state.Names
                    .Where(name => string.IsNullOrWhiteSpace(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (string name in state.List.Items.Cast<string>()) {
                    if (TryGetAnimationIndex(name, out int index) && state.SelectedIndices.Contains(index))
                        state.List.SelectedItems.Add(name);
                }
            } finally {
                state.Refreshing = false;
            }
        }

        private static void UpdateAnimationPickerControlState(AnimationPickerState state) {
            bool enabled = !state.LookupPending && state.Names.Count > 0;
            state.Search.IsEnabled = enabled;
            state.List.IsEnabled = enabled;
        }

        private void PreviewAnimation(AnimationPickerState state, string name) {
            if (TryGetAnimationIndex(name, out int index)) {
                state.PreviewIndex = index;
                if (ReferenceEquals(state, _activeAnimations))
                    SchedulePreview();
            }
        }

        private static bool TryGetAnimationIndex(string name, out int index) {
            index = -1;
            return name.StartsWith("Anim", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name.AsSpan(4), out index);
        }

        private static bool TryGetAnimationItem(ListBox list, DependencyObject? source, out ListBoxItem? item, out string animation) {
            item = source == null ? null : ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
            if (item?.Content is string name) {
                animation = name;
                return true;
            }
            animation = string.Empty;
            return false;
        }

        private static IReadOnlyCollection<int>? GetSelectedAnimationIndices(AnimationPickerState? state) =>
            state != null && state.SelectedIndices.Count > 0
                ? state.SelectedIndices.OrderBy(index => index).ToArray()
                : null;

        private void SchedulePreview() {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview() {
            if (_previewBattlefield) {
                string locationCode = _txtBattlefield.Text.Trim();
                if (string.IsNullOrWhiteSpace(locationCode)) {
                    _modelPreview.ShowMessage("Select a battlefield to preview");
                    return;
                }
                if (!float.TryParse(_txtScale.Text, out float battlefieldScale)) {
                    _modelPreview.ShowMessage("Enter a valid scale to preview this battlefield");
                    return;
                }
                DataSource battlefieldSource = _source;
                BattleModelOptions battlefieldOptions = GetOptions(battlefieldScale);
                _modelPreview.ShowModel(() => new BattleModel(battlefieldSource, battlefieldOptions)
                    .BuildSceneFromBattleLocation(locationCode), locationCode, cameraZoom: 1.4f);
                return;
            }

            ListBox? modelList = _magicModels ? _activeMagicModelList : _lstModels;
            if (modelList?.SelectedItem is not string modelCode) {
                _modelPreview.ShowMessage("Select a model to preview");
                return;
            }
            if (_activeAnimations?.LookupPending == true) {
                _modelPreview.ShowMessage("Finding animations…");
                return;
            }
            if (!float.TryParse(_txtScale.Text, out float scale)) {
                _modelPreview.ShowMessage("Enter a valid scale to preview this model");
                return;
            }

            DataSource source = _source;
            BattleModelOptions options = GetOptions(scale);
            int? previewAnimationIndex = _activeAnimations?.PreviewIndex;
            _modelPreview.ShowModel(() => {
                var exporter = new BattleModel(source, options);
                if (previewAnimationIndex.HasValue)
                    return exporter.BuildSceneAuto(modelCode, includeAnimations: true, animationIndices: new[] { previewAnimationIndex.Value });
                return exporter.BuildSceneAuto(modelCode, includeAnimations: true, maxAnimationCount: 1);
            }, modelCode);
        }

        private UIElement CreateModelPicker() {
            var picker = new StackPanel();
            picker.Children.Add(DesktopUi.CreateSearchField("Search models…", _txtModelSearch));
            picker.Children.Add(_lstModels);
            return picker;
        }

        private void BtnExport_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                ListBox modelList = _magicModels ? _activeMagicModelList ?? _lstModels : _lstModels;
                if (modelList.SelectedItem is not string modelCode)
                    throw new Exception("Select a model from the available models list.");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var options = GetOptions(scale);
                var exporter = new BattleModel(_source, options);
                bool embedAnimations = _chkEmbedAnimations.IsChecked == true;
                var animationIndices = embedAnimations ? GetSelectedAnimationIndices(_activeAnimations) : null;
                var model = exporter.BuildSceneAuto(modelCode, includeAnimations: embedAnimations, animationIndices: animationIndices);
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                string animationSummary = _chkEmbedAnimations.IsChecked == true
                    ? $"Animation clips included: {model.LogicalAnimations.Count}"
                    : "Animations were not embedded.";
                DesktopUi.ShowInfo(this, "Success", $"Export succeeded.\n{animationSummary}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private async void BtnExportAll_Clicked(IReadOnlyList<string>? categoryModelCodes = null, string? category = null) {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                var modelCodes = categoryModelCodes?.ToList() ?? (_magicModels
                    ? _activeMagicModelCodes.ToList()
                    : ModelBatchDiscovery.FindBattleModels(_source));
                if (modelCodes.Count == 0) {
                    string modelType = _magicModels ? (category ?? _activeMagicModelGroup).ToLowerInvariant() : "battle";
                    throw new Exception($"No {modelType} models were found in the selected source.");
                }

                var preferences = PreferencesStore.Load();
                string group = category ?? _activeMagicModelGroup;
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    _magicModels ? $"Choose a folder for all {group.ToLowerInvariant()} model exports" : "Choose a folder for all battle model exports",
                    preferences.LastOutputDirectory,
                    this
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                DataSource source = _source;
                BattleModelOptions options = GetOptions(scale);
                bool embedAnimations = _chkEmbedAnimations.IsChecked == true;
                IReadOnlyCollection<int>? animationIndices = embedAnimations ? GetSelectedAnimationIndices(_activeAnimations) : null;
                string result = await BatchExportRunner.RunAsync(
                    this,
                    _magicModels ? $"Exporting all {group.ToLowerInvariant()} models" : "Exporting all battle models",
                    modelCodes.Count,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        int animationCount = 0;
                        int processed = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < modelCodes.Count; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            string code = modelCodes[index];
                            progress.Report(new BatchProgressUpdate(index, modelCodes.Count, $"Exporting {code}…"));
                            try {
                                var exporter = new BattleModel(source, options);
                                var model = exporter.BuildSceneAuto(code, includeAnimations: embedAnimations, animationIndices: animationIndices);
                                model.SaveGLB(Path.Combine(outputDirectory, code + ".glb"));
                                animationCount += model.LogicalAnimations.Count;
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{code}: {ex.Message}");
                            }
                            processed++;
                            progress.Report(new BatchProgressUpdate(index + 1, modelCodes.Count, $"Processed {code}"));
                        }

                        string animationSummary = embedAnimations
                    ? $"Animation clips included: {animationCount}"
                    : "Animations were not embedded.";
                        string status = processed < modelCodes.Count ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {modelCodes.Count} models to:\n{outputDirectory}\n\n{animationSummary}";
                        if (failures.Count > 0) {
                            summary += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                            if (failures.Count > 5)
                                summary += "\n...";
                        }
                        return summary;
                    }
                );
                DesktopUi.ShowInfo(this, _magicModels ? $"Export All {group} Models" : "Export All", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private void BtnExportBattlefield_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var exporter = new BattleModel(_source, GetOptions(scale));
                if (string.IsNullOrWhiteSpace(_txtBattlefield.Text))
                    throw new Exception("Select a battlefield or enter its location code.");
                string locationCode = _txtBattlefield.Text;
                var model = exporter.BuildSceneFromBattleLocation(locationCode);
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Success", "Battlefield export succeeded");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private async void BtnExportAllBattlefields_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                var locationCodes = ModelBatchDiscovery.FindBattleLocations(_source);
                if (locationCodes.Count == 0)
                    throw new Exception("No battlefield skeletons were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    "Choose a folder for all battlefield exports",
                    preferences.LastOutputDirectory,
                    this
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                DataSource source = _source;
                BattleModelOptions options = GetOptions(scale);
                string result = await BatchExportRunner.RunAsync(
                    this,
                    "Exporting all battlefields",
                    locationCodes.Count,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        int processed = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < locationCodes.Count; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            string code = locationCodes[index];
                            progress.Report(new BatchProgressUpdate(index, locationCodes.Count, $"Exporting {code}…"));
                            try {
                                var exporter = new BattleModel(source, options);
                                var model = exporter.BuildSceneFromBattleLocation(code);
                                model.SaveGLB(Path.Combine(outputDirectory, code + ".glb"));
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{code}: {ex.Message}");
                            }
                            processed++;
                            progress.Report(new BatchProgressUpdate(index + 1, locationCodes.Count, $"Processed {code}"));
                        }

                        string status = processed < locationCodes.Count ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported {exported} of {locationCodes.Count} battlefields to:\n{outputDirectory}";
                        if (failures.Count > 0) {
                            summary += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                            if (failures.Count > 5)
                                summary += "\n...";
                        }
                        return summary;
                    }
                );
                DesktopUi.ShowInfo(this, "Export Battlefields", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private async void BtnExportAllAnimations_Clicked(IReadOnlyList<string>? categoryModelCodes = null, string? category = null) {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                var modelCodes = categoryModelCodes?.ToList() ?? (_magicModels
                    ? _activeMagicModelCodes.ToList()
                    : ModelBatchDiscovery.FindBattleModels(_source));
                if (modelCodes.Count == 0)
                    throw new Exception("No models were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string group = category ?? _activeMagicModelGroup;
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    _magicModels ? $"Choose a folder for all {group.ToLowerInvariant()} animations" : "Choose a folder for all battle animations",
                    preferences.LastOutputDirectory,
                    this
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                DataSource source = _source;
                BattleModelOptions options = GetOptions(scale);
                string result = await BatchExportRunner.RunAsync(
                    this,
                    $"Exporting {group.ToLowerInvariant()} animations",
                    modelCodes.Count,
                    (progress, cancellationToken) => {
                        int exported = 0;
                        int animationCount = 0;
                        int processed = 0;
                        var failures = new List<string>();
                        for (int index = 0; index < modelCodes.Count; index++) {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            string code = modelCodes[index];
                            progress.Report(new BatchProgressUpdate(index, modelCodes.Count, $"Exporting animations for {code}…"));
                            try {
                                var exporter = new BattleModel(source, options);
                                var model = exporter.BuildSceneAuto(code, includeMeshes: false, includeAnimations: true);
                                string outputFile = Path.Combine(outputDirectory, code, "Animations.glb");
                                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                                model.SaveGLB(outputFile);
                                animationCount += model.LogicalAnimations.Count;
                                exported++;
                            } catch (Exception ex) {
                                failures.Add($"{code}: {ex.Message}");
                            }
                            processed++;
                            progress.Report(new BatchProgressUpdate(index + 1, modelCodes.Count, $"Processed {code}"));
                        }

                        string status = processed < modelCodes.Count ? "Cancelled. " : string.Empty;
                        string summary = $"{status}Exported animations for {exported} of {modelCodes.Count} models to:\n{outputDirectory}\n" +
                            $"Animation clips: {animationCount}";
                        if (failures.Count > 0) {
                            summary += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                            if (failures.Count > 5)
                                summary += "\n...";
                        }
                        return summary;
                    }
                );
                DesktopUi.ShowInfo(this, _magicModels ? $"Export All {group} Animations" : "Export All Animations", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private string? ChooseGlbPath() {
            var preferences = PreferencesStore.Load();
            return NativeFilePicker.SaveGlb(
                preferences.LastOutputDirectory,
                this
            );
        }

        private BattleModelOptions GetOptions(float scale) => new() {
            ConvertSRGBToLinear = _chkSRGB.IsChecked == true,
            SwapWinding = _chkSwapWinding.IsChecked == true,
            BakeVertexColours = _chkBakeColours.IsChecked == true,
            Scale = scale,
        };

    }

    internal static class ModelBatchDiscovery {
        // Vanilla magic.lgp contains summon rigs alongside spell/ability effect rigs.
        // Keep the known summon file stems here; unrecognized magic skeletons remain
        // available under the Magic tab so mods can add their own effects.
        private static readonly HashSet<string> SummonMagicModelNames = new(
            new[] {
                "alexdat", "bahamdat", "bahamrda", "bahamzda", "cyvadat", "debcyoda",
                "hadesdat", "ifreetda", "kujatada", "lamdat", "mogdat", "mogurida",
                "odin_gda", "odin_zda", "pheonixd", "rivadat", "titandat", "tupondat",
            }.Concat(Enumerable.Range(1, 13).Select(index => $"knight{index:00}")),
            StringComparer.OrdinalIgnoreCase
        );

        public static List<string> FindBattleModels(DataSource source) {
            return source.AllFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => name.Length == 4 && name.EndsWith("aa", StringComparison.OrdinalIgnoreCase))
                .Select(name => name[..2])
                .Where(code => source.Exists(code + "da"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> FindMagicModels(DataSource source) {
            return source.AllFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => Path.GetExtension(name).Equals(".d", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => source.Exists(name + ".a00"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> FindNonSummonMagicModels(IEnumerable<string> modelCodes) =>
            modelCodes
                .Where(name => !SummonMagicModelNames.Contains(name))
                .ToList();

        public static List<string> FindSummonModels(IEnumerable<string> modelCodes) =>
            modelCodes
                .Where(SummonMagicModelNames.Contains)
                .ToList();

        public static List<string> FindBattleLocations(DataSource source) {
            return source.AllFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => name.Length == 4 && name.EndsWith("aa", StringComparison.OrdinalIgnoreCase))
                .Select(name => name[..2])
                .Where(code => BattleModel.IsBattleLocation(source, code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
