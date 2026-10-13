// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Field;
using Ficedula;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Numerics;

namespace Ficedula.FF7.Exporters {
    public class FieldModel : ModelBase {
        private DataSource _data;

        public FieldModel(DataSource data, ModelBaseOptions options) {
            _data = data;
            _options = options;
        }

        public SharpGLTF.Schema2.ModelRoot BuildScene(string modelHRC, IEnumerable<string> animFiles) {
            return BuildScene(modelHRC, animFiles, false, out _);
        }

        public SharpGLTF.Schema2.ModelRoot BuildScene(
            string modelHRC,
            IEnumerable<string> animFiles,
            bool compatibleAnimationsOnly,
            out IReadOnlyList<string> usedAnimations) {
            var scene = new SceneBuilder();
            var model = new Field.HRCModel(_data.Open, modelHRC);
            var animationFiles = animFiles.ToList();
            if (compatibleAnimationsOnly) {
                animationFiles = animationFiles
                    .Where(file => HasCompatibleSkeleton(file, model.Bones.Count))
                    .ToList();
            }

            var animations = animationFiles
                .Select(file => new { Anim = new Field.FieldAnim(_data.Open(file)), Name = Path.GetFileNameWithoutExtension(file) })
                .ToList();
            if (!compatibleAnimationsOnly && (animations.Count == 0 || animations.Any(anim => anim.Anim.Frames.Count == 0)))
                throw new InvalidDataException("At least one field animation with frames is required.");
            usedAnimations = animations.Select(anim => anim.Name).ToList();

            var materials = ConvertTextures(
                model.Bones
                .SelectMany(bone => bone.Polygons)
                .SelectMany(poly => poly.Textures)
            );
            MaterialBuilder defMaterial = GetUntexturedMaterial();

            var firstFrame = animations.FirstOrDefault()?.Anim.Frames.FirstOrDefault();
            var allNodes = new Dictionary<string, NodeBuilder>();
            var sourceNodes = new Dictionary<string, HRCModel.Bone>();

            void Descend(SceneBuilder scene, HRCModel.Bone bone, NodeBuilder node, Vector3 translation, Vector3? scale) {
                sourceNodes[bone.Name] = bone;
                Vector3 rotationDegrees = Vector3.Zero;
                if (firstFrame != null) {
                    if (bone.Index < 0) {
                        rotationDegrees = new Vector3(
                            firstFrame.Rotation.X - 180,
                            360 - firstFrame.Rotation.Y,
                            firstFrame.Rotation.Z
                        );
                        translation = new Vector3(
                            firstFrame.Translation.X,
                            firstFrame.Translation.Y,
                            -firstFrame.Translation.Z
                        );
                    } else if (bone.Index < firstFrame.Bones.Count) {
                        rotationDegrees = firstFrame.Bones[bone.Index];
                    }
                }
                var rotation = Quaternion.CreateFromYawPitchRoll(
                    rotationDegrees.Y * (float)Math.PI / 180,
                    rotationDegrees.X * (float)Math.PI / 180,
                    rotationDegrees.Z * (float)Math.PI / 180
                );

                node.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(
                    null, scale ?? Vector3.One, rotation, translation
                );

                foreach (var child in bone.Children) {
                    var c = node.CreateNode(child.Name);
                    allNodes[child.Name] = c;
                    Descend(scene, child, c, new Vector3(0, 0, -bone.Length), null);
                }
            }

            void DescendMesh(SceneBuilder scene, HRCModel.Bone bone, NodeBuilder node, Matrix4x4 transform, NodeBuilder[] joints) {
                foreach (var poly in bone.Polygons) {
                    foreach (var mesh in BuildMeshes(poly.PFile, poly.Textures.Select(tex => materials[tex]), defMaterial, bone.Index))
                        scene.AddSkinnedMesh(mesh, transform, joints);
                }
                foreach(var child in bone.Children) {
                    var childNode = allNodes[child.Name];
                    var childTransform = childNode.LocalMatrix * transform;
                    DescendMesh(scene, child, childNode, childTransform, joints);
                }
            }

            var rootContainer = new NodeBuilder("RootContainer");
            var root = rootContainer.CreateNode("root");
            Descend(scene, model.Root, root, Vector3.Zero, null);
            DescendMesh(
                scene, model.Root, root, root.LocalMatrix, 
                sourceNodes.Values
                .Where(bone => bone.Index >= 0)
                .OrderBy(bone => bone.Index)
                .Select(bone => allNodes[bone.Name])
                .ToArray()
            );
            scene.AddNode(rootContainer);

            FinishBaking();

            SceneBuilderSchema2Settings settings = SceneBuilderSchema2Settings.Default;
            settings.UseStridedBuffers = false;
            SharpGLTF.Schema2.ModelRoot output = scene.ToGltf2(settings);
            //Console.WriteLine($"Anim names ({animations.Count}): {string.Join(" ", animations.Select(x => x.Name).ToArray())}");
            //Console.WriteLine($"Nodes ({output.LogicalNodes.Count}): {string.Join(", ", output.LogicalNodes.Select(x => x.Name).ToArray())}");

            foreach (var anim in animations) {
                //Console.WriteLine($"Processing animation: {anim.Name}");

                SharpGLTF.Schema2.Animation mAnim = output.CreateAnimation();
                mAnim.Name = anim.Name;
                
                bool isNaNFound = false;

                foreach (var node in output.LogicalNodes) {
                    //if (node.Name == "null" || string.IsNullOrWhiteSpace(node.Name)) {
                    //    Console.WriteLine("Skipping: " + node.Name);
                    //    continue;
                    //}
                    int boneIndex;
                    if (sourceNodes.TryGetValue(node.Name ?? "", out var bone))
                        boneIndex = bone.Index;
                    else
                        continue;

                    var rots = new Dictionary<float, Quaternion>();
                    var trans = new Dictionary<float, Vector3>();

                    for (int frameIndex = 0; frameIndex < anim.Anim.Frames.Count; frameIndex++) {
                        var frame = anim.Anim.Frames[frameIndex];
                        float time = frameIndex / 30f;
                        Quaternion rotation;

                        if (boneIndex < 0) {
                            trans[time] = new Vector3(frame.Translation.X, frame.Translation.Y, -frame.Translation.Z);
                            rotation = Quaternion.CreateFromYawPitchRoll(
                                (360 - frame.Rotation.Y) * (float)Math.PI / 180,
                                (frame.Rotation.X - 180) * (float)Math.PI / 180,
                                frame.Rotation.Z * (float)Math.PI / 180
                            );
                        } else if (boneIndex < anim.Anim.BoneCount && boneIndex < frame.Bones.Count) {
                            rotation = Quaternion.CreateFromYawPitchRoll(
                                frame.Bones[boneIndex].Y * (float)Math.PI / 180,
                                frame.Bones[boneIndex].X * (float)Math.PI / 180,
                                frame.Bones[boneIndex].Z * (float)Math.PI / 180
                            );
                        } else {
                            rotation = Quaternion.Identity;
                        }
                        rots[time] = rotation;
                    }

                    if (boneIndex < 0)
                        mAnim.CreateTranslationChannel(node, trans);

                    foreach (var kvp in rots.ToList()) {
                        Quaternion q = kvp.Value;

                        if (float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z) || float.IsNaN(q.W)) {
                            if (!isNaNFound) {
                                isNaNFound = true;
                                Console.WriteLine($"{anim.Name} Error: rots contains NaN values.");
                            }
                            //Console.WriteLine($"NaN found in rots at time {kvp.Key}: ({q.X}, {q.Y}, {q.Z}, {q.W}) - Replacing with default.");
                            rots[kvp.Key] = Quaternion.Identity;
                        }
                    }

                    mAnim.CreateRotationChannel(node, rots);
                }
            }

            return output;
        }

        private bool HasCompatibleSkeleton(string animationFile, int modelBoneCount) {
            try {
                using var stream = _data.Open(animationFile);
                int version = stream.ReadI32();
                int frameCount = stream.ReadI32();
                int animationBoneCount = stream.ReadI32();
                return version == 1 && frameCount > 0 && animationBoneCount == modelBoneCount;
            } catch {
                return false;
            }
        }
    }
}
