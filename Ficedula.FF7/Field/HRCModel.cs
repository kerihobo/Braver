// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Ficedula.FF7.Field {
    public class HRCModel {

        public List<Bone> Bones { get; } = new();
        public Bone Root { get; }
        public string Name { get; }

        public class BonePolygon {
            public PFile PFile { get; }
            public List<TexFile> Textures { get; }

            public BonePolygon(PFile pFile, List<TexFile> textures) {
                PFile = pFile;
                Textures = textures;
            }
        }

        public class Bone {
            public List<Bone> Children { get; } = new();
            public float Length { get; }
            public List<BonePolygon> Polygons { get; } = new();
            public int Index { get; }
            public string Name { get; }

            public Bone(float length, int index, string name) {
                Length = length;
                Index = index;
                Name = name;
            }
        }


        public HRCModel(Func<string, Stream> dataProvider, string hrcFile) {
            using (var hrc = dataProvider(hrcFile)) {
                string[] lines = hrc.ReadAllLines().ToArray();
                Name = lines[1].Split(null).Last();
                int numBones = int.Parse(lines[2].Split(null).Last());
                Dictionary<string, Bone> bones = new Dictionary<string, Bone>(StringComparer.InvariantCultureIgnoreCase);
                
                if (numBones == 0) //special case, bah
                    numBones++;

                Root = new Bone(0, -1, "root");
                bones.Add("root", Root);

                bool isNUp = false;
                int n = 0;
                lines = lines.Where(s => !s.StartsWith("#")).ToArray();


                //Console.WriteLine(string.Join("\n", lines));
                foreach (int b in Enumerable.Range(0, numBones)) {
                    //float length = 0;
                    //float.TryParse(lines[6 + 5 * b], out length);
                    float length = float.Parse(lines[6 + n + (5 * b)]);

                    string boneName = lines[4 + n + (5 * b)];
                    Bone bone = new Bone(length, b, boneName);

                    string targetLine = lines[7 + n + (5 * b)];
                    //Console.WriteLine("Here we go! " + targetLine);
                    if (targetLine.Length > 0 && targetLine[0] == '#') {
                        targetLine = lines[8 + (5 * b)];
                        isNUp = true;
                    }

                    foreach (string rsd in targetLine.Split(null).Skip(1)) {
                        if (string.IsNullOrWhiteSpace(rsd)) continue;

                        //Console.WriteLine("RSD: " + rsd);
                        string[] rsdLines = dataProvider(rsd + ".RSD").ReadAllLines().ToArray();
                        string pFile = rsdLines
                            .First(s => s.StartsWith("PLY=", StringComparison.InvariantCultureIgnoreCase))
                            .Substring(4)
                            .Replace(".PLY", ".P");
                        int numTex = int.Parse(rsdLines
                            .First(s => s.StartsWith("NTEX="))
                            .Substring(5)
                            );
                        BonePolygon bp = new BonePolygon(
                            new PFile(dataProvider(pFile)),
                            Enumerable.Range(0, numTex)
                                .Select(n => rsdLines.First(s => s.StartsWith($"TEX[{n}]=")).Substring(7).Replace(".TIM", ".TEX"))
                                .Select(t => new TexFile(dataProvider(t)))
                                .ToList()
                        );
                        bone.Polygons.Add(bp);
                    }
                    bones.Add(lines[4 + n + (5 * b)], bone);
                    
                    string key = lines[5 + n + (5 * b)];
                    //Console.WriteLine("bone: " + key);
                    if (bones.ContainsKey(key)) {
                        bones[key].Children.Add(bone);
                    }

                    if (isNUp) {
                        //n++;
                    }

                    Bones.Add(bone);
                }
            }
        }

        public HRCModel(LGPFile lgp, string hrcFile) : this(lgp.Open, hrcFile) { }

    }

    public class FieldAnim {

        public int BoneCount { get; private set; }
        public List<Frame> Frames { get; private set; }

        public class Frame {
            public Vector3 Translation { get; }
            public Vector3 Rotation { get; }
            public List<Vector3> Bones { get; }

            internal Frame(Stream s, int boneCount) {
                Rotation = new Vector3(s.ReadF32(), s.ReadF32(), s.ReadF32());
                Translation = new Vector3(s.ReadF32(), s.ReadF32(), s.ReadF32());
                Bones = Enumerable.Range(0, boneCount)
                    .Select(_ => new Vector3(s.ReadF32(), s.ReadF32(), s.ReadF32()))
                    .ToList();
            }
        }

        public FieldAnim(Stream s) {
            Trace.Assert(s.ReadI32() == 1);
            int frameCount = s.ReadI32();
            BoneCount = s.ReadI32();
            int rotationOrder = s.ReadI32();
            Trace.Assert(rotationOrder == 0x020001);
            s.Seek(20, SeekOrigin.Current);
            Frames = Enumerable.Range(0, frameCount)
                .Select(_ => new Frame(s, BoneCount))
                .ToList();
        }
    }
}
