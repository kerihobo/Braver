// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.Windows;
using System.Windows.Controls;
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
        private readonly ListBox _lvHRCs;
        private readonly TextBox _txtAnims;
        private readonly List<string> _hrcFiles;
        private readonly CheckBox _chkAllCompatible;
        private readonly CheckBox _chkSRGB;
        private readonly CheckBox _chkSwapWinding;
        private readonly CheckBox _chkBakeColours;
        private readonly DataSource _source;

        public FieldExportWindow(DataSource source) {
            _source = source;
            _hrcFiles = source.AllFiles
                .Where(file => Path.GetExtension(file).Equals(".hrc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _lvHRCs = new ListBox {
                ItemsSource = _hrcFiles,
                MinHeight = 220,
                MaxHeight = 340,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            if (_hrcFiles.Count > 0)
                _lvHRCs.SelectedIndex = 0;

            _txtAnims = DesktopUi.CreateTextBox();
            _txtAnims.AcceptsReturn = true;
            _txtAnims.TextWrapping = TextWrapping.Wrap;
            _txtAnims.VerticalContentAlignment = VerticalAlignment.Top;
            _txtAnims.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtAnims.MinHeight = 100;

            _chkAllCompatible = new CheckBox {
                Content = "Use all compatible animations",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 12),
            };
            _txtAnims.IsEnabled = false;
            _chkAllCompatible.Checked += (_, _) => _txtAnims.IsEnabled = false;
            _chkAllCompatible.Unchecked += (_, _) => _txtAnims.IsEnabled = true;

            _chkSRGB = new CheckBox { Content = "Convert colours from sRGB to linear", IsChecked = true, Margin = new Thickness(0, 0, 18, 8) };
            _chkSwapWinding = new CheckBox { Content = "Swap triangle winding", Margin = new Thickness(0, 0, 18, 8) };
            _chkBakeColours = new CheckBox { Content = "Bake vertex colours to texture", Margin = new Thickness(0, 0, 18, 8) };

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Field model"));
            content.Children.Add(DesktopUi.CreateHint($"Choose a model from the source. {_hrcFiles.Count} HRC models found."));
            content.Children.Add(DesktopUi.CreateFormRow("Model", _lvHRCs, 110));
            content.Children.Add(DesktopUi.CreateFormRow("Animation files", _txtAnims, 110));
            content.Children.Add(_chkAllCompatible);
            content.Children.Add(new WrapPanel {
                Margin = new Thickness(0, 4, 0, 16),
                Children = { _chkSRGB, _chkSwapWinding, _chkBakeColours },
            });

            var export = DesktopUi.CreateButton("Export model", primary: true);
            export.Click += (_, _) => ExportOne();
            var exportAll = DesktopUi.CreateButton("Export all models + compatible animations");
            exportAll.Click += (_, _) => ExportAllModels();
            var exportAnimations = DesktopUi.CreateButton("Export all animations separately");
            exportAnimations.Click += (_, _) => ExportAllAnimations();
            content.Children.Add(DesktopUi.CreateActionRow(export, exportAll, exportAnimations));

            DesktopUi.ConfigureWindow(
                this,
                "Field model exporter",
                "Export models with selected animations, or batch export every compatible animation",
                content,
                back: Close,
                width: 1120,
                height: 820
            );
        }

        private void ExportOne() {
            try {
                if (_lvHRCs.SelectedItem is not string hrcFile)
                    throw new InvalidOperationException("Select an HRC model first.");

                var animations = _txtAnims.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (_chkAllCompatible.IsChecked != true && animations.Length == 0)
                    throw new InvalidOperationException("Enter one or more animation files, or enable all compatible animations.");

                string? outputFile = ChooseGlbPath();
                if (outputFile == null)
                    return;

                var exporter = new FieldModel(_source, GetOptions());
                IEnumerable<string> animationFiles = _chkAllCompatible.IsChecked == true
                    ? GetAllAnimationFiles()
                    : animations;
                var model = exporter.BuildScene(
                    hrcFile,
                    animationFiles,
                    compatibleAnimationsOnly: _chkAllCompatible.IsChecked == true,
                    out var usedAnimations
                );
                model.SaveGLB(outputFile);
                PreferencesStore.RememberOutputPath(outputFile);

                string message = "Export succeeded.";
                if (_chkAllCompatible.IsChecked == true)
                    message += $"\nCompatible animations included: {usedAnimations.Count}";
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

                var animations = GetAllAnimationFiles();
                int exported = 0;
                int animationCount = 0;
                int modelsWithoutAnimations = 0;
                var failures = new List<string>();
                foreach (string hrcFile in _hrcFiles) {
                    try {
                        var exporter = new FieldModel(_source, GetOptions());
                        var model = exporter.BuildScene(hrcFile, animations, compatibleAnimationsOnly: true, out var usedAnimations);
                        model.SaveGLB(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(hrcFile) + ".glb"));
                        exported++;
                        animationCount += usedAnimations.Count;
                        if (usedAnimations.Count == 0)
                            modelsWithoutAnimations++;
                    } catch (Exception ex) {
                        failures.Add($"{hrcFile}: {ex.Message}");
                    }
                }

                string result = $"Exported {exported} of {_hrcFiles.Count} field models to:\n{outputDirectory}\n\n" +
                    $"Compatible animation clips: {animationCount}\nModels without compatible animations: {modelsWithoutAnimations}";
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

        private IEnumerable<string> GetAllAnimationFiles() => _source.AllFiles
            .Where(file => Path.GetExtension(file).Equals(".a", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        private ModelBaseOptions GetOptions() => new() {
            ConvertSRGBToLinear = _chkSRGB.IsChecked == true,
            SwapWinding = _chkSwapWinding.IsChecked == true,
            BakeVertexColours = _chkBakeColours.IsChecked == true,
        };

        private string? ChooseOutputFolder(string title) {
            var preferences = PreferencesStore.Load();
            string? path = NativeFilePicker.OpenFolder(
                title,
                preferences.LastOutputDirectory ?? preferences.DefaultSourcePath,
                this
            );
            if (path != null)
                PreferencesStore.RememberOutputDirectory(path);
            return path;
        }

        private string? ChooseGlbPath() {
            var preferences = PreferencesStore.Load();
            return NativeFilePicker.SaveGlb(preferences.LastOutputDirectory ?? preferences.DefaultSourcePath, this);
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
