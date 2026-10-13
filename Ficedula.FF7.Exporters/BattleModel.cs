// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Battle;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Ficedula.FF7.Exporters {

    public class BattleModelOptions : ModelBaseOptions {
        public float Scale { get; set; } = 1f;
    }

    public class BattleModel : ModelBase {
        private DataSource _source;

        private BattleModelOptions Options => _options as BattleModelOptions;


        public BattleModel(DataSource source, BattleModelOptions options) {
            _source = source;
            _options = options;
        }

        private SharpGLTF.Schema2.ModelRoot BuildScene(string skeleton, string anims, IEnumerable<string> texs, Func<string?> nextFile, IReadOnlyList<(string Name, PFile Model)>? weaponParts = null, int bodyAnimationCount = 0, int weaponAnimationCount = 0) {
            var scene = new SceneBuilder();

            Animations animations;
            using (var s = _source.Open(anims))
                animations = new Animations(s);
            BBone rootBone;
            using(var s = _source.Open(skeleton))
                rootBone = BBone.Decode(s);

            int bodyBoneCount = rootBone.ThisAndDescendants().Count(b => b.Index >= 0);
            BBone? weaponBone = null;

            var textures = texs
                .Select(t => {
                    using (var s = _source.TryOpen(t))
                        return s == null ? null : new Ficedula.FF7.TexFile(s);
                })
                .Where(tex => tex != null)
                .ToArray();

            var materials = ConvertTextures(textures);
            var orderedMaterials = textures.Select(t => materials[t]).ToList();
            MaterialBuilder defMaterial = GetUntexturedMaterial();

            List<PFile> pFiles = new();
            foreach (var bone in rootBone.ThisAndDescendants().Where(b => b.PFileIndex != null).OrderBy(b => b.PFileIndex.Value)) {
                while (pFiles.Count <= bone.PFileIndex.Value)
                    pFiles.Add(null);
                using (var s = _source.Open(nextFile()))
                    pFiles[bone.PFileIndex.Value] = new PFile(s);
            }

            if (weaponParts?.Count > 0) {
                weaponBone = new BBone { Index = bodyBoneCount };
                rootBone.Children.Add(weaponBone);
            }

            var firstFrame = animations.Anims[0].Frames[0];

            var allNodes = new Dictionary<int, NodeBuilder>();
            NodeBuilder? weaponNode = null;

            Frame? firstWeaponFrame = weaponAnimationCount > 0 && bodyAnimationCount < animations.Anims.Count
                ? animations.Anims[bodyAnimationCount]?.Frames.FirstOrDefault()
                : null;

            void Descend(SceneBuilder scene, BBone bone, NodeBuilder node, Vector3 translation, Vector3? scale) {
                var boneRotation = bone == weaponBone && firstWeaponFrame != null
                    ? firstWeaponFrame.Rotations[0]
                    : bone.Index >= 0 && bone.Index < bodyBoneCount && bone.Index + 1 < firstFrame.Rotations.Length
                        ? firstFrame.Rotations[bone.Index + 1]
                        : default;
                var rotation = Quaternion.CreateFromYawPitchRoll(
                    (360 * boneRotation.rY / 4096f) * (float)Math.PI / 180,
                    (360 * boneRotation.rX / 4096f) * (float)Math.PI / 180,
                    (360 * boneRotation.rZ / 4096f) * (float)Math.PI / 180
                );

                node.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(
                    null, scale ?? Vector3.One, rotation, translation
                );

                foreach (var child in bone.Children) {
                    var c = node.CreateNode(child == weaponBone ? "WEAPON" : child.Index.ToString());
                    allNodes[child.Index] = c;
                    if (child == weaponBone)
                        weaponNode = c;
                    Descend(scene, child, c, new Vector3(0, 0, bone.Length), null);
                }
            }

            void DescendMesh(SceneBuilder scene, BBone bone, NodeBuilder node, Matrix4x4 transform, NodeBuilder[] joints) {
                if (bone.PFileIndex != null && bone != weaponBone) {
                    foreach (var mesh in BuildMeshes(pFiles[bone.PFileIndex.Value], orderedMaterials, defMaterial, bone.Index)) {
                        scene.AddSkinnedMesh(mesh, transform, joints);
                    }
                }
                foreach (var child in bone.Children) {
                    var childNode = allNodes[child.Index];
                    var childTransform = childNode.LocalMatrix * transform;
                    DescendMesh(scene, child, childNode, childTransform, joints);
                }
            }

            var rootContainer = new NodeBuilder("RootContainer");
            var root = rootContainer.CreateNode("-1");
            Descend(scene, rootBone, root, Vector3.Zero, new Vector3(Options.Scale));

            var firstRootRotation = firstFrame.Rotations.Length > 0 ? firstFrame.Rotations[0] : default;
            var firstRootQuaternion = Quaternion.CreateFromYawPitchRoll(
                -(360 * firstRootRotation.rY / 4096f) * (float)Math.PI / 180,
                (180 + 360 * firstRootRotation.rX / 4096f) * (float)Math.PI / 180,
                (360 * firstRootRotation.rZ / 4096f) * (float)Math.PI / 180
            );
            root.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(
                null,
                new Vector3(Options.Scale),
                firstRootQuaternion,
                new Vector3(firstFrame.X, -firstFrame.Y, firstFrame.Z) * Options.Scale
            );

            DescendMesh(
                scene, rootBone, root, root.LocalMatrix,
                allNodes.Where(kv => kv.Key >= 0).OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray()
            );
            scene.AddNode(rootContainer);
            if (weaponParts?.Count > 0 && weaponNode != null && weaponBone != null) {
                for (int weaponIndex = 0; weaponIndex < weaponParts.Count; weaponIndex++) {
                    var weaponPart = weaponParts[weaponIndex];
                    var partNode = weaponNode.CreateNode(weaponPart.Name);
                    foreach (var mesh in BuildMeshes(weaponPart.Model, orderedMaterials, defMaterial, weaponBone.Index))
                        scene.AddRigidMesh(mesh, partNode);
                }
            }

            FinishBaking();

            var settings = SceneBuilderSchema2Settings.Default;
            settings.UseStridedBuffers = false;
            var model = scene.ToGltf2(settings);

            // Create animations for skeleton nodes (ones with numeric names) as before
            int numBodyAnimations = weaponParts?.Count > 0 ? bodyAnimationCount : animations.Anims.Count;
            for (int animationIndex = 0; animationIndex < numBodyAnimations; animationIndex++) {
                var anim = animations.Anims[animationIndex];
                if (anim == null)
                    continue;
                if (anim.Bones <= bodyBoneCount)
                    continue;

                var mAnim = model.CreateAnimation();
                mAnim.Name = "Anim" + (model.LogicalAnimations.Count - 1);

                var rootNode = model.LogicalNodes.FirstOrDefault(n => n.Name == "RootContainer");
                if (rootNode != null) {
                    var rootRots = new Dictionary<float, Quaternion>();
                    var rootTrans = new Dictionary<float, Vector3>();
                    for (int frameIndex = 0; frameIndex < anim.Frames.Length; frameIndex++) {
                        var frame = anim.Frames[frameIndex];
                        if (frame == null)
                            continue;

                        float time = frameIndex / 15f;
                        var rotation = frame.Rotations.Length > 0 ? frame.Rotations[0] : default;
                        rootRots[time] = Quaternion.CreateFromYawPitchRoll(
                            -(360 * rotation.rY / 4096f) * (float)Math.PI / 180,
                            (360 * rotation.rX / 4096f) * (float)Math.PI / 180,
                            (360 * rotation.rZ / 4096f) * (float)Math.PI / 180
                        );
                        // Keep the same small root-origin adjustment as Kujata's battle export.
                        rootTrans[time] = new Vector3(
                            frame.X,
                            -frame.Y - 13.535284f,
                            -frame.Z - 0.077069275f
                        ) * Options.Scale;
                    }
                    if (rootRots.Count > 0)
                        mAnim.CreateRotationChannel(rootNode, rootRots);
                    if (rootTrans.Count > 0)
                        mAnim.CreateTranslationChannel(rootNode, rootTrans);
                }

                foreach (var node in model.LogicalNodes) {
                    if (!int.TryParse(node.Name, out int boneIndex))
                        continue;
                    if (boneIndex < 0)
                        continue;

                    Dictionary<float, Quaternion> rots = new Dictionary<float, Quaternion>();

                    if (boneIndex >= bodyBoneCount)
                        continue;

                    for (int frameIndex = 0; frameIndex < anim.Frames.Length; frameIndex++) {
                        var frame = anim.Frames[frameIndex];
                        if (frame == null || frame.Rotations.Length <= boneIndex + 1)
                            continue;

                        var rotation = Quaternion.CreateFromYawPitchRoll(
                            (360 * frame.Rotations[boneIndex + 1].rY / 4096f) * (float)Math.PI / 180,
                            (360 * frame.Rotations[boneIndex + 1].rX / 4096f) * (float)Math.PI / 180,
                            (360 * frame.Rotations[boneIndex + 1].rZ / 4096f) * (float)Math.PI / 180
                        );

                        rots[frameIndex / 15f] = rotation;
                    }

                    mAnim.CreateRotationChannel(node, rots);
                }
                if (weaponBone != null && animationIndex < weaponAnimationCount && animationIndex + bodyAnimationCount < animations.Anims.Count) {
                    var weaponAnim = animations.Anims[bodyAnimationCount + animationIndex];
                    var targetNode = model.LogicalNodes.FirstOrDefault(n => n.Name == "WEAPON");
                    if (weaponAnim != null && weaponAnim.Bones == 1 && targetNode != null) {
                        Dictionary<float, Quaternion> weaponRots = new();
                        Dictionary<float, Vector3> weaponTrans = new();
                        for (int frameIndex = 0; frameIndex < anim.Frames.Length; frameIndex++) {
                            var frame = weaponAnim.Frames[Math.Min(frameIndex, weaponAnim.Frames.Length - 1)];
                            var bodyFrame = anim.Frames[frameIndex];
                            float time = frameIndex / 15f;
                            // Frame.Y is stored in the opposite sign convention from Kujata's
                            // decoded root translation, so the relative weapon Y offset keeps
                            // the raw delta's sign here.
                            weaponTrans[time] = new Vector3(frame.X - bodyFrame.X, frame.Y - bodyFrame.Y, frame.Z - bodyFrame.Z) * Options.Scale;
                            weaponRots[time] = Quaternion.CreateFromYawPitchRoll(
                                (360 * frame.Rotations[0].rY / 4096f) * (float)Math.PI / 180,
                                (360 * frame.Rotations[0].rX / 4096f) * (float)Math.PI / 180,
                                (360 * frame.Rotations[0].rZ / 4096f) * (float)Math.PI / 180
                            );
                        }
                        if (weaponTrans.Count > 0)
                            mAnim.CreateTranslationChannel(targetNode, weaponTrans);
                        if (weaponRots.Count > 0)
                            mAnim.CreateRotationChannel(targetNode, weaponRots);
                    }
                }
            }

            return model;
        }

        public SharpGLTF.Schema2.ModelRoot BuildSceneAuto(string name) {
            if (_source.Exists(name + ".a00"))
                return BuildSceneFromSummon(name);
            else if (IsBattleLocation(_source, name))
                return BuildSceneFromBattleLocation(name);
            else if (_source.Exists(name + "DA"))
                return BuildSceneFromModel(name);
            else
                throw new Exception($"Can't detect battle model type");
        }

        public SharpGLTF.Schema2.ModelRoot BuildSceneFromSummon(string summonName) {
            //TODO - very similar to code in main Braver project - move into Ficedula.FF7
            var texs = Enumerable.Range(0, 99).Select(i => $"{summonName}.t{i:00}");
            int part = 0;
            Func<string?> NextData = () => {
                string data;
                do {
                    if (part >= 99)
                        return null;
                    data = $"{summonName}.p{part:00}";
                    part++;
                } while (!_source.Exists(data));
                return data;
            };
            return BuildScene(summonName + ".d", summonName + ".a00", texs, NextData);
        }

        public SharpGLTF.Schema2.ModelRoot BuildSceneFromBattleLocation(string locationCode) {
            locationCode = NormalizeBattleLocationCode(locationCode);

            (int skeletonType, int boneCount, int jointCount, int textureCount) header;
            using (var skeleton = _source.Open(locationCode + "aa"))
                header = ReadBattleSkeletonHeader(skeleton);

            if (header.skeletonType != 1 || header.boneCount != 0)
                throw new InvalidDataException($"{locationCode}aa is not a battle location skeleton.");
            if (header.jointCount <= 0)
                throw new InvalidDataException($"{locationCode}aa does not define any location parts.");
            if (header.textureCount < 0)
                throw new InvalidDataException($"{locationCode}aa has an invalid texture count.");

            var textureSlots = Enumerable.Range(0, Math.Min(header.textureCount, 10))
                .Select(index => {
                    string textureName = $"{locationCode}a{(char)('c' + index)}";
                    using (var stream = _source.TryOpen(textureName))
                        return stream == null ? null : new Ficedula.FF7.TexFile(stream);
                })
                .ToArray();
            var materials = ConvertTextures(textureSlots.OfType<Ficedula.FF7.TexFile>());
            MaterialBuilder defaultMaterial = GetUntexturedMaterial();
            var orderedMaterials = textureSlots
                .Select(texture => texture == null ? defaultMaterial : materials[texture])
                .ToList();

            var scene = new SceneBuilder();
            var root = new NodeBuilder(locationCode.ToUpperInvariant());
            root.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(
                null,
                new Vector3(Options.Scale),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI),
                Vector3.Zero
            );

            for (int partIndex = 0; partIndex < header.jointCount; partIndex++) {
                string partName = GetBattleLocationPartName(locationCode, partIndex);
                using var stream = _source.TryOpen(partName);
                if (stream == null)
                    throw new InvalidDataException($"Battle location {locationCode} is missing part {partName}.");

                var partNode = root.CreateNode(partName.ToUpperInvariant());
                var part = new PFile(stream);
                foreach (var mesh in BuildMeshes(part, orderedMaterials, defaultMaterial, 0))
                    scene.AddRigidMesh(mesh, partNode);
            }

            scene.AddNode(root);
            FinishBaking();

            var settings = SceneBuilderSchema2Settings.Default;
            settings.UseStridedBuffers = false;
            return scene.ToGltf2(settings);
        }

        public static bool IsBattleLocation(DataSource source, string locationCode) {
            try {
                locationCode = NormalizeBattleLocationCode(locationCode);
                using var skeleton = source.TryOpen(locationCode + "aa");
                if (skeleton == null)
                    return false;

                var header = ReadBattleSkeletonHeader(skeleton);
                return header.skeletonType == 1 && header.boneCount == 0;
            } catch {
                return false;
            }
        }

        public static string NormalizeBattleLocationCode(string locationCode) {
            string code = Path.GetFileNameWithoutExtension(locationCode.Trim());
            if (code.Length == 4 && code.EndsWith("aa", StringComparison.OrdinalIgnoreCase))
                code = code[..2];
            if (code.Length != 2)
                throw new ArgumentException("Enter a two-letter battlefield code or its skeleton filename, such as px or pxaa.");
            return code;
        }

        private static (int skeletonType, int boneCount, int jointCount, int textureCount) ReadBattleSkeletonHeader(Stream skeleton) {
            if (skeleton.Length < 52)
                throw new InvalidDataException("Battle skeleton header is incomplete.");

            skeleton.Position = 0;
            int skeletonType = skeleton.ReadI32();
            skeleton.Position = 12;
            int boneCount = skeleton.ReadI32();
            skeleton.Position = 20;
            int jointCount = skeleton.ReadI32();
            int textureCount = skeleton.ReadI32();
            return (skeletonType, boneCount, jointCount, textureCount);
        }

        private static string GetBattleLocationPartName(string locationCode, int partIndex) {
            int suffixIndex = partIndex + 12;
            return locationCode + (char)('a' + suffixIndex / 26) + (char)('a' + suffixIndex % 26);
        }

        public SharpGLTF.Schema2.ModelRoot BuildSceneFromModel(string modelCode) {
            //TODO - very similar to code in main Braver project - move into Ficedula.FF7
            var texs = Enumerable.Range((int)'c', 10).Select(i => modelCode + "a" + ((char)i).ToString());
            int codecounter = 12;
            Func<string?> NextData = () => {
                char c1, c2;
                string data;
                do {
                    if (codecounter >= 260)
                        return null;
                    c1 = (char)('a' + (codecounter / 26));
                    c2 = (char)('a' + (codecounter % 26));
                    codecounter++;
                    data = modelCode + c1.ToString() + c2.ToString();
                } while (!_source.Exists(data));
                return data;
            };

            int bodyAnimationCount, weaponAnimationCount;
            using (var s = _source.Open(modelCode + "aa")) {
                s.Position = 0x1C;
                bodyAnimationCount = s.ReadI32();
                s.Position = 0x28;
                weaponAnimationCount = s.ReadI32();
            }

            List<(string Name, PFile Model)> weaponParts = new();
            for (char suffix = 'k'; suffix <= 'z'; suffix++) {
                using (var s = _source.TryOpen(modelCode + "c" + suffix)) {
                    if (s == null)
                        continue;
                    try {
                        weaponParts.Add(($"weapon_{modelCode}c{suffix}", new PFile(s)));
                    } catch {
                        // Skip entries that are not polygon files.
                    }
                }
            }

            return BuildScene(modelCode + "aa", modelCode + "da", texs, NextData, weaponParts,
                bodyAnimationCount, weaponAnimationCount);
        }
    }
}
