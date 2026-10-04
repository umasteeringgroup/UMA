using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public sealed class TexturePaintProjectionRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PatchTriangle { public Vector4 a, b, c, uv; }
        private readonly Material material;
        private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        private readonly IReadOnlyList<TextureSet> stableGeometry;
        private TexturePaintProjectionGeometry cachedGeometry;
        private ComputeBuffer patchBuffer;
        private RenderTexture visibilityTarget;
        private Texture outlineSource;
        private Texture2D cachedOutline;

        // A gesture changes the projector, not the model. Cache its world-space geometry until
        // mouse-up; ordinary generators still rebuild it so moved/deformed models stay correct.
        public TexturePaintProjectionRenderer(Shader shader = null, IReadOnlyList<TextureSet> stableGeometry = null)
        {
            this.stableGeometry = stableGeometry;
            shader ??= Shader.Find("Hidden/UMA/TexturePaint/Projection");
            if (shader != null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public bool Generate(IReadOnlyList<TextureSet> geometrySets, IReadOnlyList<TextureSet> targets,
            IReadOnlyList<TexturePaintLayer> layers, TexturePaintProjectionSettings definition, out string error)
        {
            error = null;
            if (definition == null || targets == null || layers == null || targets.Count != layers.Count)
            { error = "Projection targets are incomplete."; return false; }
            if (material == null || !material.shader.isSupported)
            { error = "The Projection shader is unavailable."; return false; }
            TexturePaintProjectionSettings settings = definition.Clone(); settings.Normalize();
            bool garment = settings.garment?.enabled == true;
            bool hasSource = !garment && (settings.source == TexturePaintBrushSource.Overlay ? settings.overlay != null :
                settings.PreferredSource() != null);
            var garmentInputs = new List<TexturePaintGarmentSettings.Inputs>();
            try
            {
                var geometry = stableGeometry != null
                    ? cachedGeometry ??= new TexturePaintProjectionGeometry(stableGeometry)
                    : new TexturePaintProjectionGeometry(geometrySets);
                int component = geometry.ResolveComponent(settings);
                if (settings.placed && component == -2)
                { error = "The projection's surface region is missing. Place it on the model again."; return false; }
                PatchTriangle[] patch = BuildPatch(settings, out Bounds bounds);
                if (patchBuffer == null || patchBuffer.count != patch.Length)
                {
                    patchBuffer?.Dispose();
                    patchBuffer = new ComputeBuffer(patch.Length, Marshal.SizeOf<PatchTriangle>());
                }
                patchBuffer.SetData(patch);
                Texture visibility = settings.placed && settings.firstSurfaceOnly
                    ? BuildVisibility(settings, geometrySets, geometry, patch) : null;
                Texture2D curve = CurveTexture(settings.falloff);
                Texture coverage = garment ? Texture2D.whiteTexture : settings.source != TexturePaintBrushSource.Overlay &&
                    settings.PreferredSource()?.source == TexturePaintBrushSource.Color
                    ? ResolveChannelSource(settings.PreferredSource(), null) : settings.ResolveCoverage();
                Texture2D outline = null;
                if (settings.fade == TexturePaintProjectionFade.AlphaOutline && coverage != null)
                {
                    if (cachedOutline == null || outlineSource != coverage || stableGeometry == null)
                    {
                        if (cachedOutline != null) UnityEngine.Object.DestroyImmediate(cachedOutline);
                        cachedOutline = BuildOutlineDistance(coverage);
                        temporary.Remove(cachedOutline);
                        outlineSource = coverage;
                    }
                    outline = cachedOutline;
                }
                // Resolve and validate all members before replacing any generated channels.
                var allSources = new List<Dictionary<TexturePaintChannel, Texture>>();
                for (int i = 0; i < targets.Count; i++)
                {
                    TextureSet set = targets[i];
                    if (set?.surface?.mesh == null || layers[i] == null) { error = "A projection target has no mesh."; return false; }
                    var sources = new Dictionary<TexturePaintChannel, Texture>();
                    if (garment)
                    {
                        foreach (var channel in settings.garment.OutputChannels())
                            if (set.GetChannel(channel) != null) sources[channel] = Texture2D.whiteTexture;
                    }
                    else if (settings.source == TexturePaintBrushSource.Overlay && hasSource)
                    {
                        foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                            if (!TexturePaintChannelUtility.IsAuxiliary(channel) &&
                                set.TryResolveOverlaySource(settings.overlay, channel, settings.normalConvention, false,
                                    out Texture source, out _)) sources[channel] = source;
                    }
                    else if (settings.source != TexturePaintBrushSource.Overlay)
                    {
                        foreach (var item in settings.GetChannelSources())
                        {
                            if (set.GetChannel(item.channel) == null) continue;
                            Texture resolved = ResolveChannelSource(item, set);
                            if (item.HasSource && resolved == null)
                            { error = "The projection source cannot supply " + item.channel + " on '" + set.Name + "'."; return false; }
                            sources[item.channel] = resolved;
                        }
                    }
                    bool compatible = false;
                    foreach (Texture source in sources.Values) if (source != null) { compatible = true; break; }
                    if (hasSource && !compatible)
                    { error = "The projection source has no compatible channels on '" + set.Name + "'."; return false; }
                    allSources.Add(sources);
                    if (garment) garmentInputs.Add(new TexturePaintGarmentSettings.Inputs(settings.garment,set,geometrySets,layers[i]));
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    TextureSet set = targets[i]; TexturePaintLayer layer = layers[i];
                    Dictionary<TexturePaintChannel, Texture> sources = allSources[i];
                    foreach (var channel in new List<TexturePaintChannel>(layer.channels.Keys))
                        if (!sources.ContainsKey(channel))
                        { layer.channels[channel].Dispose(); layer.channels.Remove(channel); }
                    // Removing an authored map must also remove its options and retarget its effects.
                    foreach (var channel in new List<TexturePaintChannel>(layer.channelSettings.Keys))
                        if (!sources.ContainsKey(channel)) layer.channelSettings.Remove(channel);
                    TexturePaintChannel replacement = TexturePaintChannel.Albedo;
                    foreach (var channel in sources.Keys) { replacement = channel; break; }
                    if (layer.effects != null)
                        foreach (var effect in layer.effects.Stack)
                            if (effect != null && !sources.ContainsKey(effect.channel))
                            { if (sources.Count > 0) effect.channel = replacement; else effect.enabled = false; }
                    layer.kind = TexturePaintLayerKind.Projection; layer.projectionSettings = settings.Clone();
                    Mesh mesh = geometry.RestrictedMesh(set, component, out bool ownsMesh, settings, bounds);
                    if (ownsMesh) temporary.Add(mesh);
                    foreach (KeyValuePair<TexturePaintChannel, Texture> source in sources)
                    {
                        TextureChannelTarget baseChannel = set.GetChannel(source.Key);
                        if (!layer.channels.TryGetValue(source.Key, out var target) || target.Front == null ||
                            target.Width != baseChannel.Texture.width || target.Height != baseChannel.Texture.height ||
                            target.Front.format != baseChannel.format)
                        {
                            target?.Dispose();
                            target = new EditableTextureTarget(layer.name + " " + source.Key, baseChannel.Texture.width,
                                baseChannel.Texture.height, baseChannel.format, null, Color.clear);
                            layer.channels[source.Key] = target;
                        }
                        if (!garment) layer.GetChannelSettings(source.Key).sourceSettings = settings.GetChannelSourceSettings(source.Key);
                        else layer.GetChannelSettings(source.Key);
                        if (!settings.placed || source.Value == null) { target.Reset(null, Color.clear); continue; }
                        var properties = new MaterialPropertyBlock();
                        properties.SetInt("_GarmentEnabled",0);
                        if (garment) settings.garment.Bind(properties,source.Key,new Vector2(settings.width,settings.height),
                            set,geometrySets,layer,garmentInputs[i]);
                        properties.SetBuffer("_Patch", patchBuffer); properties.SetInt("_PatchCount", patch.Length);
                        properties.SetVector("_BoundsMin", bounds.min); properties.SetVector("_BoundsMax", bounds.max);
                        properties.SetTexture("_Source", source.Value);
                        properties.SetTexture("_Coverage", coverage != null ? coverage : Texture2D.whiteTexture);
                        properties.SetTexture("_Visibility", visibility != null ? visibility : Texture2D.whiteTexture);
                        properties.SetTexture("_Outline", outline != null ? outline : Texture2D.whiteTexture);
                        properties.SetTexture("_Curve", curve);
                        properties.SetFloat("_HalfDepth", settings.depth * 0.5f);
                        properties.SetFloat("_DepthMin", settings.BackDepth);
                        properties.SetFloat("_DepthMax", settings.FrontDepth);
                        properties.SetInt("_DepthFromSurface", settings.depthFromSurface ? 1 : 0);
                        properties.SetFloat("_DepthFade", settings.depthFade);
                        properties.SetFloat("_EdgeWidth", settings.edgeWidth);
                        properties.SetInt("_Fade", (int)settings.fade);
                        properties.SetInt("_CyclicU", settings.mode == TexturePaintProjectionMode.Cylindrical && settings.cylinderAngle >= 359.99f ? 1 : 0);
                        properties.SetInt("_FrontOnly", settings.frontFacesOnly ? 1 : 0);
                        properties.SetFloat("_AngleStart", Mathf.Cos(settings.angleStart * Mathf.Deg2Rad));
                        properties.SetFloat("_AngleEnd", Mathf.Cos(settings.angleEnd * Mathf.Deg2Rad));
                        properties.SetInt("_FirstSurface", settings.firstSurfaceOnly ? 1 : 0);
                        properties.SetFloat("_VisibilityTolerance", settings.visibilityTolerance);
                        properties.SetInt("_NormalChannel", source.Key == TexturePaintChannel.Normal ? 1 : 0);
                        properties.SetVector("_Flip", new Vector4(settings.flipX ? 1 : 0, settings.flipY ? 1 : 0, 0, 0));
                        using (var command = new CommandBuffer { name = "Generate Projection Layer" })
                        {
                            command.SetRenderTarget(target.Front); command.ClearRenderTarget(false, true, Color.clear);
                            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                                command.DrawMesh(mesh, TexturePaintProjectionGeometry.LocalToWorld(set), material, sub, 0, properties);
                            Graphics.ExecuteCommandBuffer(command);
                        }
                        for (int pass = 0; pass < 2; pass++)
                        { material.SetTexture("_MainTex", target.Front); Graphics.Blit(target.Front, target.Back, material, 1); target.Swap(); }
                        target.CopyFrontToBack();
                    }
                }
                return true;
            }
            catch (Exception exception) { error = "Projection generation failed: " + exception.Message; return false; }
            finally { foreach(var input in garmentInputs) input.Dispose(); ClearTemporary(); }
        }

        private Texture ResolveChannelSource(TexturePaintProjectionChannelSource source, TextureSet set)
        {
            if (source.source != TexturePaintBrushSource.Color) return source.Resolve(set);
            var texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            temporary.Add(texture);
            Color color = source.color;
            if (TexturePaintChannelUtility.IsGrayscale(source.channel))
            {
                float value = TexturePaintChannelUtility.ScalarValue(color);
                color = new Color(value, value, value, color.a);
            }
            texture.SetPixel(0, 0, color); texture.Apply(false, true);
            return texture;
        }

        private static PatchTriangle[] BuildPatch(TexturePaintProjectionSettings settings, out Bounds bounds)
        {
            int divisions = settings.PatchDivisions;
            var triangles = new PatchTriangle[divisions * divisions * 2]; int index = 0;
            bounds = new Bounds(settings.Evaluate(0, 0), Vector3.zero);
            float frontAllowance = 0f;
            for (int y = 0; y < divisions; y++) for (int x = 0; x < divisions; x++)
            {
                float u = (float)x / divisions, v = (float)y / divisions, step = 1f / divisions;
                Vector3 a = settings.Evaluate(u, v), b = settings.Evaluate(u + step, v);
                Vector3 c = settings.Evaluate(u + step, v + step), d = settings.Evaluate(u, v + step);
                bounds.Encapsulate(a); bounds.Encapsulate(b); bounds.Encapsulate(c); bounds.Encapsulate(d);
                var first = MakePatchTriangle(settings, a, b, c, new Vector2(u, v), new Vector2(u + step, v), new Vector2(u + step, v + step));
                first.uv = new Vector4(u, v, step, 0);
                var second = MakePatchTriangle(settings, a, c, d, new Vector2(u, v), new Vector2(u + step, v + step), new Vector2(u, v + step));
                second.uv = new Vector4(u, v, step, 1);
                triangles[index++] = first; triangles[index++] = second;
                frontAllowance = Mathf.Max(frontAllowance, first.a.w, second.a.w);
            }
            bounds.Expand(2f * Mathf.Max(-settings.BackDepth, settings.FrontDepth + frontAllowance) + 0.0001f); return triangles;
        }
        private static PatchTriangle MakePatchTriangle(TexturePaintProjectionSettings settings,
            Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var result = new PatchTriangle { a = a, b = b, c = c };
            if (!settings.depthFromSurface || settings.mode == TexturePaintProjectionMode.Planar) return result;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            // The smooth patch can bulge in front of its tessellated chord. Give only that
            // local curvature a small allowance, shared by visibility rays and GPU clipping.
            float deviation = 0f;
            foreach (Vector2 uv in new[] { (ua + ub) * .5f, (ub + uc) * .5f, (uc + ua) * .5f, (ua + ub + uc) / 3f })
                deviation = Mathf.Max(deviation, Vector3.Dot(settings.Evaluate(uv.x, uv.y) - a, normal));
            result.a.w = deviation * 2f;
            return result;
        }
        private Texture BuildVisibility(TexturePaintProjectionSettings settings, IReadOnlyList<TextureSet> geometrySets,
            TexturePaintProjectionGeometry geometry, PatchTriangle[] patch)
        {
            // Rasterize the closest signed depth in projector space. Max blending gives the
            // same nearest surface as the CPU rays, regardless of polygon order or shared UVs.
            // Each wrapped patch triangle has its own projection frame and clips to its UV cell.
            if (!SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RFloat))
                return BuildVisibilityCPU(settings, geometry, patch);
            int size = settings.visibilityResolution;
            if (visibilityTarget == null || visibilityTarget.width != size)
            {
                if (visibilityTarget != null) UnityEngine.Object.DestroyImmediate(visibilityTarget);
                visibilityTarget = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                {
                    name = "Projection Visibility", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                visibilityTarget.Create();
            }
            using var command = new CommandBuffer { name = "Projection Surface Visibility" };
            command.SetRenderTarget(visibilityTarget);
            command.ClearRenderTarget(false, true, new Color(-1e10f, 0, 0, 0));
            var properties = new MaterialPropertyBlock();
            properties.SetBuffer("_Patch", patchBuffer);
            properties.SetFloat("_DepthMin", settings.BackDepth);
            properties.SetFloat("_DepthMax", settings.FrontDepth);
            properties.SetInt("_FrontOnly", settings.frontFacesOnly ? 1 : 0);
            properties.SetFloat("_VisibilitySize", size);
            for (int triangle = 0; triangle < patch.Length; triangle++)
            {
                properties.SetInt("_VisibilityPatch", triangle);
                foreach (var set in geometrySets)
                {
                    Mesh mesh = set?.surface?.mesh;
                    if (mesh == null) continue;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        command.DrawMesh(mesh, TexturePaintProjectionGeometry.LocalToWorld(set), material, sub, 2, properties);
                }
            }
            Graphics.ExecuteCommandBuffer(command);
            return visibilityTarget;
        }

        private Texture2D BuildVisibilityCPU(TexturePaintProjectionSettings settings, TexturePaintProjectionGeometry geometry, PatchTriangle[] patch)
        {
            int size = settings.visibilityResolution;
            var values = new float[size * size];
            int divisions = settings.PatchDivisions;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * divisions, v = (y + 0.5f) / size * divisions;
                int cellX = Mathf.FloorToInt(u), cellY = Mathf.FloorToInt(v);
                float du = u - cellX, dv = v - cellY;
                PatchTriangle triangle = patch[(cellY * divisions + cellX) * 2 + (du >= dv ? 0 : 1)];
                Vector3 a = triangle.a, b = triangle.b, c = triangle.c;
                Vector3 point = du >= dv ? a * (1 - du) + b * (du - dv) + c * dv
                    : a * (1 - dv) + b * du + c * (dv - du);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                float front = settings.FrontDepth + triangle.a.w;
                bool hit = geometry.Raycast(new Ray(point + normal * front, -normal), front - settings.BackDepth, -1,
                    out Vector3 position, out _, settings.frontFacesOnly);
                values[y * size + x] = hit ? Vector3.Dot(position - point, normal) : 1e10f;
            }
            return FloatTexture("Projection Visibility", size, size, values, FilterMode.Point);
        }
        private Texture2D CurveTexture(AnimationCurve curve)
        {
            var values = new float[256]; for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp01(curve.Evaluate(i / 255f));
            return FloatTexture("Projection Falloff", 256, 1, values, FilterMode.Bilinear);
        }
        private Texture2D FloatTexture(string name, int width, int height, float[] values, FilterMode filter)
        {
            var texture = new Texture2D(width, height, TextureFormat.RFloat, false, true)
                { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = filter };
            texture.SetPixelData(values, 0); texture.Apply(false, false); temporary.Add(texture); return texture;
        }
        private Texture2D BuildOutlineDistance(Texture source)
        {
            const int size = 256;
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply();
                Color32[] pixels = read.GetPixels32(); var distance = new float[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    distance[y * size + x] = pixels[y * size + x].a < 128 ? 0f : Mathf.Min(x + 1, y + 1, size - x, size - y);
                // Two-pass chamfer distance follows the alpha silhouette, including interior holes.
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i - 1] + 1f);
                    if (y > 0) distance[i] = Mathf.Min(distance[i], distance[i - size] + 1f);
                    if (x > 0 && y > 0) distance[i] = Mathf.Min(distance[i], distance[i - size - 1] + 1.414214f);
                    if (x + 1 < size && y > 0) distance[i] = Mathf.Min(distance[i], distance[i - size + 1] + 1.414214f);
                }
                for (int y = size - 1; y >= 0; y--) for (int x = size - 1; x >= 0; x--)
                {
                    int i = y * size + x;
                    if (x + 1 < size) distance[i] = Mathf.Min(distance[i], distance[i + 1] + 1f);
                    if (y + 1 < size) distance[i] = Mathf.Min(distance[i], distance[i + size] + 1f);
                    if (x + 1 < size && y + 1 < size) distance[i] = Mathf.Min(distance[i], distance[i + size + 1] + 1.414214f);
                    if (x > 0 && y + 1 < size) distance[i] = Mathf.Min(distance[i], distance[i + size - 1] + 1.414214f);
                }
                for (int i = 0; i < distance.Length; i++) distance[i] /= size;
                return FloatTexture("Projection Alpha Outline", size, size, distance, FilterMode.Bilinear);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(read); }
        }
        private void ClearTemporary()
        { foreach (UnityEngine.Object resource in temporary) if (resource != null) UnityEngine.Object.DestroyImmediate(resource); temporary.Clear(); }
        public void Dispose()
        {
            ClearTemporary();
            patchBuffer?.Dispose(); patchBuffer = null;
            if (visibilityTarget != null) UnityEngine.Object.DestroyImmediate(visibilityTarget);
            if (cachedOutline != null) UnityEngine.Object.DestroyImmediate(cachedOutline);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            cachedGeometry = null;
        }
    }
}
