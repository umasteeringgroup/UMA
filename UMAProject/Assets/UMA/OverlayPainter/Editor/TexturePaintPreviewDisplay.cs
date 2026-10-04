using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal enum TexturePaintPreviewView { Channel, LitSurface, Before, After, Split }

    // Owns only temporary display pixels; changing the view never edits authoring settings.
    internal sealed class TexturePaintPreviewDisplay : IDisposable
    {
        internal const int Resolution = 128;
        private Dictionary<TexturePaintChannel, Color[]> output, before;
        private TexturePaintChannel channel = TexturePaintChannel.Albedo;
        private TexturePaintPreviewView view;
        private bool mask, compare;
        internal Texture2D Texture { get; private set; }
        internal TexturePaintPreviewView View => view;

        internal void Set(Dictionary<TexturePaintChannel, Color[]> output,
            Dictionary<TexturePaintChannel, Color[]> before = null, bool mask = false, bool compare = false)
        {
            this.output = output; this.before = before; this.mask = mask; this.compare = compare;
            if (output != null && !output.ContainsKey(channel))
                foreach (var available in output.Keys) { channel = available; break; }
            if (mask && view == TexturePaintPreviewView.LitSurface) view = TexturePaintPreviewView.Channel;
            if (!compare && view >= TexturePaintPreviewView.Before) view = TexturePaintPreviewView.Channel;
            Rebuild();
        }

        internal void SelectView(TexturePaintPreviewView value) { view = value; Rebuild(); }

        internal bool Draw(string status, string help, Action regenerateLayer = null, bool canRegenerate = true)
        {
            bool changed = GUI.changed;
            bool refresh = false;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect rect = GUILayoutUtility.GetRect(Resolution, Resolution,
                        GUILayout.Width(Resolution), GUILayout.Height(Resolution));
                    if (Texture != null) EditorGUI.DrawTextureTransparent(rect, Texture, ScaleMode.ScaleToFit);
                    else EditorGUI.DrawRect(rect, new Color(.15f, .15f, .15f));
                    if (view == TexturePaintPreviewView.Split && Texture != null)
                    {
                        EditorGUI.DrawRect(new Rect(rect.center.x - .5f, rect.y, 1, rect.height), Color.white);
                        GUI.Label(new Rect(rect.x + 2, rect.y + 2, 60, 18), "Before", EditorStyles.whiteMiniLabel);
                        GUI.Label(new Rect(rect.center.x + 2, rect.y + 2, 60, 18), "After", EditorStyles.whiteMiniLabel);
                    }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                        var modes = new List<TexturePaintPreviewView> { TexturePaintPreviewView.Channel };
                        var labels = new List<string> { "Channel" };
                        if (!mask) { modes.Add(TexturePaintPreviewView.LitSurface); labels.Add("Lit Surface"); }
                        if (compare)
                        {
                            modes.AddRange(new[] { TexturePaintPreviewView.Before, TexturePaintPreviewView.After, TexturePaintPreviewView.Split });
                            labels.AddRange(new[] { "Before", "After", "Before / After Split" });
                        }
                        int current = Mathf.Max(0, modes.IndexOf(view));
                        int next = EditorGUILayout.Popup(current, labels.ToArray());
                        if (next != current) SelectView(modes[next]);
                        if (view != TexturePaintPreviewView.LitSurface && output != null && output.Count > 1)
                        {
                            var channels = new List<TexturePaintChannel>(output.Keys);
                            string[] names = channels.ConvertAll(value => value == TexturePaintChannel.NormalControl
                                ? "Height / Normal Control" : ObjectNames.NicifyVariableName(value.ToString())).ToArray();
                            current = channels.IndexOf(channel);
                            next = EditorGUILayout.Popup(current, names);
                            if (next != current) { channel = channels[next]; Rebuild(); }
                        }
                        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
                        refresh = GUILayout.Button("Refresh Preview", EditorStyles.miniButton);
                        if (regenerateLayer != null)
                            using (new EditorGUI.DisabledScope(!canRegenerate))
                                if (GUILayout.Button("Regenerate Layer", EditorStyles.miniButton))
                                    regenerateLayer();
                    }
                }
            }
            finally { GUI.changed = changed; }
            return refresh;
        }

        private void Rebuild()
        {
            if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
            if (output == null || output.Count == 0) return;
            Color[] colors = view == TexturePaintPreviewView.LitSurface ? Shade(output, before) :
                ComposeView(output, before, channel, view);
            if (colors == null) return;
            bool color = view == TexturePaintPreviewView.LitSurface || (!mask && TexturePaintChannelUtility.IsColor(channel));
            // Display pixels are sRGB. Encode working RGB exactly once; data previews keep their
            // numeric values. A display texture then renders consistently in Linear and Gamma projects.
            var display = new Color[colors.Length];
            for (int i = 0; i < colors.Length; i++) display[i] = color ? colors[i].gamma : colors[i];
            Texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, false)
                { name = "Overlay Painter draft preview", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Texture.SetPixels(display); Texture.Apply(false, false);
        }

        internal static Color[] ComposeView(Dictionary<TexturePaintChannel, Color[]> output,
            Dictionary<TexturePaintChannel, Color[]> before, TexturePaintChannel channel, TexturePaintPreviewView view)
        {
            if (output == null || !output.TryGetValue(channel, out var after)) return null;
            if (view == TexturePaintPreviewView.Channel) return after;
            Color[] source = null;
            before?.TryGetValue(channel, out source);
            var result = new Color[Resolution * Resolution];
            for (int i = 0; i < result.Length; i++)
            {
                Color from = source != null ? source[i] : Fallback(channel);
                bool showBefore = view == TexturePaintPreviewView.Before ||
                    (view == TexturePaintPreviewView.Split && i % Resolution < Resolution / 2);
                result[i] = showBefore ? from : Over(from, after[i]);
            }
            return result;
        }

        internal static Color[] Shade(Dictionary<TexturePaintChannel, Color[]> output,
            Dictionary<TexturePaintChannel, Color[]> before = null)
        {
            var result = new Color[Resolution * Resolution];
            Vector3 light = new Vector3(-.45f, .55f, 1).normalized;
            Vector3 halfway = (light + Vector3.forward).normalized;
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = y * Resolution + x;
                Color albedo = Surface(output, before, TexturePaintChannel.Albedo, i);
                Color encoded = Surface(output, before, TexturePaintChannel.Normal, i);
                Vector3 normal = new Vector3(encoded.r * 2 - 1, encoded.g * 2 - 1, encoded.b * 2 - 1);
                if (normal.sqrMagnitude < .000001f) normal = Vector3.forward;
                float dx = Height(output, before, x + 1, y) - Height(output, before, x - 1, y);
                float dy = Height(output, before, x, y + 1) - Height(output, before, x, y - 1);
                normal = (normal.normalized + new Vector3(-dx, -dy, 0) * Resolution * .35f).normalized;
                float ao = Mathf.Clamp01(Surface(output, before, TexturePaintChannel.AmbientOcclusion, i).r);
                float roughness = Mathf.Clamp(Surface(output, before, TexturePaintChannel.Roughness, i).r, .08f, 1);
                float metallic = Mathf.Clamp01(Surface(output, before, TexturePaintChannel.Metallic, i).r);
                float diffuse = Mathf.Max(0, Vector3.Dot(normal, light));
                float alpha = roughness * roughness, alpha2 = alpha * alpha;
                float nh = Mathf.Max(0, Vector3.Dot(normal, halfway));
                float denominator = nh * nh * (alpha2 - 1) + 1;
                float distribution = alpha2 / Mathf.Max(.0001f, Mathf.PI * denominator * denominator);
                Color reflectance = Color.Lerp(new Color(.04f, .04f, .04f), albedo, metallic);
                Color lit = albedo * ((.18f + diffuse * .7f) * ao * (1 - metallic * .85f)) +
                    reflectance * Mathf.Min(8, distribution * diffuse * .3f) * ao + albedo * metallic * .12f;
                lit.a = 1;
                result[i] = lit;
            }
            return result;
        }

        private static float Height(Dictionary<TexturePaintChannel, Color[]> output,
            Dictionary<TexturePaintChannel, Color[]> before, int x, int y)
        {
            if (output == null || !output.ContainsKey(TexturePaintChannel.NormalControl)) return 0;
            int index = Mathf.Clamp(y, 0, Resolution - 1) * Resolution + Mathf.Clamp(x, 0, Resolution - 1);
            float baseline = .5f;
            // Captured Normal already includes the input height. Add only the height change.
            if (before != null && before.ContainsKey(TexturePaintChannel.Normal))
            {
                baseline = Surface(null, before, TexturePaintChannel.NormalControl, index).r;
                if (output.TryGetValue(TexturePaintChannel.Normal, out var normals))
                    baseline = Mathf.Lerp(baseline, .5f, normals[index].a);
            }
            return Surface(output, before, TexturePaintChannel.NormalControl, index).r - baseline;
        }

        private static Color Surface(Dictionary<TexturePaintChannel, Color[]> output,
            Dictionary<TexturePaintChannel, Color[]> before, TexturePaintChannel channel, int index)
        {
            Color from = before != null && before.TryGetValue(channel, out var original) ? original[index] : Fallback(channel);
            return output != null && output.TryGetValue(channel, out var generated) ? Over(from, generated[index]) : from;
        }

        internal static Color Over(Color from, Color to) => new Color(Mathf.Lerp(from.r, to.r, to.a),
            Mathf.Lerp(from.g, to.g, to.a), Mathf.Lerp(from.b, to.b, to.a), to.a + from.a * (1 - to.a));

        private static Color Fallback(TexturePaintChannel channel) => channel switch
        {
            TexturePaintChannel.Normal => new Color(.5f, .5f, 1, 1),
            TexturePaintChannel.NormalControl => new Color(.5f, .5f, .5f, 1),
            TexturePaintChannel.Roughness => new Color(.7f, .7f, .7f, 1),
            TexturePaintChannel.AmbientOcclusion => Color.white,
            TexturePaintChannel.Albedo => new Color(.24f, .28f, .34f, 1),
            _ => Color.black
        };

        internal void Clear() { Set(null); }
        public void Dispose() { if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture); Texture = null; output = before = null; }
    }
}
