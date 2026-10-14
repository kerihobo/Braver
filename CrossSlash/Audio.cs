// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.Windows;
using System.Windows.Controls;

namespace CrossSlash {
    public class Audio : ExportKind {
        public override string Help =>
@"Usage: CrossSlash Audio [SoundFolder] [DestFolder] [AudioID] [AudioID] [AudioID...]
    SoundFolder should contain audio.fmt and audio.dat.
    AudioID can be a numeric sound effect ID, or * to extract all.
    Example: CrossSlash Audio C:\FF7\data\sound C:\temp\audio 0 1 33 35
    Sounds are written as <soundID>.wav in the destination folder.
";

        public override object Config => null!;
        public override bool HasGui => true;
        public override string Name => "Audio Extractor";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            using var dataStream = source.Open("audio.dat");
            using var formatStream = source.Open("audio.fmt");
            var audio = new Ficedula.FF7.Audio(dataStream, formatStream);
            Directory.CreateDirectory(dest);
            foreach (string parameter in parameters) {
                IEnumerable<int> range = parameter == "*"
                    ? Enumerable.Range(0, audio.EntryCount)
                    : Enumerable.Repeat(int.Parse(parameter), 1);
                foreach (int id in range) {
                    if (audio.IsValid(id)) {
                        Console.WriteLine($"Exporting sound {id}");
                        using var output = File.Create(Path.Combine(dest, $"{id}.wav"));
                        audio.Export(id, output);
                    } else {
                        Console.WriteLine($"Skipping invalid sound effect {id}");
                    }
                }
            }
        }

        public override Window ExecuteGui(DataSource source) => new AudioWindow(source, this);
    }

    public class AudioWindow : Window {
        private readonly DataSource _source;
        private readonly Audio _exporter;
        private readonly TextBox _ids;
        private readonly TextBox _destination;

        public AudioWindow(DataSource source, Audio exporter) {
            _source = source;
            _exporter = exporter;
            _ids = DesktopUi.CreateTextBox();
            _ids.AcceptsReturn = true;
            _ids.TextWrapping = TextWrapping.Wrap;
            _ids.VerticalContentAlignment = VerticalAlignment.Top;
            _ids.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _ids.MinHeight = 190;
            _destination = DesktopUi.CreateTextBox(readOnly: true);

            var browse = DesktopUi.CreateButton("Browse…");
            browse.Click += (_, _) => BrowseDestination();
            var destinationRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            destinationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            destinationRow.Children.Add(DesktopUi.CreateLabel("Save sounds to"));
            Grid.SetColumn(_destination, 1);
            destinationRow.Children.Add(_destination);
            Grid.SetColumn(browse, 2);
            browse.Margin = new Thickness(10, 0, 0, 0);
            destinationRow.Children.Add(browse);

            var export = DesktopUi.CreateButton("Export sounds", primary: true);
            export.Click += (_, _) => ExportSounds();

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Sound effects"));
            content.Children.Add(DesktopUi.CreateHint("Enter one numeric ID per line. Leave the list blank to export every sound effect."));
            content.Children.Add(DesktopUi.CreateFormRow("Sound IDs", _ids, 160));
            content.Children.Add(destinationRow);
            content.Children.Add(DesktopUi.CreateActionRow(export));

            DesktopUi.ConfigureWindow(this, content);
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

        private void ExportSounds() {
            try {
                if (string.IsNullOrWhiteSpace(_destination.Text))
                    throw new InvalidOperationException("Choose a destination folder first.");
                string[] ids = _ids.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _exporter.Execute(_source, _destination.Text, ids.Length > 0 ? ids : new[] { "*" });
                DesktopUi.ShowInfo(this, "Export complete", $"Sounds were exported to:\n{_destination.Text}");
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Export failed", ex.Message);
            }
        }
    }
}
