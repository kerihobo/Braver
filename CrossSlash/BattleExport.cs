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
        ModelCode can be a two letter battle model code (e.g. RV), a battlefield code/file (e.g. px or pxaa), or a summon model code (e.g. cyvadat)
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
        private CheckBox _chkSRGB, _chkSwapWinding, _chkBakeColours;
        private readonly WrapPanel _exportOptions;
        private StackPanel? _optionsPanel;
        private readonly TextBox _txtModelSearch, _txtScale, _txtBattlefield;
        private readonly ListBox _lstModels, _lstBattlefields;
        private readonly List<string> _modelCodes;
        private readonly List<string> _battlefieldCodes;
        private readonly ICollectionView _modelView;

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
            _exportOptions = new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { _chkSRGB, _chkSwapWinding, _chkBakeColours },
            };

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateFormRow("Scale", _txtScale));

            if (_magicModels)
            {
                var magicContent = CreateMagicModelContent();
                MoveExportOptionsTo(magicContent);
                content.Children.Add(magicContent);
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

        private StackPanel CreateMagicModelContent() {
            var panel = new StackPanel();
            panel.Children.Add(DesktopUi.CreateSectionTitle("Summon models"));
            panel.Children.Add(DesktopUi.CreateHint($"{_modelCodes.Count:N0} summons detected. Search the list and select a model to export."));
            panel.Children.Add(DesktopUi.CreateFormRow("Available summons", CreateModelPicker()));

            var export = DesktopUi.CreateButton("Export model", primary: true);
            export.Click += (_, _) => BtnExport_Clicked();
            var exportAll = DesktopUi.CreateButton("Export all summons");
            exportAll.Click += (_, _) => BtnExportAll_Clicked();
            var exportAnimations = DesktopUi.CreateButton("Export all animations");
            exportAnimations.Click += (_, _) => BtnExportAllAnimations_Clicked();
            panel.Children.Add(DesktopUi.CreateActionRow(export, exportAll, exportAnimations));
            return panel;
        }

        private void MoveExportOptionsToSelectedTab(TabControl tabs) {
            if (tabs.SelectedItem is TabItem { Content: StackPanel panel })
                MoveExportOptionsTo(panel);
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
                if (_lstModels.SelectedItem is not string modelCode)
                    throw new Exception("Select a model from the available models list.");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var options = GetOptions(scale);
                var exporter = new BattleModel(_source, options);
                exporter.BuildSceneAuto(modelCode).SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                DesktopUi.ShowInfo(this, "Success", "Export succeeded");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Error", ex.Message);
            }
        }

        private void BtnExportAll_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                var modelCodes = _magicModels
                    ? ModelBatchDiscovery.FindMagicModels(_source)
                    : ModelBatchDiscovery.FindBattleModels(_source);
                if (modelCodes.Count == 0) {
                    string modelType = _magicModels ? "magic" : "battle";
                    throw new Exception($"No {modelType} models were found in the selected source.");
                }

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    _magicModels ? "Choose a folder for all magic model exports" : "Choose a folder for all battle model exports",
                    preferences.LastOutputDirectory,
                    this
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                int exported = 0;
                var failures = new List<string>();
                foreach (string code in modelCodes) {
                    try {
                        var exporter = new BattleModel(_source, GetOptions(scale));
                        exporter.BuildSceneAuto(code).SaveGLB(Path.Combine(outputDirectory, code + ".glb"));
                        exported++;
                    } catch (Exception ex) {
                        failures.Add($"{code}: {ex.Message}");
                    }
                }

                string result = $"Exported {exported} of {modelCodes.Count} models to:\n{outputDirectory}";
                if (failures.Count > 0) {
                    result += $"\n\nFailed: {failures.Count}\n" + string.Join("\n", failures.Take(5));
                    if (failures.Count > 5)
                        result += "\n...";
                }
                DesktopUi.ShowInfo(this, "Export All", result);
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

        private void BtnExportAllAnimations_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                var modelCodes = _magicModels
                    ? ModelBatchDiscovery.FindMagicModels(_source)
                    : ModelBatchDiscovery.FindBattleModels(_source);
                if (modelCodes.Count == 0)
                    throw new Exception("No models were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    _magicModels ? "Choose a folder for all summon animations" : "Choose a folder for all battle animations",
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
                DesktopUi.ShowInfo(this, "Export All Animations", result);
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
