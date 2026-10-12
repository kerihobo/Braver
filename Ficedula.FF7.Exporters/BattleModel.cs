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

        private SharpGLTF.Schema2.ModelRoot BuildScene(string skeleton, string anims, IEnumerable<string> texs, Func<string?> nextFile, IEnumerable<PFile>? extraParts = null) {
            var scene = new SceneBuilder();

            Animations animations;
            using (var s = _source.Open(anims))
                animations = new Animations(s);
            BBone rootBone;
            using(var s = _source.Open(skeleton))
                rootBone = BBone.Decode(s);

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

            var firstFrame = animations.Anims[0].Frames[0];

            int maxBone = 0;
            var allNodes = new Dictionary<int, NodeBuilder>();

            void Descend(SceneBuilder scene, BBone bone, NodeBuilder node, Vector3 translation, Vector3? scale) {
                maxBone = Math.Max(maxBone, bone.Index);

                var rotation = Quaternion.CreateFromYawPitchRoll(
                    (360 * firstFrame.Rotations[bone.Index + 1].rY / 4096f) * (float)Math.PI / 180,
                    (360 * firstFrame.Rotations[bone.Index + 1].rX / 4096f) * (float)Math.PI / 180,
                    (360 * firstFrame.Rotations[bone.Index + 1].rZ / 4096f) * (float)Math.PI / 180
                );

                node.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(
                    null, scale ?? Vector3.One, rotation, translation
                );

                foreach (var child in bone.Children) {
                    var c = node.CreateNode(child.Index.ToString());
                    allNodes[child.Index] = c;
                    Descend(scene, child, c, new Vector3(0, 0, bone.Length), null);
                }
            }

            void DescendMesh(SceneBuilder scene, BBone bone, NodeBuilder node, Matrix4x4 transform, NodeBuilder[] joints) {
                if (bone.PFileIndex != null) {
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

            var root = new NodeBuilder("-1");
            root.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI)
            );
            Descend(scene, rootBone, root, Vector3.Zero, new Vector3(Options.Scale));
            DescendMesh(
                scene, rootBone, root, root.LocalMatrix,
                allNodes.Where(kv => kv.Key >= 0).OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray()
            );
            scene.AddNode(root);

            // If there are extra standalone PFiles (e.g. separate weapon/attachment models)
            // create a node per extra part and add their meshes as children of that node.
            var weaponNodes = new List<NodeBuilder>();
            if (extraParts != null) {
                int wi = 0;
                foreach (var p in extraParts) {
                    try {
                        // Create a weapon node under root so it is a sibling of the skeleton group
                        var wnode = root.CreateNode($"weapon_{wi}");
                        weaponNodes.Add(wnode);

                        foreach (var mesh in BuildMeshes(p, orderedMaterials, defMaterial, 0)) {
                            try {
                                // Attach the mesh using the weapon node's current local matrix
                                scene.AddSkinnedMesh(mesh, wnode.LocalMatrix, new NodeBuilder[0]);
                            } catch {
                                // Fallback: add as rigid with identity
                                scene.AddRigidMesh(mesh, Matrix4x4.Identity);
                            }
                        }

                        wi++;
                    } catch {
                        // ignore parse/build errors for extra parts
                    }
                }
            }

            FinishBaking();

            var settings = SceneBuilderSchema2Settings.Default;
            settings.UseStridedBuffers = false;
            var model = scene.ToGltf2(settings);

            // Create animations for skeleton nodes (ones with numeric names) as before
            foreach (var anim in animations.Anims) {
                if (anim == null)
                    continue;
                if (anim.Bones <= (maxBone + 1))
                    continue;

                var mAnim = model.CreateAnimation();
                mAnim.Name = "Anim" + (model.LogicalAnimations.Count - 1);

                foreach (var node in model.LogicalNodes) {
                    int c = 0;
                    if (!int.TryParse(node.Name, out int boneIndex))
                        continue;

                    Dictionary<float, Quaternion> rots = new Dictionary<float, Quaternion>();
                    Dictionary<float, Vector3> trans = new Dictionary<float, Vector3>();

                    foreach (var frame in anim.Frames) {
                        float additionalX = 0;
                        if (node.VisualRoot == node) {
                            trans[c / 15f] = new Vector3(frame.X, -frame.Y, frame.Z) * Options.Scale;
                            additionalX = 180;
                        }

                        var rotation = Quaternion.CreateFromYawPitchRoll(
                            (360 * frame.Rotations[boneIndex + 1].rY / 4096f) * (float)Math.PI / 180,
                            (additionalX + 360 * frame.Rotations[boneIndex + 1].rX / 4096f) * (float)Math.PI / 180,
                            (360 * frame.Rotations[boneIndex + 1].rZ / 4096f) * (float)Math.PI / 180
                        );

                        rots[c / 15f] = rotation;

                        c++;
                    }

                    if (node.VisualRoot == node)
                        mAnim.CreateTranslationChannel(node, trans);
                    mAnim.CreateRotationChannel(node, rots);
                }
            }

            // Create animations for weapon nodes using animations that contain a single bone (weapon anims)
            var weaponAnims = animations.Anims.Where(a => a != null && a.Bones == 1).ToList();
            if (weaponAnims.Count > 0) {
                // Map weaponAnims in order to weapon_x nodes created earlier
                int wi = 0;
                foreach (var wanim in weaponAnims) {
                    if (wi >= weaponNodes.Count)
                        break;

                    var nodeName = weaponNodes[wi].Name;
                    var targetNode = model.LogicalNodes.FirstOrDefault(n => n.Name == nodeName);
                    if (targetNode != null) {
                        var mAnim = model.CreateAnimation();
                        mAnim.Name = "AnimWeapon" + wi;

                        Dictionary<float, Quaternion> rots = new Dictionary<float, Quaternion>();
                        Dictionary<float, Vector3> trans = new Dictionary<float, Vector3>();

                        int c = 0;
                        foreach (var frame in wanim.Frames) {
                            float time = c / 15f;
                            // Weapon translation: do NOT negate Y (authored in visual space)
                            trans[time] = new Vector3(frame.X, frame.Y, frame.Z) * Options.Scale;

                            // Weapon rotation uses the single bone at index 0
                            var rotation = Quaternion.CreateFromYawPitchRoll(
                                (360 * frame.Rotations[0].rY / 4096f) * (float)Math.PI / 180,
                                (360 * frame.Rotations[0].rX / 4096f) * (float)Math.PI / 180,
                                (360 * frame.Rotations[0].rZ / 4096f) * (float)Math.PI / 180
                            );
                            rots[time] = rotation;

                            c++;
                        }

                        if (trans.Count > 0)
                            mAnim.CreateTranslationChannel(targetNode, trans);
                        if (rots.Count > 0)
                            mAnim.CreateRotationChannel(targetNode, rots);
                    }

                    wi++;
                }
            }

            return model;
        }

        public SharpGLTF.Schema2.ModelRoot BuildSceneAuto(string name) {
            if (_source.Exists(name + ".a00"))
                return BuildSceneFromSummon(name);
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

        public SharpGLTF.Schema2.ModelRoot BuildSceneFromModel(string modelCode) {
            //TODO - very similar to code in main Braver project - move into Ficedula.FF7
            var texs = Enumerable.Range((int)'c', 10).Select(i => modelCode + "a" + ((char)i).ToString());
            int codecounter = 12;
            // Track which data files are consumed for the main skeleton (so we don't duplicate them)
            var usedDataFiles = new List<string>();
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
                usedDataFiles.Add(data);
                return data;
            };

            // Collect extra standalone PFiles (e.g. separate weapon/attachment models) that
            // exist in the same two-letter suffix space but were not consumed by the main
            // model's NextData iterator.
            var extraParts = new List<PFile>();
            // Only consider suffixes starting with 'c' (e.g. rtca..rtcz)
            char targetPrefix = 'c';
            for (int i = 0; i < 26; i++) {
                char c1 = targetPrefix;
                char c2 = (char)('a' + i);
                string candidate = modelCode + c1.ToString() + c2.ToString();

                if (usedDataFiles.Contains(candidate))
                    continue;

                using (var s = _source.TryOpen(candidate)) {
                    if (s == null)
                        continue;

                    try {
                        var p = new PFile(s);
                        extraParts.Add(p);
                    } catch {
                        // ignore non-PFile entries
                    }
                }
            }

            return BuildScene(modelCode + "aa", modelCode + "da", texs, NextData, extraParts);
        }
    }
}
