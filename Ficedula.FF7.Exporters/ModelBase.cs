// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SkiaSharp;
using SharpGLTF.Schema2;

namespace Ficedula.FF7.Exporters {

    public class ModelBaseOptions {
        public bool ConvertSRGBToLinear { get; set; }
        public bool SwapWinding { get; set; }
        public bool BakeVertexColours { get; set; }
    }

    public class ModelBase {

        private const int BakedTextureSize = 1024;
        private const int BakedTileSize = 8;

        protected ModelBaseOptions _options;

        private Dictionary<(uint c0, uint c1, uint c2), (int x, int y)> _bakedColours = new();
        private SKBitmap _bakedTexture;
        private MaterialBuilder _bakedMaterial;

        private static double SRGBToLinear(double value) {
            const double a = 0.055;
            if (value <= 0.04045)
                return value / 12.92;
            else
                return Math.Pow((value + a) / (1 + a), 2.4);
        }

        private static double LinearToSRGB(double value) {
            const double a = 0.055;
            value = Math.Clamp(value, 0, 1);
            if (value <= 0.0031308)
                return value * 12.92;
            return (1 + a) * Math.Pow(value, 1 / 2.4) - a;
        }

        protected SKColor ColourToSK(uint colour) {
            return ColourToSK(GetBakedLinearColour(colour));
        }

        private static SKColor ColourToSK(Vector3 linear) {
            return new SKColor(
                ToTextureChannel(linear.X),
                ToTextureChannel(linear.Y),
                ToTextureChannel(linear.Z),
                255
            );
        }

        private Vector3 GetBakedLinearColour(uint colour) {
            var c = new Vector3(
                (colour & 0xff) / 255f,
                ((colour >> 8) & 0xff) / 255f,
                ((colour >> 16) & 0xff) / 255f
            );
            if (_options.ConvertSRGBToLinear) {
                c = new Vector3(
                    (float)SRGBToLinear(c.X),
                    (float)SRGBToLinear(c.Y),
                    (float)SRGBToLinear(c.Z)
                );
            }
            return c;
        }

        private static byte ToTextureChannel(float linearChannel) {
            double srgbChannel = LinearToSRGB(linearChannel);
            return (byte)Math.Clamp((int)Math.Round(srgbChannel * 255), 0, 255);
        }

        private (Vector2 tc0, Vector2 tc1, Vector2 tc2) GetBakedCoords(uint c0, uint c1, uint c2) {
            var sorted = new[] { c0, c1, c2 }.OrderBy(x => x).ToList();
            int x, y;
            if (_bakedColours.TryGetValue((sorted[0], sorted[1], sorted[2]), out var coord)) {
                (x, y) = coord;
            } else {
                int tilesPerRow = BakedTextureSize / BakedTileSize;
                if (_bakedColours.Count >= tilesPerRow * tilesPerRow)
                    throw new InvalidOperationException("The baked vertex-colour atlas is full.");

                x = BakedTileSize * (_bakedColours.Count % tilesPerRow);
                y = BakedTileSize * (_bakedColours.Count / tilesPerRow);

                Vector3 c0Linear = GetBakedLinearColour(sorted[0]);
                Vector3 c1Linear = GetBakedLinearColour(sorted[1]);
                Vector3 c2Linear = GetBakedLinearColour(sorted[2]);

                // The UVs map this tile to a right triangle. Store the affine colour
                // plane across the tile so bilinear filtering reproduces barycentric
                // vertex-colour interpolation. Values outside the triangle are only
                // a filter gutter; clamp them to the texture's representable range.
                for (int py = 0; py < BakedTileSize; py++) {
                    float v = py / (float)(BakedTileSize - 1);
                    for (int px = 0; px < BakedTileSize; px++) {
                        float u = px / (float)(BakedTileSize - 1);
                        Vector3 colour = c0Linear
                            + (c1Linear - c0Linear) * u
                            + (c2Linear - c0Linear) * v;
                        colour = Vector3.Clamp(colour, Vector3.Zero, Vector3.One);
                        _bakedTexture.SetPixel(x + px, y + py, ColourToSK(colour));
                    }
                }

                _bakedColours[(sorted[0], sorted[1], sorted[2])] = (x, y);
            }

            Vector2[] coords = new[] {
                new Vector2((x + 0.5f) / BakedTextureSize, (y + 0.5f) / BakedTextureSize),
                new Vector2((x + BakedTileSize - 0.5f) / BakedTextureSize, (y + 0.5f) / BakedTextureSize),
                new Vector2((x + 0.5f) / BakedTextureSize, (y + BakedTileSize - 0.5f) / BakedTextureSize),
            };

            return (
                coords[sorted.IndexOf(c0)],
                coords[sorted.IndexOf(c1)],
                coords[sorted.IndexOf(c2)]
            );
        }

