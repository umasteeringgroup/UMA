using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintVDMCoverage { Footprint, SourceAlpha, MaskRed }

    [CreateAssetMenu(menuName = "UMA/Overlay Painter/VDM Brush", fileName = "VDM Brush")]
    public sealed class TexturePaintVDMBrush : ScriptableObject
    {
        [Tooltip("Signed linear EXR data. Import with the Overlay Painter VDM import command.")]
        public Texture2D vectorTexture;
        public Texture2D coverageMask;
        public TexturePaintVDMCoverage coverage;
        [Tooltip("Blender sculpt brush RGB maps to local U,V,N. Other exporters may require custom axes.")]
        public Vector3 uAxis = Vector3.right, vAxis = Vector3.up, nAxis = Vector3.forward;
        public Vector3 neutral;
        public Vector3 componentScale = Vector3.one;
        public bool flipImageY;
        public string sourceGuid;
        public string sourceRevision;
        public int decoderVersion = 1;
        public string tags;

        public TexturePaintVDMApplicationSettings CreateApplication() => new TexturePaintVDMApplicationSettings
        {
            enabled = true, brush = this, vectorTexture = vectorTexture, coverageMask = coverageMask,
            coverage = coverage, uAxis = uAxis, vAxis = vAxis, nAxis = nAxis,
            neutral = neutral, componentScale = componentScale, flipImageY = flipImageY,
            sourceGuid = sourceGuid, sourceRevision = TexturePaintVDMSource.Revision(vectorTexture),
            decoderVersion = decoderVersion
        };
    }

    // This is authoritative authoring data, shared by Projection and Path channel sources.
    // Keep full vector decoding and asset references even though phase 1 emits only height.
    [Serializable]
    public sealed class TexturePaintVDMApplicationSettings
    {
        public bool enabled;
        public int decoderVersion = 1;
        public TexturePaintVDMBrush brush;
        public Texture2D vectorTexture, coverageMask;
        public TexturePaintVDMCoverage coverage;
        public Vector3 uAxis = Vector3.right, vAxis = Vector3.up, nAxis = Vector3.forward;
        public Vector3 neutral, componentScale = Vector3.one;
        public bool flipImageY;
        public string sourceGuid, sourceRevision;
        public bool IsUnassigned => brush == null && vectorTexture == null &&
            string.IsNullOrEmpty(sourceGuid) && string.IsNullOrEmpty(sourceRevision);
        public float amplitude = 1f;
        public float reliefEncodingScale = 1f;
        // Geometry composition and output will be versioned separately from texture blending.
        public TexturePaintVDMApplicationSettings Clone() => (TexturePaintVDMApplicationSettings)MemberwiseClone();
        public Vector3 Decode(Color raw)
        {
            Vector3 v = Vector3.Scale(new Vector3(raw.r, raw.g, raw.b) - neutral, componentScale);
            return new Vector3(Vector3.Dot(v, uAxis), Vector3.Dot(v, vAxis), Vector3.Dot(v, nAxis));
        }
        public float EncodeHeight(Color raw) => .5f + Decode(raw).z * amplitude * reliefEncodingScale;
        public Vector3 Displacement(Color raw, Vector3 u, Vector3 v, Vector3 n)
        {
            Vector3 d = Decode(raw) * amplitude;
            return d.x * u + d.y * v + d.z * n;
        }
    }

    public static class TexturePaintVDMSource
    {
        private sealed class Entry { public string key; public Texture2D texture; }
        private static readonly List<Entry> cache = new List<Entry>();
        private static Material material;
        public static string LastError { get; private set; }
        public static string Revision(Texture2D source) => source != null ? source.imageContentsHash.ToString() : "";
        public static bool IsEnabled(TexturePaintChannelSourceSettings source) =>
            source?.source == TexturePaintBrushSource.Texture && source.vdm?.enabled == true;
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public static bool Validate(TexturePaintVDMApplicationSettings settings, out string error)
        {
            error = null;
            if (settings == null || !settings.enabled) { error = "No VDM source is enabled."; return false; }
            if (settings.decoderVersion != 1) { error = "Unsupported VDM decoder version. Update the brush profile explicitly."; return false; }
            Texture2D source = settings.vectorTexture;
            if (source == null) { error = "The VDM vector texture is missing. Assign or reimport its brush."; return false; }
            if (source.isDataSRGB || (source.format != TextureFormat.RGBAHalf && source.format != TextureFormat.RGBAFloat))
            { error = "VDM requires linear signed RGBAHalf/RGBAFloat data. Use Assets > Create > UMA > Overlay Painter > Import VDM EXR."; return false; }
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            { error = "VDM relief requires floating-point render textures on this graphics device."; return false; }
            if (!Finite(settings.neutral) || !Finite(settings.componentScale) || !Finite(settings.uAxis) ||
                !Finite(settings.vAxis) || !Finite(settings.nAxis) || !float.IsFinite(settings.amplitude) ||
                !float.IsFinite(settings.reliefEncodingScale))
            { error = "VDM decode settings must be finite."; return false; }
            if (settings.coverage == TexturePaintVDMCoverage.MaskRed && settings.coverageMask == null)
            { error = "Assign the VDM coverage mask or choose Footprint coverage."; return false; }
            if (settings.coverage == TexturePaintVDMCoverage.MaskRed && settings.coverageMask.isDataSRGB)
            { error = "VDM coverage masks must be linear data (disable sRGB on the mask)."; return false; }
            if (!string.IsNullOrEmpty(settings.sourceRevision) && settings.sourceRevision != Revision(source))
            { error = "The VDM source changed. Use Refresh Source Snapshot to accept the new revision."; return false; }
            return true;
        }

        public static bool FloatingTarget(RenderTextureFormat format) =>
            format == RenderTextureFormat.ARGBHalf || format == RenderTextureFormat.ARGBFloat;

        public static Texture2D Resolve(TexturePaintVDMApplicationSettings settings)
        {
            LastError = null;
            if (!Validate(settings, out string validationError)) { LastError = validationError; return null; }
            string key = JsonUtility.ToJson(settings) + Revision(settings.vectorTexture) + Revision(settings.coverageMask);
            foreach (Entry entry in cache) if (entry.key == key && entry.texture != null) return entry.texture;
            Shader shader = Shader.Find("Hidden/UMA/TexturePaint/VDMHeight");
            if (shader == null || !shader.isSupported) { LastError = "The VDM height shader is unavailable."; return null; }
            if (material == null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var source = settings.vectorTexture;
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                material.SetVector("_Neutral", settings.neutral);
                material.SetVector("_ComponentScale", settings.componentScale);
                material.SetVector("_NormalAxis", settings.nAxis);
                material.SetFloat("_HeightScale", settings.amplitude * settings.reliefEncodingScale);
                material.SetInt("_FlipY", settings.flipImageY ? 1 : 0);
                material.SetInt("_CoverageMode", (int)settings.coverage);
                material.SetTexture("_Mask", settings.coverageMask != null ? settings.coverageMask : Texture2D.whiteTexture);
                Graphics.Blit(source, target, material);
#if UNITY_EDITOR
                if (UnityEditor.ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException("The VDM height shader failed to compile. See the Unity Console.");
#endif
                RenderTexture.active = target;
                result = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true)
                { name = source.name + " (VDM Relief)", hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply(false, false); // The existing CPU stroke fallback needs readable pixels.
                // Reject invalid EXR samples once per cache build, never once per path dab.
                foreach (Color pixel in result.GetRawTextureData<Color>())
                    if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.a))
                        throw new InvalidOperationException("VDM contains non-finite height or coverage values.");
                cache.Add(new Entry { key = key, texture = result });
#if UNITY_EDITOR
                // VDM generation is synchronous. Retire old previews on the next editor tick,
                // after all projection members/path stamps have finished using this batch.
                // This bounds slider-driven cache growth without destroying in-flight sources.
                long bytes = 0;
                foreach (Entry entry in cache) bytes += (long)entry.texture.width * entry.texture.height * 32;
                while (cache.Count > 1 && (cache.Count > 8 || bytes > 128L * 1024 * 1024))
                {
                    var retired = cache[0].texture; cache.RemoveAt(0);
                    bytes -= (long)retired.width * retired.height * 32;
                    UnityEditor.EditorApplication.delayCall += () =>
                    { if (retired != null) UnityEngine.Object.DestroyImmediate(retired); };
                }
#endif
                return result;
            }
            catch (Exception error)
            {
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                LastError = error.Message; return null;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        public static void ClearCache()
        {
            foreach (Entry entry in cache) if (entry.texture != null) UnityEngine.Object.DestroyImmediate(entry.texture);
            cache.Clear();
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            material = null;
        }
    }
}
