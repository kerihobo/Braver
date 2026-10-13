// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Windows;
using System.Windows.Controls;

namespace CrossSlash {
    internal sealed class PreferencesWindow : Window {
        private readonly TextBox _txtSourcePath;

        public string? DefaultSourcePath => _txtSourcePath.Text;

        public PreferencesWindow(string? defaultSourcePath) {
            _txtSourcePath = DesktopUi.CreateTextBox(defaultSourcePath);

            var content = new StackPanel();
            content.Children.Add(DesktopUi.CreateSectionTitle("Default source"));
            content.Children.Add(DesktopUi.CreateHint("This path is used as the starting location when an exporter has no saved source."));
            content.Children.Add(DesktopUi.CreateFormRow("LGP or folder path", _txtSourcePath, 150));

            var browseButtons = new StackPanel {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(166, 0, 0, 20),
            };
            var browseLgp = DesktopUi.CreateButton("Browse LGP archive");
            browseLgp.Click += (_, _) => {
                string? path = NativeFilePicker.OpenLgp(_txtSourcePath.Text, this);
                if (path != null)
                    _txtSourcePath.Text = path;
            };
            var browseFolder = DesktopUi.CreateButton("Browse folder");
            browseFolder.Margin = new Thickness(0);
            browseFolder.Click += (_, _) => {
                string? path = NativeFilePicker.OpenFolder("Choose the default source folder", _txtSourcePath.Text, this);
                if (path != null)
                    _txtSourcePath.Text = path;
            };
            browseButtons.Children.Add(browseLgp);
            browseButtons.Children.Add(browseFolder);
            content.Children.Add(browseButtons);

            var actions = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var cancel = DesktopUi.CreateButton("Cancel");
            cancel.Click += (_, _) => Close();
            var save = DesktopUi.CreateButton("Save", primary: true);
            save.Margin = new Thickness(0);
            save.Click += (_, _) => Save();
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            content.Children.Add(actions);

            DesktopUi.ConfigureWindow(
                this,
                "Preferences",
                "Set a default source location",
                content,
                width: 720,
                height: 420
            );
        }

        private void Save() {
            string? path = DefaultSourcePath;
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path)) {
                DesktopUi.ShowError(this, "Invalid path", "Choose an existing LGP archive or folder, or clear the path.");
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
