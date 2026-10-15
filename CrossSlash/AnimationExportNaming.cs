// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

namespace CrossSlash {
    internal static class AnimationExportNaming {
        public static void PrefixAnimationNames(SharpGLTF.Schema2.ModelRoot model, string modelName, bool enabled) {
            if (!enabled || string.IsNullOrWhiteSpace(modelName))
                return;

            string prefix = modelName + "_";
            foreach (var animation in model.LogicalAnimations) {
                if (string.IsNullOrWhiteSpace(animation.Name) || animation.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                animation.Name = prefix + animation.Name;
            }
        }

        public static string GetAnimationFileName(string modelName, bool enabled) =>
            enabled ? $"{modelName}_Animations.glb" : "Animations.glb";
    }
}
