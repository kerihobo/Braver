// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Terminal.Gui;

namespace CrossSlash {
    internal sealed class PreferencesWindow : Window {
        private readonly TextField _txtSourcePath;

        public bool Saved { get; private set; }
        public string? DefaultSourcePath => _txtSourcePath.Text?.ToString();

        public PreferencesWindow(string? defaultSourcePath) {
            Title = "CrossSlash Preferences";

            var lblSource = new Label {
                Text = "Default LGP or source folder",
                Width = Dim.Percent(32),
            };
            _txtSourcePath = new TextField {
                X = Pos.Right(lblSource) + 1,
                Y = lblSource.Y,
                Width = Dim.Fill(1),
                Text = defaultSourcePath ?? string.Empty,
            };

            var btnLgp = new Button {
                Text = "Browse LGP...",
                Y = Pos.Bottom(_txtSourcePath) + 1,
            };
            btnLgp.Clicked += () => {
                string? path = NativeFilePicker.OpenLgp(_txtSourcePath.Text?.ToString());
                if (path != null)
                    _txtSourcePath.Text = path;
            };

            var btnFolder = new Button {
                Text = "Browse folder...",
                X = Pos.Right(btnLgp) + 1,
                Y = btnLgp.Y,
            };
            btnFolder.Clicked += () => {
                string? path = NativeFilePicker.OpenFolder(
                    "Choose the default folder to read data from",
                    _txtSourcePath.Text?.ToString()
                );
                if (path != null)
                    _txtSourcePath.Text = path;
            };

            var btnSave = new Button {
                Text = "Save",
                Y = Pos.Bottom(btnFolder) + 1,
            };
            btnSave.Clicked += () => {
                string? path = DefaultSourcePath;
                if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path)) {
                    MessageBox.ErrorQuery("Invalid path", "Choose an existing LGP file or folder, or clear the path.", "OK");
                    return;
                }

                Saved = true;
                Application.RequestStop();
            };

            var btnCancel = new Button {
                Text = "Cancel",
                X = Pos.Right(btnSave) + 1,
                Y = btnSave.Y,
            };
            btnCancel.Clicked += () => Application.RequestStop();

            Add(lblSource, _txtSourcePath, btnLgp, btnFolder, btnSave, btnCancel);
        }
    }
}