        protected Vector4 UnpackColour(uint colour) {
            var c = new Vector4(
                (colour & 0xff) / 255f,
                ((colour >> 8) & 0xff) / 255f,
                ((colour >> 16) & 0xff) / 255f,
                ((colour >> 24) & 0xff) / 255f
            );
            if (_options.ConvertSRGBToLinear) {
                c = new Vector4(
                    (float)SRGBToLinear(c.X),
                    (float)SRGBToLinear(c.Y),
                    (float)SRGBToLinear(c.Z),
                    (float)SRGBToLinear(c.W)
                );
            }
            return c;
        }

        private static (Vector3 n0, Vector3 n1, Vector3 n2) GetTriangleNormals(
            PFileVert v0, PFileVert v1, PFileVert v2) {
            Vector3 faceNormal = Vector3.Cross(
                v1.Position - v0.Position,
                v2.Position - v0.Position
            );
            faceNormal = faceNormal.LengthSquared() > 0
                ? Vector3.Normalize(faceNormal)
                : Vector3.UnitY;

            // Battle P files often have no normals. A valid vertex at the origin
            // would then have a default position/normal pair, which MeshBuilder
            // treats as an empty vertex and drops its triangles.
            return (
                v0.Normal == Vector3.Zero ? faceNormal : v0.Normal,
                v1.Normal == Vector3.Zero ? faceNormal : v1.Normal,
                v2.Normal == Vector3.Zero ? faceNormal : v2.Normal
            );
        }

        private void SetupBaking() {
            if (_options.BakeVertexColours) {
                _bakedTexture ??= new SKBitmap(
                    BakedTextureSize,
                    BakedTextureSize,
                    SKColorType.Rgba8888,
                    SKAlphaType.Opaque
                );
                _bakedMaterial ??= new MaterialBuilder("BakedVertexColours")
                    .WithDoubleSide(false)
                    .WithAlpha(SharpGLTF.Materials.AlphaMode.OPAQUE)
                    .WithUnlitShader();
            }
        }

        protected void FinishBaking() {
            if (_options.BakeVertexColours) {
                byte[] data = _bakedTexture
                    .Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                    .ToArray();
                _bakedMaterial.WithChannelImage(KnownChannel.BaseColor, new SharpGLTF.Memory.MemoryImage(data));
                _bakedMaterial.GetChannel(KnownChannel.BaseColor).Texture.WithSampler(
                    TextureWrapMode.CLAMP_TO_EDGE,
                    TextureWrapMode.CLAMP_TO_EDGE,
                    TextureMipMapFilter.LINEAR,
                    TextureInterpolationFilter.LINEAR
                );
            }
        }

