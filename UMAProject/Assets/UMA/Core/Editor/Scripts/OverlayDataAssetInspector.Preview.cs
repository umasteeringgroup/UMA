#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace UMA.Editors
{
    public partial class OverlayDataAssetInspector
    {
        private PreviewRenderUtility overlayPreview;
        private Mesh overlayPreviewCube;
        private Material overlayPreviewMaterial, overlayPreviewSecondPass;
        private Vector2 overlayPreviewRotation = new Vector2(-20, -30);
        private float overlayPreviewZoom = 1f;
        private readonly List<string> overlayPreviewMessages = new List<string>();
        private GUIStyle overlayPreviewMessageStyle;
        private Vector2 overlayPreviewMessageScroll;

        public override bool HasPreviewGUI() => targets.Length == 1 && target is OverlayDataAsset;
        public override GUIContent GetPreviewTitle() => new GUIContent("Shaded overlay");

        public override void OnPreviewSettings()
        {
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
            {
                overlayPreviewRotation = new Vector2(-20, -30);
                overlayPreviewZoom = 1f;
                Repaint();
            }
            if (GUILayout.Button("X +90", EditorStyles.toolbarButton))
            { overlayPreviewRotation.x += 90; Repaint(); }
            if (GUILayout.Button("Y +90", EditorStyles.toolbarButton))
            { overlayPreviewRotation.y += 90; Repaint(); }
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background) => OnInteractivePreviewGUI(rect, background);

        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
        {
            if (IsEditorBusy || rect.width < 2 || rect.height < 2) return;
            var overlay = target as OverlayDataAsset;
            var umaMaterial = overlay != null ? overlay.material : null;
            if (umaMaterial == null || umaMaterial.material == null || umaMaterial.material.shader == null)
            {
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.HelpBox(rect, "Assign a UMA Material with a Unity material to preview this overlay.", MessageType.Info);
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                // Rebuild the list each frame: resolved problems disappear and repeated messages do not accumulate.
                overlayPreviewMessages.Clear();
                UpdateOverlayPreviewMaterial(ref overlayPreviewMaterial, umaMaterial.material, overlay, overlayPreviewMessages);
                UpdateOverlayPreviewMaterial(ref overlayPreviewSecondPass, umaMaterial.secondPass, overlay, overlayPreviewMessages);
            }
            rect = DrawOverlayPreviewMessages(rect);
            HandleOverlayPreviewInput(rect);
            if (Event.current.type != EventType.Repaint) return;
            if (overlayPreview == null)
            {
                overlayPreview = new PreviewRenderUtility();
                overlayPreview.cameraFieldOfView = 30;
                overlayPreview.camera.nearClipPlane = 0.01f;
                overlayPreview.camera.farClipPlane = 100f;
                overlayPreview.camera.clearFlags = CameraClearFlags.Color;
                overlayPreview.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1);
                overlayPreview.ambientColor = new Color(0.4f, 0.4f, 0.4f, 1);
                overlayPreview.lights[0].intensity = 2f;
                overlayPreview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
                overlayPreview.lights[1].intensity = 1.2f;
                overlayPreview.lights[1].transform.rotation = Quaternion.Euler(340, 218, 0);
            }
            if (overlayPreviewCube == null) overlayPreviewCube = CreateOverlayPreviewCube();

            float aspect = rect.width / rect.height;
            float distance = 3.7f * overlayPreviewZoom / Mathf.Min(1f, aspect);
            overlayPreview.camera.transform.SetPositionAndRotation(new Vector3(0, 0, -distance), Quaternion.identity);
            var matrix = Matrix4x4.TRS(Vector3.zero,
                Quaternion.Euler(overlayPreviewRotation.x, overlayPreviewRotation.y, 0), Vector3.one);
            Texture result;
            overlayPreview.BeginPreview(rect, background);
            try
            {
                overlayPreview.DrawMesh(overlayPreviewCube, matrix, overlayPreviewMaterial, 0);
                if (overlayPreviewSecondPass != null)
                    overlayPreview.DrawMesh(overlayPreviewCube, matrix, overlayPreviewSecondPass, 0);
                overlayPreview.Render(true);
            }
            finally { result = overlayPreview.EndPreview(); }
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        private Rect DrawOverlayPreviewMessages(Rect rect)
        {
            if (overlayPreviewMessages.Count == 0) return rect;
            if (overlayPreviewMessageStyle == null)
            {
                overlayPreviewMessageStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                overlayPreviewMessageStyle.normal.textColor = new Color(1f, 0.78f, 0.5f);
            }
            float width = Mathf.Max(1, rect.width - 24);
            float contentHeight = 4;
            foreach (string message in overlayPreviewMessages)
                contentHeight += overlayPreviewMessageStyle.CalcHeight(new GUIContent(message), width) + 2;
            float height = Mathf.Min(contentHeight + 4, rect.height * 0.45f);
            var footer = new Rect(rect.x, rect.yMax - height, rect.width, height);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(footer, new Color(0.12f, 0.12f, 0.12f, 1));
            overlayPreviewMessageScroll = GUI.BeginScrollView(footer, overlayPreviewMessageScroll,
                new Rect(0, 0, width + 8, contentHeight));
            float y = 2;
            foreach (string message in overlayPreviewMessages)
            {
                float lineHeight = overlayPreviewMessageStyle.CalcHeight(new GUIContent(message), width);
                GUI.Label(new Rect(4, y, width, lineHeight), message, overlayPreviewMessageStyle);
                y += lineHeight + 2;
            }
            GUI.EndScrollView();
            rect.height -= height;
            return rect;
        }

        private void HandleOverlayPreviewInput(Rect rect)
        {
            int control = GUIUtility.GetControlID("UMAOverlayCubePreview".GetHashCode(), FocusType.Passive, rect);
            var evt = Event.current;
            switch (evt.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && rect.Contains(evt.mousePosition))
                    { GUIUtility.hotControl = control; evt.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == control)
                    {
                        overlayPreviewRotation += new Vector2(evt.delta.y, evt.delta.x) * 0.7f;
                        evt.Use(); Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == control)
                    { GUIUtility.hotControl = 0; evt.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(evt.mousePosition))
                    {
                        overlayPreviewZoom = Mathf.Clamp(overlayPreviewZoom * Mathf.Exp(evt.delta.y * 0.05f), 0.65f, 2.5f);
                        evt.Use(); Repaint();
                    }
                    break;
            }
        }

        internal static void UpdateOverlayPreviewMaterial(ref Material preview, Material source, OverlayDataAsset overlay,
            List<string> messages = null)
        {
            if (source == null)
            {
                if (preview != null) DestroyImmediate(preview);
                preview = null;
                return;
            }
            if (preview == null || preview.shader != source.shader)
            {
                if (preview != null) DestroyImmediate(preview);
                preview = new Material(source) { hideFlags = HideFlags.HideAndDontSave, name = "UMA overlay preview" };
            }
            else preview.CopyPropertiesFromMaterial(source);

            var channels = overlay.material.channels;
            var textures = overlay.textureList;
            if (channels != null && textures != null)
            {
                for (int i = 0; i < channels.Length && i < textures.Length; i++)
                {
                    var channel = channels[i];
                    string property = channel.materialPropertyName;
                    if (channel.NonShaderTexture || channel.channelType == UMAMaterial.ChannelType.MaterialColor ||
                        string.IsNullOrEmpty(property)) continue;
                    if (!preview.HasTexture(property))
                    {
                        AddPreviewMessage(messages, $"Texture parameter {property} doesn't exist in the shader.");
                        continue;
                    }
                    preview.SetTexture(property, textures[i]);
                    if (textures[i] == null) continue;
                    // Enable map features declared by common Unity shaders, retaining custom shader settings.
                    if (property == "_BumpMap" || property == "_NormalMap") EnablePreviewKeyword(preview, "_NORMALMAP", messages);
                    if (property == "_MetallicGlossMap")
                        EnablePreviewKeyword(preview, "_METALLICGLOSSMAP", messages, "_METALLICSPECGLOSSMAP");
                    if (property == "_SpecGlossMap")
                        EnablePreviewKeyword(preview, "_SPECGLOSSMAP", messages, "_METALLICSPECGLOSSMAP");
                    if (property == "_EmissionMap") EnablePreviewKeyword(preview, "_EMISSION", messages);
                    if (property == "_OcclusionMap") EnablePreviewKeyword(preview, "_OCCLUSIONMAP", messages);
                }
            }
            if (preview.HasTexture("_AlphaMask0"))
                preview.SetTexture("_AlphaMask0", overlay.alphaMask != null ? overlay.alphaMask :
                    textures != null && textures.Length > 0 ? textures[0] : null);
        }

        private static void EnablePreviewKeyword(Material material, string name, List<string> messages = null,
            string alternative = null)
        {
            var keyword = material.shader.keywordSpace.FindKeyword(name);
            if (!keyword.isValid && alternative != null) keyword = material.shader.keywordSpace.FindKeyword(alternative);
            if (keyword.isValid) material.SetKeyword(keyword, true);
            else AddPreviewMessage(messages, $"Local keyword {name} doesn't exist in the shader.");
        }

        private static void AddPreviewMessage(List<string> messages, string message)
        {
            if (messages != null && !messages.Contains(message)) messages.Add(message);
        }

        internal static Mesh CreateOverlayPreviewCube()
        {
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var tangents = new Vector4[24];
            var uv = new Vector2[24];
            var triangles = new int[36];
            var directions = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            var corners = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            for (int face = 0; face < directions.Length; face++)
            {
                var normal = directions[face];
                var up = Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up;
                var right = Vector3.Cross(up, normal);
                for (int corner = 0; corner < 4; corner++)
                {
                    int v = face * 4 + corner;
                    uv[v] = corners[corner];
                    vertices[v] = normal * 0.5f + right * (uv[v].x - 0.5f) + up * (uv[v].y - 0.5f);
                    normals[v] = normal;
                    tangents[v] = new Vector4(right.x, right.y, right.z, 1);
                }
                int t = face * 6, start = face * 4;
                triangles[t] = start; triangles[t + 1] = start + 1; triangles[t + 2] = start + 2;
                triangles[t + 3] = start; triangles[t + 4] = start + 2; triangles[t + 5] = start + 3;
            }
            var mesh = new Mesh { name = "UMA overlay preview cube", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents;
            mesh.uv = uv; mesh.uv2 = uv; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DisposeOverlayPreview()
        {
            overlayPreview?.Cleanup(); overlayPreview = null;
            if (overlayPreviewCube != null) DestroyImmediate(overlayPreviewCube);
            if (overlayPreviewMaterial != null) DestroyImmediate(overlayPreviewMaterial);
            if (overlayPreviewSecondPass != null) DestroyImmediate(overlayPreviewSecondPass);
            overlayPreviewCube = null; overlayPreviewMaterial = overlayPreviewSecondPass = null;
            overlayPreviewMessages.Clear();
            overlayPreviewMessageScroll = Vector2.zero;
        }
    }
}
#endif
