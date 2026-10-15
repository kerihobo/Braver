// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrossSlash {
    internal static class FieldLightingPresetCatalog {
        private static readonly Lazy<IReadOnlyDictionary<string, List<FieldLightingModelPreset>>> _presets = new(Load);

        public static IReadOnlyDictionary<string, List<FieldLightingModelPreset>> Presets => _presets.Value;
        public static IReadOnlyList<string> FieldIds => Presets.Keys.OrderBy(fieldId => fieldId, StringComparer.OrdinalIgnoreCase).ToList();

        private static IReadOnlyDictionary<string, List<FieldLightingModelPreset>> Load() {
            Assembly assembly = typeof(FieldLightingPresetCatalog).Assembly;
            string resourceName = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(".FieldLightingPresets.json", StringComparison.OrdinalIgnoreCase));
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Could not load embedded field-lighting presets: {resourceName}");
            return JsonSerializer.Deserialize<Dictionary<string, List<FieldLightingModelPreset>>>(stream)
                ?? throw new InvalidDataException("The embedded field-lighting preset data is empty.");
        }
    }

    internal sealed class FieldLightingModelPreset {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("hrcId")]
        public string HrcId { get; set; } = string.Empty;

        [JsonPropertyName("globalLight")]
        public FieldLightingColorPreset GlobalLight { get; set; } = new();

        [JsonPropertyName("light1")]
        public FieldLightingPointPreset Light1 { get; set; } = new();

        [JsonPropertyName("light2")]
        public FieldLightingPointPreset Light2 { get; set; } = new();

        [JsonPropertyName("light3")]
        public FieldLightingPointPreset Light3 { get; set; } = new();

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(HrcId) ? Name : $"{HrcId} — {Name}";

        public IReadOnlyList<FieldLightingPointPreset> PointLights => new[] { Light1, Light2, Light3 };
    }

    internal class FieldLightingColorPreset {
        [JsonPropertyName("r")]
        public int R { get; set; }

        [JsonPropertyName("g")]
        public int G { get; set; }

        [JsonPropertyName("b")]
        public int B { get; set; }

        public int ToPackedRgb() => (R << 16) | (G << 8) | B;
    }

    internal sealed class FieldLightingPointPreset : FieldLightingColorPreset {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }
    }
}
