// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Text.Json;

namespace CrossSlash {
    internal sealed class CrossSlashPreferences {
        public string? DefaultSourcePath { get; set; }
        public Dictionary<string, string> ExporterSourcePaths { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);
        public string? LastOutputDirectory { get; set; }
        public string? LastExtractDirectory { get; set; }

        public string? GetExporterSourcePath(string exporterKey) {
            ExporterSourcePaths ??= new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            return ExporterSourcePaths.TryGetValue(exporterKey, out string? path) ? path : DefaultSourcePath;
        }

        public void RememberExporterSourcePath(string exporterKey, string path) {
            ExporterSourcePaths ??= new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            ExporterSourcePaths[exporterKey] = path;
        }
    }

    internal static class PreferencesStore {
        private static readonly string PreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossSlash",
            "preferences.json"
        );

        public static CrossSlashPreferences Load() {
            try {
                if (File.Exists(PreferencesPath)) {
                    return JsonSerializer.Deserialize<CrossSlashPreferences>(File.ReadAllText(PreferencesPath))
                        ?? new CrossSlashPreferences();
                }
            } catch {
                // Ignore unreadable or outdated settings and let the user choose a source again.
            }

            return new CrossSlashPreferences();
        }

        public static void Save(CrossSlashPreferences preferences) {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions {
                WriteIndented = true,
            }));
        }

        public static void RememberOutputPath(string path) {
            try {
                var preferences = Load();
                preferences.LastOutputDirectory = Path.GetDirectoryName(path);
                Save(preferences);
            } catch {
                // A failure to remember a dialog location should not interrupt an export.
            }
        }

        public static void RememberOutputDirectory(string path) {
            try {
                var preferences = Load();
                preferences.LastOutputDirectory = path;
                Save(preferences);
            } catch {
                // A failure to remember a dialog location should not interrupt an export.
            }
        }

        public static void RememberExtractDirectory(string path) {
            try {
                var preferences = Load();
                preferences.LastExtractDirectory = path;
                Save(preferences);
            } catch {
                // A failure to remember a dialog location should not interrupt an export.
            }
        }
    }
}
