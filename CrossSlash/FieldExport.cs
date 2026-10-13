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
            const string cyan = "\x1b[36m";
            const string white = "\x1b[37m";
            const string yellow = "\x1b[33m";
            const string reset = "\x1b[0m";

            Console.Write($"Exporting model ");
            Console.Write($"{cyan}{parameters.First()} {white}-> ");
            Console.WriteLine($"{yellow}{dest}{reset}");

            //foreach (var item in parameters) {
            //    System.Diagnostics.Debug.WriteLine(item);
            //}

            var model = exporter.BuildScene(parameters.First(), parameters.Skip(1));
            //Console.WriteLine($"Saving output to {dest}...");
            model.SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new FieldExportWindow(source);
    }

    public class FieldExportWindow : Window {

        private ListView _lvHRCs;
        private TextView _txtAnims;
        private List<string> _hrcFiles;
        private CheckBox _chkAllCompatible, _chkSRGB, _chkSwapWinding, _chkBakeColours;

        private DataSource _source;

        public FieldExportWindow(DataSource source) {
            _source = source;
            Title = "CrossSlash Exporter (Ctrl-Q to Quit)";

            Label lblHRC = new Label {
                Y = 1,
                Width = Dim.Percent(25),
                Text = "HRC/Model",
            };

            _lvHRCs = new ListView {
                Y = lblHRC.Y,
                X = Pos.Right(lblHRC),
                Width = Dim.Fill(1),
                Height = 8,
            };

            Label lblAnims = new Label {
                Width = Dim.Percent(25),
                Y = Pos.Bottom(_lvHRCs) + 1,
                Text = "Animations:"
            };

            _txtAnims = new TextView {
                X = Pos.Right(lblAnims) + 1,
                Y = lblAnims.Y,
                Width = Dim.Fill(),
                Height = 8,
            };

            _chkAllCompatible = new CheckBox {
                Checked = true,
                Text = "Use all compatible animations",
                Y = Pos.Bottom(_txtAnims) + 1,
            };
            _txtAnims.Enabled = false;
            // Toggled supplies the previous state, so read Checked after the change.
            _chkAllCompatible.Toggled += _ => _txtAnims.Enabled = !_chkAllCompatible.Checked;

            _chkSRGB = new CheckBox {
                Checked = true,
                Text = "Convert colours from SRGB->Linear",
                Y = Pos.Bottom(_chkAllCompatible) + 1,
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
                Width = Dim.Percent(24),
                Y = Pos.Bottom(_chkBakeColours) + 1,
            };
            btnGLB.Clicked += BtnExport_Clicked;

            var btnExportAll = new Button {
                X = Pos.Right(btnGLB) + 1,
                Y = btnGLB.Y,
                Width = Dim.Percent(37),
                Text = "Export All",
            };
            btnExportAll.Clicked += BtnExportAll_Clicked;

            var btnExportAllAnimations = new Button {
                X = Pos.Right(btnExportAll) + 1,
                Y = btnGLB.Y,
                Width = Dim.Fill(1),
                Text = "Export All Animations",
            };
            btnExportAllAnimations.Clicked += BtnExportAllAnimations_Clicked;

            var btnBack = ExporterWindowNavigation.CreateBackButton();

            _hrcFiles = _source.AllFiles
                .Where(s => Path.GetExtension(s).Equals(".hrc", StringComparison.InvariantCultureIgnoreCase))
                .ToList();
            _lvHRCs.SetSource(_hrcFiles);


            Add(btnBack, lblHRC, _lvHRCs, lblAnims, _txtAnims,
                _chkAllCompatible,
                _chkSRGB, _chkSwapWinding, _chkBakeColours,
                btnGLB, btnExportAll, btnExportAllAnimations);
        }

        private void BtnExport_Clicked() {
            try {
                var anims = _txtAnims.Text
                    .ToString()
                    .Split('\r', '\n')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (_source == null)
                    throw new Exception("No data source selected");
                if (_lvHRCs.SelectedItem < 0)
                    throw new Exception("No HRC file selected");
                if (!_chkAllCompatible.Checked && !anims.Any())
                    throw new Exception("No animations specified");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var options = new ModelBaseOptions {
                    ConvertSRGBToLinear = _chkSRGB.Checked,
                    SwapWinding = _chkSwapWinding.Checked,
                    BakeVertexColours = _chkBakeColours.Checked,
                };
                IEnumerable<string> animationFiles;
                bool compatibleAnimationsOnly = _chkAllCompatible.Checked;
                if (_chkAllCompatible.Checked) {
                    animationFiles = _source.AllFiles
                        .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                } else {
                    foreach (var item in anims)
                        System.Diagnostics.Debug.WriteLine(item);
                    animationFiles = anims;
                }

                var exporter = new FieldModel(_source, options);
                var model = exporter.BuildScene(
                    _hrcFiles[_lvHRCs.SelectedItem],
                    animationFiles.ToList(),
                    compatibleAnimationsOnly,
                    out var usedAnimations
                );
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);

                string successMessage = "Export Succeeded";
                if (_chkAllCompatible.Checked)
                    successMessage += $"\nCompatible animations: {usedAnimations.Count}";
                MessageBox.Query("Success", successMessage, "OK");
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Error", ex.Message, "OK");
            }
        }

        private void BtnExportAll_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (_hrcFiles.Count == 0)
                    throw new Exception("No HRC field models were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    "Choose a folder for all field model exports",
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                var animationFiles = _source.AllFiles
                    .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var options = new ModelBaseOptions {
                    ConvertSRGBToLinear = _chkSRGB.Checked,
                    SwapWinding = _chkSwapWinding.Checked,
                    BakeVertexColours = _chkBakeColours.Checked,
                };

                int exported = 0;
                int compatibleAnimationCount = 0;
                int modelsWithoutAnimations = 0;
                var failures = new List<string>();
                foreach (string hrcFile in _hrcFiles) {
                    try {
                        string outputFile = Path.Combine(
                            outputDirectory,
                            Path.GetFileNameWithoutExtension(hrcFile) + ".glb"
                        );
                        var exporter = new FieldModel(_source, options);
                        var model = exporter.BuildScene(
                            hrcFile,
                            animationFiles,
                            compatibleAnimationsOnly: true,
                            out var usedAnimations
                        );
                        model.SaveGLB(outputFile);
                        exported++;
                        compatibleAnimationCount += usedAnimations.Count;
                        if (usedAnimations.Count == 0)
                            modelsWithoutAnimations++;
                    } catch (Exception ex) {
                        failures.Add($"{hrcFile}: {ex.Message}");
                    }
                }

                string result = $"Exported {exported} of {_hrcFiles.Count} field models to:\n{outputDirectory}\n\n" +
                    $"Compatible animation clips: {compatibleAnimationCount}\n" +
                    $"Models without compatible animations: {modelsWithoutAnimations}";
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

        private void BtnExportAllAnimations_Clicked() {
            try {
                if (_source == null)
                    throw new Exception("No data source selected");
                if (_hrcFiles.Count == 0)
                    throw new Exception("No HRC field models were found in the selected source.");

                var preferences = PreferencesStore.Load();
                string? outputDirectory = NativeFilePicker.OpenFolder(
                    "Choose a folder for all field animations",
                    preferences.LastOutputDirectory ?? preferences.DefaultSourcePath
                );
                if (outputDirectory == null)
                    return;
                PreferencesStore.RememberOutputDirectory(outputDirectory);

                var animationFiles = _source.AllFiles
                    .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var options = new ModelBaseOptions {
                    ConvertSRGBToLinear = _chkSRGB.Checked,
                    SwapWinding = _chkSwapWinding.Checked,
                    BakeVertexColours = _chkBakeColours.Checked,
                };

                int exported = 0;
                int compatibleAnimationCount = 0;
                int modelsWithoutAnimations = 0;
                var failures = new List<string>();
                foreach (string hrcFile in _hrcFiles) {
                    try {
                        var exporter = new FieldModel(_source, options);
                        var model = exporter.BuildScene(
                            hrcFile,
                            animationFiles,
                            compatibleAnimationsOnly: true,
                            out var usedAnimations,
                            includeMeshes: false,
                            includeAnimations: true
                        );
                        compatibleAnimationCount += usedAnimations.Count;
                        if (usedAnimations.Count == 0) {
                            modelsWithoutAnimations++;
                            continue;
                        }

                        string outputFile = Path.Combine(
                            outputDirectory,
                            Path.GetFileNameWithoutExtension(hrcFile),
                            "Animations.glb"
                        );
                        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                        model.SaveGLB(outputFile);
                        exported++;
                    } catch (Exception ex) {
                        failures.Add($"{hrcFile}: {ex.Message}");
                    }
                }

                string result = $"Exported animations for {exported} field models to:\n{outputDirectory}\n\n" +
                    $"Compatible animation clips: {compatibleAnimationCount}\n" +
                    $"Models without compatible animations: {modelsWithoutAnimations}";
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

    }
}
