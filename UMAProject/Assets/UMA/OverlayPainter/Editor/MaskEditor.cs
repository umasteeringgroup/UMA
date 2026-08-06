using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class MaskEditor : EditorWindow
    {
        private TexturePaintStageController controller;
        private Vector2 scroll;
        private TexturePaintMaskPreset preset;
        private bool editActiveLayer = true;

        public static void Open(TexturePaintStageController controller)
        {
            MaskEditor window = GetWindow<MaskEditor>(true, "Overlay Painter Masks");
            window.controller = controller; window.minSize = new Vector2(420f, 300f); window.Show();
        }

        private void OnGUI()
        {
            if (controller == null) { EditorGUILayout.HelpBox("Open this window from an active Overlay Painter stage.", MessageType.Info); return; }
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            TextureSet activeSet = null;
            TexturePaintLayer activeLayer = null;
            bool hasLayer = stage != null && stage.TryGetActiveLayer(out activeSet, out activeLayer);
            editActiveLayer = EditorGUILayout.ToggleLeft("Attach masks to active layer", editActiveLayer && hasLayer);
            TexturePaintMaskStack stack = editActiveLayer && hasLayer
                ? new TexturePaintMaskStack(activeLayer.masks)
                : controller.Masks;
            List<TexturePaintMask> masksBefore = TexturePaintStageWindow.CloneMasksForHistory(stack.Masks);
            string ownerLayer = editActiveLayer && hasLayer ? activeLayer.id : null;
            string ownerSurface = editActiveLayer && hasLayer ? activeSet.surface.index.ToString() : null;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            preset = (TexturePaintMaskPreset)EditorGUILayout.ObjectField("Preset", preset, typeof(TexturePaintMaskPreset), false);
            using (new EditorGUI.DisabledScope(preset == null))
            {
                if (GUILayout.Button("Apply", GUILayout.Width(60f)))
                {
                    preset.ApplyTo(stack);
                    for (int i = 0; i < stack.Masks.Count; i++)
                    {
                        stack.Masks[i].ownerLayerId = ownerLayer;
                        stack.Masks[i].ownerSurfaceId = ownerSurface;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < stack.Masks.Count; i++)
            {
                TexturePaintMask mask = stack.Masks[i];
                EditorGUILayout.BeginVertical("box");
                mask.enabled = EditorGUILayout.ToggleLeft(mask.name, mask.enabled);
                mask.name = EditorGUILayout.TextField("Name", mask.name);
                mask.kind = (TexturePaintMaskKind)EditorGUILayout.EnumPopup("Kind", mask.kind);
                mask.operation = (TexturePaintMaskOperation)EditorGUILayout.EnumPopup("Combine", mask.operation);
                mask.invert = EditorGUILayout.Toggle("Invert", mask.invert);
                mask.inputMin = EditorGUILayout.Slider("Input Min", mask.inputMin, 0f, 1f);
                mask.inputMax = EditorGUILayout.Slider("Input Max", mask.inputMax, 0f, 1f);
                mask.gamma = EditorGUILayout.Slider("Gamma", mask.gamma, 0.01f, 4f);
                mask.feather = EditorGUILayout.Slider("Feather", mask.feather, 0f, 0.5f);
                mask.blurRadius = EditorGUILayout.IntSlider("Blur Radius", mask.blurRadius, 0, 16);
                if (mask.kind == TexturePaintMaskKind.Slot) mask.surfaceIndex = EditorGUILayout.IntField("Surface", mask.surfaceIndex);
                if (mask.kind == TexturePaintMaskKind.Painted || mask.kind == TexturePaintMaskKind.Bitmap)
                    mask.grayscaleTexture = (Texture2D)EditorGUILayout.ObjectField("Grayscale", mask.grayscaleTexture, typeof(Texture2D), false);
                if (mask.kind == TexturePaintMaskKind.Painted && GUILayout.Button("Paint Mask in 3D"))
                    stage?.BeginMaskSelection(mask, 3);
                if (mask.kind == TexturePaintMaskKind.ID) mask.idValue = EditorGUILayout.IntField("ID", mask.idValue);
                if (mask.kind == TexturePaintMaskKind.Polygon || mask.kind == TexturePaintMaskKind.UVIsland)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Click")) stage?.BeginMaskSelection(mask, 0);
                    if (GUILayout.Button("Box")) stage?.BeginMaskSelection(mask, 1);
                    if (GUILayout.Button("Lasso")) stage?.BeginMaskSelection(mask, 2);
                    if (mask.kind == TexturePaintMaskKind.Polygon && hasLayer && GUILayout.Button("Grow"))
                        ResizePolygonSelection(mask, activeSet.surface.mesh, true);
                    if (mask.kind == TexturePaintMaskKind.Polygon && hasLayer && GUILayout.Button("Shrink"))
                        ResizePolygonSelection(mask, activeSet.surface.mesh, false);
                    if (GUILayout.Button("Clear Selection")) { mask.triangleIndices.Clear(); mask.uvIslandIndices.Clear(); }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Remove")) { stack.RemoveAt(i); i--; }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ White")) AddMask(stack, ownerLayer, ownerSurface, "White Mask", TexturePaintMaskKind.White);
            if (GUILayout.Button("+ Black")) AddMask(stack, ownerLayer, ownerSurface, "Black Mask", TexturePaintMaskKind.Black);
            if (GUILayout.Button("+ Bitmap")) AddMask(stack, ownerLayer, ownerSurface, "Bitmap Mask", TexturePaintMaskKind.Bitmap);
            if (GUILayout.Button("+ Painted")) AddMask(stack, ownerLayer, ownerSurface, "Paint Mask", TexturePaintMaskKind.Painted);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Slot")) AddMask(stack, ownerLayer, ownerSurface, "Slot Mask", TexturePaintMaskKind.Slot);
            if (GUILayout.Button("+ Polygon")) AddMask(stack, ownerLayer, ownerSurface, "Polygon Mask", TexturePaintMaskKind.Polygon);
            if (GUILayout.Button("+ UV Island")) AddMask(stack, ownerLayer, ownerSurface, "UV Island Mask", TexturePaintMaskKind.UVIsland);
            if (GUILayout.Button("+ ID")) AddMask(stack, ownerLayer, ownerSurface, "ID Mask", TexturePaintMaskKind.ID);
            if (GUILayout.Button("+ Procedural")) AddMask(stack, ownerLayer, ownerSurface, "Procedural Mask", TexturePaintMaskKind.Procedural);
            GUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(stage == null))
                if (GUILayout.Button("Stop Scene Mask Tool (Esc)")) stage?.EndMaskSelection();
            if (EditorGUI.EndChangeCheck())
            {
                stack.Touch();
                stage?.RecordMaskChange(editActiveLayer && hasLayer ? activeSet : null,
                    editActiveLayer && hasLayer ? activeLayer : null, masksBefore, stack.Masks);
            }
        }

        private static void AddMask(TexturePaintMaskStack stack, string ownerLayer, string ownerSurface,
            string name, TexturePaintMaskKind kind)
        {
            stack.Add(new TexturePaintMask
            {
                name = name,
                kind = kind,
                ownerLayerId = ownerLayer,
                ownerSurfaceId = ownerSurface
            });
        }

        private static void ResizePolygonSelection(TexturePaintMask mask, Mesh mesh, bool grow)
        {
            if (mesh == null) return;
            int[] triangles = mesh.triangles;
            System.Collections.Generic.HashSet<int> selected = new System.Collections.Generic.HashSet<int>(mask.triangleIndices);
            System.Collections.Generic.HashSet<int> vertices = new System.Collections.Generic.HashSet<int>();
            foreach (int triangle in selected)
            {
                int offset = triangle * 3;
                if (offset + 2 >= triangles.Length) continue;
                vertices.Add(triangles[offset]); vertices.Add(triangles[offset + 1]); vertices.Add(triangles[offset + 2]);
            }
            if (grow)
            {
                for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                {
                    int offset = triangle * 3;
                    if (vertices.Contains(triangles[offset]) || vertices.Contains(triangles[offset + 1]) || vertices.Contains(triangles[offset + 2]))
                        selected.Add(triangle);
                }
            }
            else
            {
                System.Collections.Generic.HashSet<int> remove = new System.Collections.Generic.HashSet<int>();
                System.Collections.Generic.HashSet<int> outsideVertices = new System.Collections.Generic.HashSet<int>();
                for (int other = 0; other < triangles.Length / 3; other++)
                {
                    if (selected.Contains(other)) continue;
                    int otherOffset = other * 3;
                    outsideVertices.Add(triangles[otherOffset]); outsideVertices.Add(triangles[otherOffset + 1]);
                    outsideVertices.Add(triangles[otherOffset + 2]);
                }
                foreach (int triangle in selected)
                {
                    int offset = triangle * 3;
                    if (outsideVertices.Contains(triangles[offset]) || outsideVertices.Contains(triangles[offset + 1]) ||
                        outsideVertices.Contains(triangles[offset + 2])) remove.Add(triangle);
                }
                selected.ExceptWith(remove);
            }
            mask.triangleIndices = new System.Collections.Generic.List<int>(selected);
        }
    }
}
