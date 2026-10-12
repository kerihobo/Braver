// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System;
using System.Linq;
using Terminal.Gui;

namespace CrossSlash {
    public class SplashWindow : Window {
        private readonly CrossSlashPreferences _preferences;

        public SplashWindow() {
            _preferences = PreferencesStore.Load();

            Title = "CrossSlash FF7 exporter";

            var hint = new Label {
                Text = "Choose a source for each exporter. The paths are remembered separately.",
                Width = Dim.Fill(1),
            };
            var exporterHeader = new Label {
                Text = "Export type",
                Y = Pos.Bottom(hint) + 1,
                Width = Dim.Percent(30),
                TextAlignment = TextAlignment.Right,
            };
            var sourceHeader = new Label {
                Text = "Source LGP or folder",
                X = Pos.Percent(39),
                Y = exporterHeader.Y,
            };

            Add(hint, exporterHeader, sourceHeader);

            Pos y = Pos.Bottom(exporterHeader) + 1;
            foreach (var entry in ExportKind.Exporters.Where(e => e.Value.HasGui)) {
                string key = entry.Key;
                ExportKind exporter = entry.Value;

                string? savedPath = _preferences.GetExporterSourcePath(key);
                var sourcePath = new Label {
                    Text = string.IsNullOrWhiteSpace(savedPath) ? "(No source selected)" : savedPath,
                    X = Pos.Percent(39),
                    Y = y,
                    Width = Dim.Percent(43),
                };
                var browse = new Button {
                    Text = "Browse...",
                    X = Pos.Right(sourcePath) + 1,
                    Y = y,
                    Width = Dim.Fill(1),
                };
                browse.Clicked += () => BrowseSource(key, sourcePath);

                var exportName = new Label {
                    Text = exporter.Name,
                    Width = Dim.Percent(34),
                    Y = y,
                    CanFocus = true,
                    TextAlignment = TextAlignment.Right,
                };
                exportName.Clicked += () => RunExporter(key, exporter, sourcePath);

                Add(exportName, sourcePath, browse);
                y = Pos.Bottom(exportName) + 1;
            }
        }

        private void BrowseSource(string key, Label sourcePath) {
            int choice = MessageBox.Query(
                "Choose source type",
                "Browse for an LGP archive or a source folder",
                "LGP archive",
                "Folder",
                "Cancel"
            );

            string? currentPath = sourcePath.Text?.ToString();
            string? path = choice switch {
                0 => NativeFilePicker.OpenLgp(currentPath),
                1 => NativeFilePicker.OpenFolder("Choose the source folder", currentPath),
                _ => null,
            };

            if (path == null)
                return;

            sourcePath.Text = path;
            RememberSourcePath(key, path);
        }

        private void RunExporter(string key, ExportKind exporter, Label sourcePath) {
            string? path = sourcePath.Text?.ToString();
            if (string.IsNullOrWhiteSpace(path) || path == "(No source selected)") {
                MessageBox.ErrorQuery("Source required", "Browse for a source LGP archive or folder.", "OK");
                return;
            }

            DataSource source;
            try {
                source = DataSource.Create(path);
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Could not open source", ex.Message, "OK");
                return;
            }

            RememberSourcePath(key, path);
            try {
                Application.Run(exporter.ExecuteGui(source));
            } finally {
                if (source is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private void RememberSourcePath(string key, string path) {
            _preferences.RememberExporterSourcePath(key, path);
            try {
                PreferencesStore.Save(_preferences);
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Preferences", $"Could not save preferences: {ex.Message}", "OK");
            }
        }
    }
}
