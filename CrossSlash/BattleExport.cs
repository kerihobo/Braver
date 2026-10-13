// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
        private TextBox _txtModel, _txtScale;
        private ListBox _lstModels;
        private readonly List<string> _modelCodes;

        public BattleExportWindow(DataSource source, bool magicModels = false) {
            _source = source;
            _magicModels = magicModels;
            _modelCodes = magicModels
                ? ModelBatchDiscovery.FindMagicModels(source)
                : ModelBatchDiscovery.FindBattleModels(source);
            _txtModel = DesktopUi.CreateTextBox();
            _txtScale = DesktopUi.CreateTextBox(magicModels ? "0.1" : "1");
            _lstModels = new ListBox {
                ItemsSource = _modelCodes,
                MinHeight = 150,
                MaxHeight = 230,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            _lstModels.SelectionChanged += (_, _) => {
                if (_lstModels.SelectedItem is string code)
                    _txtModel.Text = code;
            };
            if (_modelCodes.Count > 0) {
                _lstModels.SelectedIndex = 0;
                _txtModel.Text = _modelCodes[0];
            }
            _chkSRGB = new CheckBox { Content = "Convert colours from SRGB to linear", IsChecked = true, Margin = new Thickness(0, 0, 20, 10) };
            _chkSwapWinding = new CheckBox { Content = "Swap triangle winding", Margin = new Thickness(0, 0, 20, 10) };
            _chkBakeColours = new CheckBox { Content = "Bake vertex colours to texture", Margin = new Thickness(0, 0, 20, 10) };

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Model settings"));
            content.Children.Add(DesktopUi.CreateHint($"{_modelCodes.Count:N0} models detected. Select one below or enter a model code manually."));
            content.Children.Add(DesktopUi.CreateFormRow("Available models", _lstModels));
            content.Children.Add(DesktopUi.CreateFormRow("Model code", _txtModel));
            content.Children.Add(DesktopUi.CreateFormRow("Scale", _txtScale));
            content.Children.Add(new WrapPanel {
                Margin = new Thickness(0, 4, 0, 18),
                Children = { _chkSRGB, _chkSwapWinding, _chkBakeColours },
            });

            var btnExport = DesktopUi.CreateButton("Export", primary: true);
            btnExport.Click += (_, _) => BtnExport_Clicked();
            var btnExportAll = DesktopUi.CreateButton(magicModels ? "Export All Summons" : "Export All Battle Models");
            btnExportAll.Click += (_, _) => BtnExportAll_Clicked();
            var btnExportAllAnimations = DesktopUi.CreateButton("Export All Animations");
            btnExportAllAnimations.Click += (_, _) => BtnExportAllAnimations_Clicked();
            content.Children.Add(DesktopUi.CreateActionRow(btnExport, btnExportAll, btnExportAllAnimations));

            if (!_magicModels) {
                content.Children.Add(DesktopUi.CreateSectionTitle("Battlefields"));
                var btnExportBattlefield = DesktopUi.CreateButton("Export Battlefield");
                btnExportBattlefield.Click += (_, _) => BtnExportBattlefield_Clicked();
                var btnExportAllBattlefields = DesktopUi.CreateButton("Export All Battlefields");
                btnExportAllBattlefields.Margin = new Thickness(0);
                btnExportAllBattlefields.Click += (_, _) => BtnExportAllBattlefields_Clicked();
                content.Children.Add(DesktopUi.CreateActionRow(btnExportBattlefield, btnExportAllBattlefields));
            }

            DesktopUi.ConfigureWindow(
                this,
                magicModels ? "Magic model exporter" : "Battle model exporter",
                magicModels ? "Export summon models and their animations" : "Export battle models, animations, and battlefields",
                content,
                back: Close
            );
        }

        private void BtnExport_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (string.IsNullOrWhiteSpace(_txtModel.Text))
                    throw new Exception("No model file selected");
                if (!float.TryParse(_txtScale.Text, out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var options = GetOptions(scale);
                string modelCode = _txtModel.Text;
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
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath,
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
                string locationCode = _txtModel.Text;
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
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath,
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
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath,
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
                preferences.LastOutputDirectory ?? preferences.DefaultSourcePath,
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
