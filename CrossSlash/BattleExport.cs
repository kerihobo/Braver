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
        private readonly TextBox _txtModelSearch, _txtScale, _txtBattlefield;
        private readonly ListBox _lstModels, _lstBattlefields;
        private readonly List<string> _modelCodes;
        private readonly List<string> _battlefieldCodes;
        private readonly ICollectionView _modelView;
        private ListBox? _activeMagicModelList;
        private IReadOnlyList<string> _activeMagicModelCodes = Array.Empty<string>();
        private string _activeMagicModelGroup = "Magic";

        public BattleExportWindow(DataSource source, bool magicModels = false) {
            _source = source;
            _magicModels = magicModels;
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
            _exportOptions = new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { _chkSRGB, _chkSwapWinding, _chkBakeColours, _chkEmbedAnimations },
            };

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateFormRow("Scale", _txtScale));

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
            panel.Children.Add(DesktopUi.CreateSectionTitle("Battle models"));
            panel.Children.Add(DesktopUi.CreateHint($"{_modelCodes.Count:N0} models detected. Search the list and select a model to export."));
            panel.Children.Add(DesktopUi.CreateFormRow("Available models", CreateModelPicker()));

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
            panel.Children.Add(DesktopUi.CreateSectionTitle("Battlefields"));
            panel.Children.Add(DesktopUi.CreateHint($"{_battlefieldCodes.Count:N0} battlefield skeletons detected. Select one or enter a location code manually."));
            panel.Children.Add(DesktopUi.CreateFormRow("Available battlefields", _lstBattlefields));
            panel.Children.Add(DesktopUi.CreateFormRow("Location code", _txtBattlefield));

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

            var panel = new StackPanel();
            panel.Children.Add(DesktopUi.CreateSectionTitle($"Models ({modelCodes.Count:N0})"));
            panel.Children.Add(DesktopUi.CreateSearchField($"Search {group.ToLowerInvariant()} models…", search));
            panel.Children.Add(modelList);

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
                Tag = new MagicModelTabState(group, modelCodes, modelList, panel),
            };
        }

        private void ActivateMagicModelTab(TabControl tabs) {
            if (tabs.SelectedItem is not TabItem { Tag: MagicModelTabState state })
                return;

            _activeMagicModelGroup = state.Group;
            _activeMagicModelCodes = state.ModelCodes;
            _activeMagicModelList = state.ModelList;
            MoveExportOptionsTo(state.Panel);
        }

        private sealed record MagicModelTabState(string Group, List<string> ModelCodes, ListBox ModelList, StackPanel Panel);

        private void MoveExportOptionsToSelectedTab(TabControl tabs) {
            if (tabs.SelectedItem is TabItem { Content: StackPanel panel } tab) {
                _chkEmbedAnimations.Visibility = Equals(tab.Header, "Battlefields")
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                MoveExportOptionsTo(panel);
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
                var model = exporter.BuildSceneAuto(modelCode, includeAnimations: _chkEmbedAnimations.IsChecked == true);
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

        private void BtnExportAll_Clicked(IReadOnlyList<string>? categoryModelCodes = null, string? category = null) {
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

                int exported = 0;
                int animationCount = 0;
                var failures = new List<string>();
                foreach (string code in modelCodes) {
                    try {
                        var exporter = new BattleModel(_source, GetOptions(scale));
                        var model = exporter.BuildSceneAuto(code, includeAnimations: _chkEmbedAnimations.IsChecked == true);
                        model.SaveGLB(Path.Combine(outputDirectory, code + ".glb"));
                        animationCount += model.LogicalAnimations.Count;
                        exported++;
                    } catch (Exception ex) {
                        failures.Add($"{code}: {ex.Message}");
                    }
                }

                string animationSummary = _chkEmbedAnimations.IsChecked == true
                    ? $"Animation clips included: {animationCount}"
                    : "Animations were not embedded.";
                string result = $"Exported {exported} of {modelCodes.Count} models to:\n{outputDirectory}\n\n{animationSummary}";
                if (failures.Count > 0) {
                    result += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                    if (failures.Count > 5)
                        result += "\n...";
                }
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

        private void BtnExportAllBattlefields_Clicked() {
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

                int exported = 0;
                var failures = new List<string>();
                foreach (string code in locationCodes) {
                    try {
                        var exporter = new BattleModel(_source, GetOptions(scale));
                        var model = exporter.BuildSceneFromBattleLocation(code);
                        model.SaveGLB(Path.Combine(outputDirectory, code + ".glb"));
                        exported++;
                    } catch (Exception ex) {
                        failures.Add($"{code}: {ex.Message}");
                    }
                }

                string result = $"Exported {exported} of {locationCodes.Count} battlefields to:\n{outputDirectory}";
                if (failures.Count > 0) {
                    result += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                    if (failures.Count > 5)
                        result += "\n...";
                }
                DesktopUi.ShowInfo(this, "Export Battlefields", result);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private void BtnExportAllAnimations_Clicked(IReadOnlyList<string>? categoryModelCodes = null, string? category = null) {
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

                int exported = 0;
                int animationCount = 0;
                var failures = new List<string>();
                foreach (string code in modelCodes) {
                    try {
                        var exporter = new BattleModel(_source, GetOptions(scale));
                        var model = exporter.BuildSceneAuto(code, includeMeshes: false, includeAnimations: true);
                        string outputFile = Path.Combine(outputDirectory, code, "Animations.glb");
                        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                        model.SaveGLB(outputFile);
                        animationCount += model.LogicalAnimations.Count;
                        exported++;
                    } catch (Exception ex) {
                        failures.Add($"{code}: {ex.Message}");
                    }
                }

                string result = $"Exported animations for {exported} of {modelCodes.Count} models to:\n{outputDirectory}\n" +
                    $"Animation clips: {animationCount}";
                if (failures.Count > 0) {
                    result += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                    if (failures.Count > 5)
                        result += "\n...";
                }
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
