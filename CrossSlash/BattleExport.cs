// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System;
using System.Linq;
using System.Text;
using Terminal.Gui;

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
        private TextField _txtModel, _txtScale;

        public BattleExportWindow(DataSource source, bool magicModels = false) {
            _source = source;
            _magicModels = magicModels;
            Title = magicModels ? "CrossSlash Magic Model Exporter" : "CrossSlash Battle Model Exporter";

            Label lblModel = new Label {
                Width = Dim.Percent(25),
                Text = "Model Code",
            };
            _txtModel = new TextField {
                Y = lblModel.Y,
                X = Pos.Right(lblModel),
                Width = Dim.Fill(1),
            };

            Label lblScale = new Label {
                Y = Pos.Bottom(lblModel) + 1,
                Width = Dim.Percent(25),
                Text = "Scale",
            };
            _txtScale = new TextField {
                Y = lblScale.Y,
                X = Pos.Right(lblScale),
                Width = Dim.Fill(1),
                Text = magicModels ? "0.1" : "1",
            };

            _chkSRGB = new CheckBox {
                Checked = true,
                Text = "Convert colours from SRGB->Linear",
                Y = Pos.Bottom(_txtScale) + 1,
            };
            _chkSwapWinding = new CheckBox {
                Checked = false,
                Text = "Swap triangle winding",
                Y = Pos.Bottom(_chkSRGB),
            };
            _chkBakeColours = new CheckBox {
                Checked = false,
                Text = "Bake Vertex Colours to texture",
                Y = Pos.Bottom(_chkSwapWinding),
            };
            var btnGLB = new Button {
                Text = "Export",
                Width = Dim.Percent(50),
                Y = Pos.Bottom(_chkBakeColours) + 1,
            };
            btnGLB.Clicked += BtnExport_Clicked;

            var btnExportAll = new Button {
                X = Pos.Right(btnGLB) + 1,
                Y = btnGLB.Y,
                Width = Dim.Fill(1),
                Text = magicModels ? "Export All Summons" : "Export All Battle Models",
            };
            btnExportAll.Clicked += BtnExportAll_Clicked;

            var btnExportAllAnimations = new Button {
                Y = Pos.Bottom(btnExportAll) + 1,
                Width = Dim.Fill(1),
                Text = "Export All Animations",
            };
            btnExportAllAnimations.Clicked += BtnExportAllAnimations_Clicked;

            Button? btnExportBattlefield = null;
            Button? btnExportAllBattlefields = null;
            if (!_magicModels) {
                btnExportBattlefield = new Button {
                    Y = Pos.Bottom(btnExportAllAnimations) + 1,
                    Width = Dim.Percent(50),
                    Text = "Export Battlefield",
                };
                btnExportBattlefield.Clicked += BtnExportBattlefield_Clicked;

                btnExportAllBattlefields = new Button {
                    X = Pos.Right(btnExportBattlefield) + 1,
                    Y = btnExportBattlefield.Y,
                    Width = Dim.Fill(1),
                    Text = "Export All Battlefields",
                };
                btnExportAllBattlefields.Clicked += BtnExportAllBattlefields_Clicked;
            }

            View lastAction = btnExportAllBattlefields ?? (View)btnExportAllAnimations;
            var btnBack = ExporterWindowNavigation.CreateBackButton(Pos.Bottom(lastAction) + 1);

            Add(lblModel, _txtModel, lblScale, _txtScale, 
                _chkSRGB, _chkSwapWinding, _chkBakeColours,
                btnGLB, btnExportAll, btnExportAllAnimations);
            if (btnExportBattlefield != null && btnExportAllBattlefields != null)
                Add(btnExportBattlefield, btnExportAllBattlefields);
            Add(btnBack);
        }

        private void BtnExport_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (string.IsNullOrWhiteSpace(_txtModel.Text.ToString()))
                    throw new Exception("No model file selected");
                if (!float.TryParse(_txtScale.Text.ToString(), out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var options = GetOptions(scale);
                string modelCode = _txtModel.Text?.ToString() ?? string.Empty;
                var exporter = new BattleModel(_source, options);
                exporter.BuildSceneAuto(modelCode).SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                MessageBox.Query("Success", "Export succeeded", "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private void BtnExportAll_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text.ToString(), out float scale))
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
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
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
                MessageBox.Query("Export All", result, "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private void BtnExportBattlefield_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text.ToString(), out float scale))
                    throw new Exception("No scale specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var exporter = new BattleModel(_source, GetOptions(scale));
                string locationCode = _txtModel.Text?.ToString() ?? string.Empty;
                var model = exporter.BuildSceneFromBattleLocation(locationCode);
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);
                MessageBox.Query("Success", "Battlefield export succeeded", "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private void BtnExportAllBattlefields_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text.ToString(), out float scale))
                    throw new Exception("No scale specified");

                var locationCodes = ModelBatchDiscovery.FindBattleLocations(_source);
                if (locationCodes.Count == 0)
                    throw new Exception("No battlefield skeletons were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    "Choose a folder for all battlefield exports",
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
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
                MessageBox.Query("Export Battlefields", result, "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private void BtnExportAllAnimations_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (!float.TryParse(_txtScale.Text.ToString(), out float scale))
                    throw new Exception("No scale specified");

                var modelCodes = _magicModels
                    ? ModelBatchDiscovery.FindMagicModels(_source)
                    : ModelBatchDiscovery.FindBattleModels(_source);
                if (modelCodes.Count == 0)
                    throw new Exception("No models were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    _magicModels ? "Choose a folder for all summon animations" : "Choose a folder for all battle animations",
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
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
                MessageBox.Query("Export All Animations", result, "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private string? ChooseGlbPath() {
            var preferences = PreferencesStore.Load();
            return NativeFilePicker.SaveGlb(
                preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
            );
        }

        private BattleModelOptions GetOptions(float scale) => new() {
            ConvertSRGBToLinear = _chkSRGB.Checked,
            SwapWinding = _chkSwapWinding.Checked,
            BakeVertexColours = _chkBakeColours.Checked,
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
