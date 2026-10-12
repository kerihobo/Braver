// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Forms = System.Windows.Forms;

namespace CrossSlash {
    internal static class NativeFilePicker {
        public static string? OpenLgp(string? currentPath) {
            using var dialog = new Forms.OpenFileDialog {
                Title = "Select an FF7 LGP archive",
                Filter = "LGP archives (*.lgp)|*.lgp|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = GetInitialDirectory(currentPath),
                RestoreDirectory = true,
            };

            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.FileName : null;
        }

        public static string? OpenFolder(string title, string? currentPath) {
            using var dialog = new Forms.FolderBrowserDialog {
                Description = title,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = GetInitialDirectory(currentPath),
            };

            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        public static string? SaveGlb(string? currentPath) {
            using var dialog = new Forms.SaveFileDialog {
                Title = "Save GLB As",
                Filter = "glTF binary (*.glb)|*.glb|All files (*.*)|*.*",
                DefaultExt = "glb",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = GetInitialDirectory(currentPath),
                RestoreDirectory = true,
            };

            if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                dialog.FileName = Path.GetFileName(currentPath);

            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.FileName : null;
        }

        private static string GetInitialDirectory(string? path) {
            try {
                if (!string.IsNullOrWhiteSpace(path)) {
                    string fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                        return fullPath;

                    string? parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        return parent;
                }
            } catch {
                // Fall back to Documents when a remembered path is no longer valid.
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
