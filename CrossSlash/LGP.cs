// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.IO.Enumeration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrossSlash {
    public class LGPExportOptions {
        public bool List { get; set; }
    }

    public class LGP : ExportKind {
        public override string Help =>
@"Usage: CrossSlash LGP [SourceLGP] [DestFolder] [/List] or [file] [file] [file...]
    Extracts files from an LGP, or lists LGP contents
    Use /List to list the contents of the LGP;
    or list the files to extract from the LGP.
    Files can be an exact filename or use *? wildcards, e.g.

    CrossSlash LGP flevel.lgp C:\temp mrkt* nmk*
";

        private readonly LGPExportOptions _options = new();
        public override object Config => _options;
        public override bool HasGui => true;
        public override string Name => "LGP List/Extract";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            if (_options.List) {
                Console.WriteLine("Size      File");
                Console.WriteLine("==============");
                foreach (string filename in source.AllFiles) {
                    using var stream = source.Open(filename);
                    Console.WriteLine($"{stream.Length,9} {filename}");
                }
            } else {
                foreach (string file in source.AllFiles) {
                    if (parameters.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, file))) {
                        Console.WriteLine($"Extracting {file}...");
                        using var stream = source.Open(file);
                        string output = Path.Combine(dest, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        using var outputStream = File.Create(output);
                        stream.CopyTo(outputStream);
                    }
                }
            }
        }

        public override Window ExecuteGui(DataSource source) => new LGPWindow(source);
    }

    public class LGPWindow : Window {
        private readonly DataSource _source;
        private readonly ListBox _files;
        private readonly string[] _entries;
        private readonly TextBox _destination;

        public LGPWindow(DataSource source) {
            _source = source;
            _entries = source.AllFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
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
            destinationRow.Children.Add(DesktopUi.CreateLabel("Extract to"));
            Grid.SetColumn(_destination, 1);
            destinationRow.Children.Add(_destination);
            Grid.SetColumn(browse, 2);
            browse.Margin = new Thickness(10, 0, 0, 0);
            destinationRow.Children.Add(browse);

            var extract = DesktopUi.CreateButton("Extract selected files", primary: true);
            extract.Click += (_, _) => ExtractSelected();

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("LGP contents"));
            content.Children.Add(DesktopUi.CreateHint($"{_entries.Length:N0} files. Select one or more entries; use Ctrl+A to select all."));
            content.Children.Add(_files);
            content.Children.Add(new Border { Height = 18, Opacity = 0 });
            content.Children.Add(destinationRow);
            content.Children.Add(DesktopUi.CreateActionRow(extract));

            DesktopUi.ConfigureWindow(this, "LGP extractor", "Browse archive contents and extract selected files", content, back: Close);
        }

        private void BrowseDestination() {
            var preferences = PreferencesStore.Load();
            string? path = NativeFilePicker.OpenFolder(
                "Choose the folder to extract to",
                preferences.LastExtractDirectory,
                this
            );
            if (path == null)
                return;
            _destination.Text = path;
            PreferencesStore.RememberExtractDirectory(path);
        }

        private void ExtractSelected() {
            try {
                if (string.IsNullOrWhiteSpace(_destination.Text))
                    throw new InvalidOperationException("Choose a destination folder first.");
                if (_files.SelectedItems.Count == 0)
                    throw new InvalidOperationException("Select one or more files to extract.");

                int extracted = 0;
                foreach (string entry in _files.SelectedItems.Cast<string>()) {
                    using var stream = _source.Open(entry);
                    string output = Path.Combine(_destination.Text, entry);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    using var outputStream = File.Create(output);
                    stream.CopyTo(outputStream);
                    extracted++;
                }
                DesktopUi.ShowInfo(this, "Extraction complete", $"Extracted {extracted:N0} files to:\n{_destination.Text}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Extraction failed", ex.Message);
            }
        }
    }
}
