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

        private Label _lblLGP;
        private DataSource? _source;
        private CrossSlashPreferences _preferences;

        public SplashWindow() {
            _preferences = PreferencesStore.Load();

            Title = "CrossSlash FF7 exporter";

            var btnLGP = new Button {
                Text = "Select LGP file",
                Width = Dim.Percent(25),
            };
            btnLGP.Clicked += () => DoOpen(true);

            var btnFolder = new Button {
                Text = "Select folder",
                Width = Dim.Percent(25),
                X = Pos.Right(btnLGP) + 1,
            };
            btnFolder.Clicked += () => DoOpen(false);

            var btnPreferences = new Button {
                Text = "Preferences...",
                Width = Dim.Percent(25),
                X = Pos.Right(btnFolder) + 1,
            };
            btnPreferences.Clicked += OpenPreferences;

            _lblLGP = new Label {
                Text = "(No source selected)",
                Y = Pos.Bottom(btnPreferences) + 1,
                Width = Dim.Fill(1),
            };

            Add(btnLGP, btnFolder, btnPreferences, _lblLGP);

            Pos y = Pos.Bottom(_lblLGP) + 2;

            foreach(var exporter in ExportKind.Exporters.Values.Where(e => e.HasGui)) {
                Button b = new Button {
                    Text = exporter.Name,
                    Width = Dim.Fill(2),
                    Y = y,
                };
                b.Clicked += () => {
                    if (_source == null) {
                        MessageBox.ErrorQuery("Error", "Select a source LGP or folder", "OK");
                        return;
                    }
                    Application.RequestStop();
                    Application.Run(exporter.ExecuteGui(_source));
                };
                y = Pos.Bottom(b) + 1;
                Add(b);
            }

            if (!string.IsNullOrWhiteSpace(_preferences.DefaultSourcePath))
                TrySelectSource(_preferences.DefaultSourcePath, showError: false);
        }

        private void DoOpen(bool lgp) {
            string? path = lgp
                ? NativeFilePicker.OpenLgp(_preferences.DefaultSourcePath)
                : NativeFilePicker.OpenFolder("Choose the folder to read data from", _preferences.DefaultSourcePath);
            if (path != null)
                TrySelectSource(path, showError: true);
        }

        private void OpenPreferences() {
            var dialog = new PreferencesWindow(_preferences.DefaultSourcePath);
            Application.Run(dialog);
            if (!dialog.Saved)
                return;

            string? path = dialog.DefaultSourcePath;
            if (string.IsNullOrWhiteSpace(path)) {
                if (_source is IDisposable disposable)
                    disposable.Dispose();
                _source = null;
                _lblLGP.Text = "(No source selected)";
                _preferences.DefaultSourcePath = null;
                SavePreferences();
            } else {
                TrySelectSource(path, showError: true);
            }
        }

        private void TrySelectSource(string path, bool showError) {
            DataSource source;
            try {
                source = DataSource.Create(path);
            } catch (Exception ex) {
                if (showError)
                    MessageBox.ErrorQuery("Error", ex.Message, "OK");
                else
                    _lblLGP.Text = $"Saved source could not be opened: {path}";
                return;
            }

            if (_source is IDisposable disposable)
                disposable.Dispose();
            _source = source;
            _lblLGP.Text = path;
            _preferences.DefaultSourcePath = path;
            SavePreferences();
        }

        private void SavePreferences() {
            try {
                PreferencesStore.Save(_preferences);
            } catch (Exception ex) {
                MessageBox.ErrorQuery("Preferences", $"Could not save preferences: {ex.Message}", "OK");
            }
        }

    }
}
