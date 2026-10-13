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
    public class Tex : ExportKind {
        public override string Help =>
@"Usage: CrossSlash Tex [SourceLGP] [DestinationFolder] [file:palette] [file:palette] ...
    For each file, specify the palette number to export, or * for all palettes.
    Exported textures are written to PNG files.
    Example:
    CrossSlash Tex C:\FF7\data\battle\magic.lgp C:\temp\texs bio.tex:0 bio.tex:1
    CrossSlash Tex C:\FF7\data\battle\magic.lgp C:\temp\texs fire00.tex:*
";

        public override object Config => null!;
        public override bool HasGui => true;
        public override string Name => "Tex export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            foreach (string tex in parameters) {
                string[] parts = tex.Split(':');
                using var stream = source.Open(parts[0]);
                var texFile = new Ficedula.FF7.TexFile(stream);
                IEnumerable<int> palettes = parts[1] == "*"
                    ? Enumerable.Range(0, texFile.Palettes.Count)
                    : Enumerable.Range(int.Parse(parts[1]), 1);
                foreach (int palette in palettes) {
                    string output = Path.Combine(dest, Path.ChangeExtension(parts[0], $"#{palette}.png"));
                    Console.WriteLine("Writing " + output);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    using var bitmap = TexFileUtil.ToBitmap(texFile, palette);
                    File.WriteAllBytes(output, bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
                }
            }
        }

        public override Window ExecuteGui(DataSource source) => new TexWindow(source, this);
    }

    public class TexWindow : Window {
        private readonly DataSource _source;
        private readonly Tex _exporter;
        private readonly ListBox _files;
        private readonly string[] _entries;
        private readonly TextBox _destination;

        public TexWindow(DataSource source, Tex exporter) {
            _source = source;
            _exporter = exporter;
            _entries = source.AllFiles
                .Where(file => Path.GetExtension(file).Equals(".tex", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _files = new ListBox {
                ItemsSource = _entries,
                SelectionMode = SelectionMode.Extended,
                MinHeight = 300,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
            };
            _destination = DesktopUi.CreateTextBox(readOnly: true);

            var browse = DesktopUi.CreateButton("Browse…");
            browse.Click += (_, _) => BrowseDestination();
            var destinationRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            destinationRow.Children.Add(DesktopUi.CreateLabel("Save textures to"));
            Grid.SetColumn(_destination, 1);
            destinationRow.Children.Add(_destination);
            Grid.SetColumn(browse, 2);
            browse.Margin = new Thickness(10, 0, 0, 0);
            destinationRow.Children.Add(browse);

            var export = DesktopUi.CreateButton("Export selected textures", primary: true);
            export.Click += (_, _) => ExportSelected();

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Textures"));
            content.Children.Add(DesktopUi.CreateHint($"{_entries.Length:N0} texture files. Select one or more entries to export all palettes."));
            content.Children.Add(_files);
            content.Children.Add(new Border { Height = 18, Opacity = 0 });
            content.Children.Add(destinationRow);
            content.Children.Add(DesktopUi.CreateActionRow(export));

            DesktopUi.ConfigureWindow(this, "Texture exporter", "Save selected LGP textures as PNG files", content, back: Close);
        }

        private void BrowseDestination() {
            var preferences = PreferencesStore.Load();
            string? path = NativeFilePicker.OpenFolder(
                "Choose the folder to save textures to",
                preferences.LastExtractDirectory ?? preferences.DefaultSourcePath,
                this
            );
            if (path == null)
                return;
            _destination.Text = path;
            PreferencesStore.RememberExtractDirectory(path);
        }

        private void ExportSelected() {
            try {
                if (string.IsNullOrWhiteSpace(_destination.Text))
                    throw new InvalidOperationException("Choose a destination folder first.");
                if (_files.SelectedItems.Count == 0)
                    throw new InvalidOperationException("Select one or more texture files to export.");

                var files = _files.SelectedItems.Cast<string>().Select(file => file + ":*").ToArray();
                _exporter.Execute(_source, _destination.Text, files);
                DesktopUi.ShowInfo(this, "Export complete", $"Exported {_files.SelectedItems.Count:N0} texture files to:\n{_destination.Text}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Export failed", ex.Message);
            }
        }
    }
}
