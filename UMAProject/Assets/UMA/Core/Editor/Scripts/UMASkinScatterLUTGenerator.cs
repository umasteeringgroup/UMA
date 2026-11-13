using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>Diagnostic views of the "UMA/URP/Realistic Skin SSS" shader. Names the values of its _SkinDebug property.</summary>
    public enum UMASkinSSSDebugView
    {
        Shaded = 0, Albedo = 1, Normals = 2, Scattering = 3, Smoothness = 4, Occlusion = 5,
        Curvature = 6, Transmission = 7, DiffuseLight = 8, Reflection = 9, DiffuseNormal = 10, Thickness = 11
    }

    /// <summary>
    /// Builds the lookup texture used by the "UMA/URP/Realistic Skin SSS" shader.
    /// Both channels use the normalized Burley diffusion profile, R(r) = (e^(-r/d) + e^(-r/3d)) / (8 pi d r),
    /// and are stored for one color channel: the shader reads the texture once per channel with that
    /// channel's scattering distance d, so the scattering color stays a free material parameter.
    /// R: diffuse lighting of a sphere after diffusion. u = N.L, v = sqrt(d / sphere radius).
    /// G: a shadow edge after diffusion. u = shadow attenuation, v = sqrt(d / penumbra width / MaxShadowRatio).
    /// B: the constant <see cref="Marker"/>. Width, Height and Marker are repeated in UMA3SkinSSSInput.hlsl.
    /// </summary>
    public static class UMASkinScatterLUTGenerator
    {
        public const string LUTPath = "Assets/UMA/SRP/Shaders/Skin/UMA3SkinScatterLUT.exr";
        public const int Width = 256;
        public const int Height = 64;
        /// <summary>Constant stored in the blue channel. The shader checks it to tell the texture from a missing one.</summary>
        public const float Marker = 0.25f;
        /// <summary>Largest scattering distance in the shadow channel, in penumbra widths. Matches the shader.</summary>
        public const double MaxShadowRatio = 0.5;
        // The filtered shadow edge is narrowed to this part of the penumbra before diffusion, so that
        // the diffused edge fits inside the penumbra the shadow map provides. Matches the shader's blend.
        const double PenumbraStart = 0.35, PenumbraWidth = 0.5;

        [MenuItem("UMA/Build/Rebuild Skin Scatter Lookup Texture", priority = 130)]
        private static void RebuildMenu()
        {
            var texture = Rebuild();
            Selection.activeObject = texture;
            EditorGUIUtility.PingObject(texture);
        }

        /// <summary>Recomputes the texture, writes it to <see cref="LUTPath"/> and applies its import settings.</summary>
        public static Texture2D Rebuild()
        {
            var pixels = Compute(Width, Height);
            var texture = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(LUTPath, texture.EncodeToEXR(Texture2D.EXRFlags.None));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(LUTPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(LUTPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 0;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(LUTPath);
        }

        /// <summary>Returns the lookup values, row 0 first. Row 0 and the first and last columns are exact end values.</summary>
        public static Color[] Compute(int width, int height)
        {
            if (width < 2 || height < 2) throw new ArgumentOutOfRangeException(nameof(width));
            var pixels = new Color[width * height];
            const int radialSamples = 2048;
            const int shadowRadial = 192, shadowAngular = 48;
            var radii = new double[radialSamples];
            var shadowRadii = new double[shadowRadial];
            for (int i = 0; i < shadowRadial; i++) shadowRadii[i] = InverseCdf((i + 0.5) / shadowRadial);
            var shadowCos = new double[shadowAngular];
            // Half a turn is enough: the other half mirrors it.
            for (int k = 0; k < shadowAngular; k++) shadowCos[k] = Math.Cos(Math.PI * (k + 0.5) / shadowAngular);

            for (int y = 0; y < height; y++)
            {
                double v = y / (double)(height - 1);

                // ---- R: diffusion on a sphere. Distances are in units of d, so the radius is 1 / v^2.
                double ratio = v * v;
                double radius = ratio > 0 ? 1.0 / ratio : double.PositiveInfinity;
                if (ratio > 0)
                {
                    // Importance-sample the chord length; the profile is cut off at the sphere's diameter.
                    double covered = Cdf(2 * radius);
                    for (int i = 0; i < radialSamples; i++) radii[i] = InverseCdf((i + 0.5) / radialSamples * covered);
                }
                for (int x = 0; x < width; x++)
                {
                    double cosTheta = 2.0 * x / (width - 1) - 1.0;
                    double diffuse;
                    if (ratio <= 0) diffuse = Math.Max(0, cosTheta);
                    else
                    {
                        double sinTheta = Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta));
                        double sum = 0;
                        for (int i = 0; i < radialSamples; i++)
                        {
                            double chord = radii[i] / radius;
                            double cosAlpha = 1 - 0.5 * chord * chord;
                            double sinAlpha = Math.Sqrt(Math.Max(0, 1 - cosAlpha * cosAlpha));
                            // Mean of max(0, n.L) over the ring of points at this chord length.
                            double a = cosTheta * cosAlpha, b = sinTheta * sinAlpha;
                            if (a >= b) sum += a;
                            else if (a > -b)
                            {
                                double phi = Math.Acos(-a / b);
                                sum += (a * phi + b * Math.Sin(phi)) / Math.PI;
                            }
                        }
                        diffuse = sum / radialSamples;
                    }
                    pixels[y * width + x].r = (float)diffuse;
                }

                // ---- G: a narrowed shadow edge, diffused in the plane.
                double distance = v * v * MaxShadowRatio;
                double atZero = ShadowAt(0, distance, shadowRadii, shadowCos);
                double atOne = ShadowAt(1, distance, shadowRadii, shadowCos);
                for (int x = 0; x < width; x++)
                {
                    double s = x / (double)(width - 1);
                    double value = (ShadowAt(s, distance, shadowRadii, shadowCos) - atZero) / Math.Max(1e-9, atOne - atZero);
                    pixels[y * width + x].g = (float)Math.Min(1, Math.Max(0, value));
                    pixels[y * width + x].b = Marker;
                    pixels[y * width + x].a = 1;
                }
            }
            return pixels;
        }

        static double ShadowAt(double s, double distance, double[] radii, double[] cosines)
        {
            if (distance <= 0) return Penumbra(s);
            double sum = 0;
            for (int i = 0; i < radii.Length; i++)
            {
                double r = radii[i] * distance;
                for (int k = 0; k < cosines.Length; k++) sum += Penumbra(s + r * cosines[k]);
            }
            return sum / (radii.Length * cosines.Length);
        }

        static double Penumbra(double s) => Math.Min(1, Math.Max(0, (s - PenumbraStart) / PenumbraWidth));

        /// <summary>Fraction of the profile's energy within radius r (d = 1).</summary>
        static double Cdf(double r) => 1 - 0.25 * Math.Exp(-r) - 0.75 * Math.Exp(-r / 3);

        static double InverseCdf(double u)
        {
            double low = 0, high = 64;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (low + high);
                if (Cdf(mid) < u) low = mid; else high = mid;
            }
            return 0.5 * (low + high);
        }
    }
}
