// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CrossSlash {
    public sealed class SplashWindow : Window {
        private readonly CrossSlashPreferences _preferences;
        private readonly Dictionary<string, SourceRow> _sourceRows = new(StringComparer.InvariantCultureIgnoreCase);

        public SplashWindow() {
            _preferences = PreferencesStore.Load();
            var preferencesButton = DesktopUi.CreateButton("Preferences");
            preferencesButton.Click += (_, _) => OpenPreferences();

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Choose an exporter"));
            content.Children.Add(DesktopUi.CreateHint("Each exporter remembers its own source. Choose an LGP archive or a folder, then open the tool."));

            var rows = new StackPanel();
            foreach (var entry in ExportKind.Exporters.Where(e => e.Value.HasGui))
                rows.Children.Add(CreateExporterRow(entry.Key, entry.Value));
            content.Children.Add(rows);

            DesktopUi.ConfigureWindow(
                this,
                "CrossSlash",
                "Final Fantasy VII asset exporter",
                content,
                headerAction: preferencesButton,
                width: 1120,
                height: 820
            );
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private UIElement CreateExporterRow(string key, ExportKind exporter) {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock {
                Text = exporter.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 77)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 14, 0),
            };
            string? savedPath = _preferences.GetExporterSourcePath(key);
            var sourceType = new ComboBox {
                ItemsSource = new[] { "LGP archive", "Folder" },
                SelectedIndex = !string.IsNullOrWhiteSpace(savedPath) && Directory.Exists(savedPath) ? 1 : 0,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var path = DesktopUi.CreateTextBox(savedPath, readOnly: true);
            path.ToolTip = savedPath ?? "No source selected";
            path.Margin = new Thickness(0, 0, 10, 0);

            var browse = DesktopUi.CreateButton("Browse");
            browse.Margin = new Thickness(0, 0, 10, 0);
            browse.Click += (_, _) => BrowseSource(key, sourceType, path);

            var open = DesktopUi.CreateButton("Open", primary: true);
            open.Margin = new Thickness(0);
            open.Click += (_, _) => RunExporter(key, exporter, path);
            _sourceRows[key] = new SourceRow { Path = path };

            row.Children.Add(name);
            Grid.SetColumn(sourceType, 1);
            row.Children.Add(sourceType);
            Grid.SetColumn(path, 2);
            row.Children.Add(path);
            Grid.SetColumn(browse, 3);
            row.Children.Add(browse);
            Grid.SetColumn(open, 4);
            row.Children.Add(open);

            return new Border {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Child = row,
            };
        }

        private void BrowseSource(string key, ComboBox sourceType, TextBox sourcePath) {
            string? currentPath = sourcePath.Text;
            string? path = sourceType.SelectedIndex switch {
                0 => NativeFilePicker.OpenLgp(currentPath, this),
                1 => NativeFilePicker.OpenFolder("Choose the source folder", currentPath, this),
                _ => null,
            };
            if (path == null)
                return;

            sourcePath.Text = path;
            sourcePath.ToolTip = path;
            RememberSourcePath(key, path);
        }

        private void RunExporter(string key, ExportKind exporter, TextBox sourcePath) {
            string path = sourcePath.Text;
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path))) {
                DesktopUi.ShowError(this, "Source required", "Choose an existing LGP archive or source folder first.");
                return;
            }

            DataSource source;
            try {
                source = DataSource.Create(path);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Could not open source", ex.Message);
                return;
            }

            RememberSourcePath(key, path);
            try {
                Window window = exporter.ExecuteGui(source);
                window.Owner = this;
                window.ShowDialog();
            } finally {
                if (source is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private void OpenPreferences() {
            string? previousDefault = _preferences.DefaultSourcePath;
            var window = new PreferencesWindow(previousDefault) { Owner = this };
            if (window.ShowDialog() != true)
                return;

            _preferences.DefaultSourcePath = window.DefaultSourcePath;
            try {
                PreferencesStore.Save(_preferences);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Preferences", $"Could not save preferences: {ex.Message}");
            }

            foreach (var row in _sourceRows.Values) {
                if (string.IsNullOrWhiteSpace(row.Path.Text) || row.Path.Text == previousDefault) {
                    row.Path.Text = _preferences.DefaultSourcePath ?? string.Empty;
                    row.Path.ToolTip = row.Path.Text;
                }
            }
        }

        private void RememberSourcePath(string key, string path) {
            _preferences.RememberExporterSourcePath(key, path);
            try {
                PreferencesStore.Save(_preferences);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Preferences", $"Could not save source path: {ex.Message}");
            }
        }

        private sealed class SourceRow {
            public TextBox Path { get; init; } = null!;
        }
    }
}
