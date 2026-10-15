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
    public sealed class SplashWindow : Window {
        private readonly CrossSlashPreferences _preferences;
        private readonly List<ExporterTab> _exporterTabs = new();
        private readonly TabControl _tabs;

        public SplashWindow() {
            DesktopUi.MainWindow = this;
            Closed += (_, _) => {
                foreach (var tab in _exporterTabs) {
                    if (tab.Source is IDisposable disposable)
                        disposable.Dispose();
                    tab.Source = null;
                }
                DesktopUi.MainWindow = null;
            };
            _preferences = PreferencesStore.Load();

            _tabs = new TabControl { TabStripPlacement = Dock.Top };
            foreach (var entry in ExportKind.Exporters.Where(e => e.Value.HasGui)) {
                var tab = CreateExporterTab(entry.Key, entry.Value);
                _exporterTabs.Add(tab);
                _tabs.Items.Add(new TabItem {
                    Header = GetTabHeader(entry.Key, entry.Value),
                    Content = tab.Content,
                });
            }
            _tabs.SelectionChanged += (_, _) => EnsureSelectedTabLoaded();

            DesktopUi.ConfigureWindow(
                this,
                _tabs,
                width: 1120,
                height: 820,
                scrollContent: false
            );
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            int fieldIndex = _exporterTabs.FindIndex(tab => tab.Key.Equals("FieldModel", StringComparison.OrdinalIgnoreCase));
            _tabs.SelectedIndex = fieldIndex >= 0 ? fieldIndex : (_exporterTabs.Count > 0 ? 0 : -1);
            EnsureSelectedTabLoaded();
        }

        private ExporterTab CreateExporterTab(string key, ExportKind exporter) {
            string? savedPath = _preferences.GetExporterSourcePath(key);
            bool savedFolder = !string.IsNullOrWhiteSpace(savedPath) && Directory.Exists(savedPath);
            bool savedFile = !string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath);

            var sourceType = new ComboBox {
                ItemsSource = new[] { "LGP archive", "Folder" },
                SelectedIndex = savedFolder ? 1 : 0,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = DesktopUi.InputBackgroundBrush,
                Foreground = DesktopUi.TextPrimaryBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
            };
            var sourcePath = DesktopUi.CreateTextBox(savedPath, readOnly: true);
            sourcePath.ToolTip = savedPath ?? "No source selected";
            sourcePath.Margin = new Thickness(0, 0, 10, 0);

            var sourceRow = new Grid();
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceRow.Children.Add(sourceType);
            Grid.SetColumn(sourcePath, 1);
            sourceRow.Children.Add(sourcePath);

            var browse = DesktopUi.CreateButton("Browse", primary: true);
            browse.Margin = new Thickness(0);
            Grid.SetColumn(browse, 2);
            sourceRow.Children.Add(browse);

            sourceRow.Margin = new Thickness(0, 0, 0, 20);

            var view = new ContentControl {
                Content = CreateStatus(savedFile || savedFolder
                    ? "Select this tab to load its saved source."
                    : "Choose an LGP archive or folder above to load this exporter."),
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            var content = new Grid { Margin = new Thickness(8) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(sourceRow);
            Grid.SetRow(view, 1);
            content.Children.Add(view);

            var state = new ExporterTab(key, exporter, sourceType, sourcePath, view, content);
            browse.Click += (_, _) => BrowseSource(state);
            return state;
        }

        private static string GetTabHeader(string key, ExportKind exporter) => key switch {
            "FieldModel" => "Field",
            "FieldLocation" => "Field Locations",
            "BattleModel" => "Battle",
            "MagicModel" => "Magic",
            "Tex" => "Textures",
            _ => exporter.Name,
        };

        private static UIElement CreateStatus(string text) => new TextBlock {
            Text = text,
            Foreground = DesktopUi.TextSecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(24),
        };

        private void EnsureSelectedTabLoaded() {
            int index = _tabs.SelectedIndex;
            if (index < 0 || index >= _exporterTabs.Count)
                return;

            ExporterTab tab = _exporterTabs[index];
            if (tab.Source != null)
                return;

            string path = tab.SourcePath.Text;
            if (string.IsNullOrWhiteSpace(path)) {
                tab.View.Content = CreateStatus("Choose an LGP archive or folder above to load this exporter.");
                return;
            }
            if (!File.Exists(path) && !Directory.Exists(path)) {
                tab.View.Content = CreateStatus("The saved source could not be found. Browse to choose another source.");
                return;
            }
            if (string.Equals(tab.FailedPath, path, StringComparison.OrdinalIgnoreCase))
                return;

            LoadExporter(tab, path);
        }

        private void BrowseSource(ExporterTab tab) {
            string? currentPath = tab.SourcePath.Text;
            string? path = tab.SourceType.SelectedIndex switch {
                0 => NativeFilePicker.OpenLgp(currentPath, this),
                1 => NativeFilePicker.OpenFolder("Choose the source folder", currentPath, this),
                _ => null,
            };
            if (path != null)
                LoadExporter(tab, path);
        }

        private bool LoadExporter(ExporterTab tab, string path) {
            DataSource? source = null;
            try {
                source = DataSource.Create(path);
                Window screen = tab.Exporter.ExecuteGui(source);
                if (screen.Content is not UIElement content)
                    throw new InvalidOperationException("The exporter screen has no content.");

                screen.Content = null;
                DataSource? previousSource = tab.Source;
                tab.View.Content = content;
                tab.Source = source;
                tab.Screen = screen;
                tab.FailedPath = null;
                tab.SourcePath.Text = path;
                tab.SourcePath.ToolTip = path;
                tab.SourceType.SelectedIndex = Directory.Exists(path) ? 1 : 0;
                source = null;

                if (previousSource is IDisposable disposable)
                    disposable.Dispose();
                RememberSourcePath(tab.Key, path);
                return true;
            } catch (Exception ex) {
                tab.FailedPath = path;
                if (source is IDisposable disposable)
                    disposable.Dispose();
                if (tab.Source == null)
                    tab.View.Content = CreateStatus("Could not load this source. Browse to choose another source.");
                DesktopUi.ShowError(this, "Could not open exporter", ex.Message);
                return false;
            }
        }

        private void RememberSourcePath(string key, string path) {
            _preferences.RememberExporterSourcePath(key, path);
            try {
                PreferencesStore.Save(_preferences);
            } catch (Exception ex) {
                DesktopUi.ShowError(this, "Could not save source path", ex.Message);
            }
        }

        private sealed class ExporterTab {
            public string Key { get; }
            public ExportKind Exporter { get; }
            public ComboBox SourceType { get; }
            public TextBox SourcePath { get; }
            public ContentControl View { get; }
            public UIElement Content { get; }
            public DataSource? Source { get; set; }
            public Window? Screen { get; set; }
            public string? FailedPath { get; set; }

            public ExporterTab(string key, ExportKind exporter, ComboBox sourceType, TextBox sourcePath, ContentControl view, UIElement content) {
                Key = key;
                Exporter = exporter;
                SourceType = sourceType;
                SourcePath = sourcePath;
                View = view;
                Content = content;
            }
        }
    }
}