        protected IEnumerable<IMeshBuilder<MaterialBuilder>> BuildMeshes(PFile poly, 
            IEnumerable<MaterialBuilder> materials, MaterialBuilder defMaterial, int boneIndex) {

            SetupBaking();

            foreach (var group in poly.Chunks) {
                if ((group.Texture != null) && (group.Texture.Value < materials.Count())) {
                    var mesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>();
                    for (int i = 0; i < group.Indices.Count; i += 3) {
                        var v0 = group.Verts[group.Indices[i]];
                        var v1 = group.Verts[group.Indices[i + (_options.SwapWinding ? 2 : 1)]];
                        var v2 = group.Verts[group.Indices[i + (_options.SwapWinding ? 1 : 2)]];
                        (var n0, var n1, var n2) = GetTriangleNormals(v0, v1, v2);

                        var vb0 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(
                            new VertexPositionNormal(v0.Position, n0),
                            new VertexColor1Texture1(UnpackColour(v0.Colour), v0.TexCoord),
                            new VertexJoints4(boneIndex)
                        );
                        var vb1 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(
                            new VertexPositionNormal(v1.Position, n1),
                            new VertexColor1Texture1(UnpackColour(v1.Colour), v1.TexCoord),
                            new VertexJoints4(boneIndex)
                        );
                        var vb2 = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(
                            new VertexPositionNormal(v2.Position, n2),
                            new VertexColor1Texture1(UnpackColour(v2.Colour), v2.TexCoord),
                            new VertexJoints4(boneIndex)
                        );

                        mesh.UsePrimitive(materials.ElementAt(group.Texture.Value))
                            .AddTriangle(vb0, vb1, vb2);
                    }
                    yield return mesh;
                } else if (_options.BakeVertexColours) {
                    var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>();
                    for (int i = 0; i < group.Indices.Count; i += 3) {
                        var v0 = group.Verts[group.Indices[i]];
                        var v1 = group.Verts[group.Indices[i + (_options.SwapWinding ? 2 : 1)]];
                        var v2 = group.Verts[group.Indices[i + (_options.SwapWinding ? 1 : 2)]];
                        (var n0, var n1, var n2) = GetTriangleNormals(v0, v1, v2);

                        (var tc0, var tc1, var tc2) = GetBakedCoords(v0.Colour, v1.Colour, v2.Colour);

                        var vb0 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>(
                            new VertexPositionNormal(v0.Position, n0),
                            new VertexTexture1(tc0),
                            new VertexJoints4(boneIndex)
                        );
                        var vb1 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>(
                            new VertexPositionNormal(v1.Position, n1),
                            new VertexTexture1(tc1),
                            new VertexJoints4(boneIndex)
                        );
                        var vb2 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>(
                            new VertexPositionNormal(v2.Position, n2),
                            new VertexTexture1(tc2),
                            new VertexJoints4(boneIndex)
                        );

                        mesh.UsePrimitive(_bakedMaterial)
                            .AddTriangle(vb0, vb1, vb2);
                    }
                    yield return mesh;
                } else {
                    var mesh = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexJoints4>();
                    for (int i = 0; i < group.Indices.Count; i += 3) {
                        var v0 = group.Verts[group.Indices[i]];
                        var v1 = group.Verts[group.Indices[i + (_options.SwapWinding ? 2 : 1)]];
                        var v2 = group.Verts[group.Indices[i + (_options.SwapWinding ? 1 : 2)]];
                        (var n0, var n1, var n2) = GetTriangleNormals(v0, v1, v2);

                        var vb0 = new VertexBuilder<VertexPositionNormal, VertexColor1, VertexJoints4>(
                            new VertexPositionNormal(v0.Position, n0),
                            new VertexColor1(UnpackColour(v0.Colour)),
                            new VertexJoints4(boneIndex)
                        );
                        var vb1 = new VertexBuilder<VertexPositionNormal, VertexColor1, VertexJoints4>(
                            new VertexPositionNormal(v1.Position, n1),
                            new VertexColor1(UnpackColour(v1.Colour)),
                            new VertexJoints4(boneIndex)
                        );
                        var vb2 = new VertexBuilder<VertexPositionNormal, VertexColor1, VertexJoints4>(
                            new VertexPositionNormal(v2.Position, n2),
                            new VertexColor1(UnpackColour(v2.Colour)),
                            new VertexJoints4(boneIndex)
                        );

                        mesh.UsePrimitive(defMaterial)
                            .AddTriangle(vb0, vb1, vb2);
                    }
                    yield return mesh;
                }
            }
        }

        protected Dictionary<TexFile, MaterialBuilder> ConvertTextures(IEnumerable<TexFile> texs) {
            Dictionary<TexFile, MaterialBuilder> materials = new();
            foreach (var texture in texs) {
                byte[] data = texture
                    .ToBitmap(0)
                    .Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                    .ToArray();
                var mat = new MaterialBuilder("Mat" + materials.Count)
                    .WithDoubleSide(false)
                    .WithUnlitShader()
                    .WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND)
                    .WithChannelImage(KnownChannel.BaseColor, new SharpGLTF.Memory.MemoryImage(data));
                //.WithChannelImage(KnownChannel.Diffuse, new SharpGLTF.Memory.MemoryImage(tex));
                materials[texture] = mat;
            }
            return materials;
        }

        protected MaterialBuilder GetUntexturedMaterial() {
            return new MaterialBuilder("Def")
                .WithDoubleSide(false)
                .WithAlpha(SharpGLTF.Materials.AlphaMode.OPAQUE)
                .WithBaseColor(Vector4.One)
                //.WithBaseColor(new System.Numerics.Vector4(1, 1, 1, 0.5f))
                .WithUnlitShader();
        }
    }
}
