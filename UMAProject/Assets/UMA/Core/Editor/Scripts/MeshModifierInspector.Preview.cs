using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// The Mesh Modifier inspector's "Edit with first character in scene" button and its preview: the
    /// slots the runtime modifiers name, in white at their rest pose, with the modifiers applied at the
    /// preview's Amount (0 = not applied, 1 = applied at each stack's own Scale).
    /// </summary>
    public partial class MeshModifierInspector
    {
        private static readonly Color PreviewSurfaceColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Vector2 PreviewDefaultRotation = new Vector2(8f, 180f);   // the front, from slightly above
        private static readonly Vector3 PreviewKeyLight = new Vector3(-0.45f, 0.55f, -1f).normalized;   // camera space, upper left front
        private static readonly Vector3 PreviewFillLight = new Vector3(0.8f, 0.1f, -0.35f).normalized;

        private sealed class PreviewSlot
        {
            public SlotDataAsset slot;
            public readonly List<MeshModifier.Modifier> stacks = new List<MeshModifier.Modifier>();
            public int adjustmentCount;
            public bool changesNormals;   // decided at full amount, so the shading method does not switch while sliding
        }

        private readonly List<PreviewSlot> previewSlots = new List<PreviewSlot>();
        private PreviewRenderUtility previewUtility;
        private Material previewMaterial;
        private bool previewMaterialShowsColors;
        private Mesh previewMesh;
        private UMARigRestPose previewRig;
        private Quaternion previewUpright = Quaternion.identity;
        private Vector3[] previewNormals;
        private Color[] previewBaseColors, previewLitColors;
        private Bounds? previewBounds;
        private Vector2 previewRotation = PreviewDefaultRotation;
        private float previewZoom = 1f;
        private float previewAmount = 1f;
        private float builtAmount = float.NaN;
        private Vector2 litRotation = new Vector2(float.NaN, float.NaN);
        private string previewKey;
        private string previewProblem;
        private int previewAdjustments;

        /// <summary>Opens the Vertex Editor on this modifier with the first character in the scene, as dropping it on that character's Editing tools does.</summary>
        private static void DrawEditInSceneButton(MeshModifier modifier)
        {
            DynamicCharacterAvatar avatar = MeshHideInspector.FindFirstSceneAvatar();
            if (avatar == null)
            {
                EditorGUILayout.HelpBox("To edit this, you need a DynamicCharacterAvatar in your scene.", MessageType.Info);
                return;
            }

            bool built = Application.isPlaying || avatar.editorTimeGeneration;
            using (new EditorGUI.DisabledScope(!built))
            {
                if (GUILayout.Button(new GUIContent("Edit with first character in scene",
                    $"Opens the Vertex Editor on this Mesh Modifier with '{avatar.name}', as dropping it on that character's Editing tools does.")))
                {
                    avatar.ignoreMeshHideAssets = true;
                    avatar.GenerateNow();
                    VertexEditorStage.ShowStage(avatar, modifier);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.LabelField(built
                ? $"Uses '{avatar.name}'."
                : $"'{avatar.name}' has Editor Time Generation off, so it is not built. Turn it on in its inspector to edit with it.",
                EditorStyles.wordWrappedMiniLabel);
        }

        public override bool HasPreviewGUI() => targets.Length == 1 && target is MeshModifier;

        public override GUIContent GetPreviewTitle() => new GUIContent("Affected slots");

        public override string GetInfoString()
        {
            if (previewSlots.Count == 0)
            {
                return previewProblem ?? string.Empty;
            }
            string info = $"{previewSlots.Count} slot{(previewSlots.Count == 1 ? "" : "s")}, {previewAdjustments:N0} adjustments, applied at {previewAmount:0.##}";
            return previewProblem != null ? info + "\n" + previewProblem : info;
        }

        public override void OnPreviewSettings()
        {
            GUILayout.Label(new GUIContent("Amount", "How much of the modifier is applied: 0 is none, 1 is each stack at its own Scale (a DNA-driven stack at DNA value 1)."), EditorStyles.miniLabel);
            float amount = GUILayout.HorizontalSlider(previewAmount, 0f, 1f, GUILayout.Width(80f));
            if (!Mathf.Approximately(amount, previewAmount))
            {
                previewAmount = amount;
                Repaint();
            }
            GUILayout.Label(previewAmount.ToString("0.00"), EditorStyles.miniLabel, GUILayout.Width(28f));
            if (GUILayout.Button("Front", EditorStyles.toolbarButton)) { previewRotation = PreviewDefaultRotation; Repaint(); }
            if (GUILayout.Button("Side", EditorStyles.toolbarButton)) { previewRotation = new Vector2(PreviewDefaultRotation.x, 90f); Repaint(); }
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton)) { previewRotation = PreviewDefaultRotation; previewZoom = 1f; previewAmount = 1f; Repaint(); }
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background) => OnInteractivePreviewGUI(rect, background);

        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
        {
            if (rect.width < 2 || rect.height < 2)
            {
                return;
            }
            HandlePreviewInput(rect);
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            EnsurePreviewSlots();
            if (previewSlots.Count == 0)
            {
                EditorGUI.HelpBox(rect, previewProblem ?? "Nothing to preview.", MessageType.Info);
                return;
            }
            EnsurePreviewUtility();
            EnsurePreviewMesh();
            if (previewMesh == null)
            {
                EditorGUI.HelpBox(rect, previewProblem ?? "Nothing to preview.", MessageType.Info);
                return;
            }
            UpdateLighting();

            Bounds bounds = previewBounds ?? previewMesh.bounds;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            float aspect = rect.width / rect.height;
            float halfFov = previewUtility.cameraFieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Sin(halfFov) * previewZoom / Mathf.Min(1f, aspect);
            Camera camera = previewUtility.camera;
            camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -distance), Quaternion.identity);
            Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(previewRotation.x, previewRotation.y, 0f)) * Matrix4x4.Translate(-bounds.center);

            previewUtility.BeginPreview(rect, background);
            Texture result;
            try
            {
                previewUtility.DrawMesh(previewMesh, matrix, previewMaterial, 0);
                previewUtility.Render(true);
            }
            finally
            {
                result = previewUtility.EndPreview();
            }
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        private void HandlePreviewInput(Rect rect)
        {
            int control = GUIUtility.GetControlID("UMAMeshModifierPreview".GetHashCode(), FocusType.Passive, rect);
            Event evt = Event.current;
            switch (evt.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && rect.Contains(evt.mousePosition)) { GUIUtility.hotControl = control; evt.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == control)
                    {
                        previewRotation += new Vector2(evt.delta.y, -evt.delta.x) * 0.7f;
                        previewRotation.x = Mathf.Clamp(previewRotation.x, -89f, 89f);
                        evt.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == control) { GUIUtility.hotControl = 0; evt.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(evt.mousePosition))
                    {
                        previewZoom = Mathf.Clamp(previewZoom * Mathf.Exp(evt.delta.y * 0.05f), 0.15f, 3f);
                        evt.Use();
                        Repaint();
                    }
                    break;
            }
        }

        /// <summary>Finds the slots the runtime stacks name again when the modifier changed.</summary>
        private void EnsurePreviewSlots()
        {
            var modifier = target as MeshModifier;
            string key = modifier == null ? "none" : $"{EditorUtility.GetDirtyCount(modifier)}|{modifier.runtimeModifiers?.Count}";
            if (key == previewKey)
            {
                return;
            }
            previewKey = key;
            previewSlots.Clear();
            previewProblem = null;
            previewAdjustments = 0;
            previewRig = null;
            previewUpright = Quaternion.identity;
            previewBounds = null;
            builtAmount = float.NaN;
            DestroyPreviewMesh();

            if (modifier == null || modifier.runtimeModifiers == null || modifier.runtimeModifiers.Count == 0)
            {
                previewProblem = modifier != null && modifier.EditorModifiers != null && modifier.EditorModifiers.Count > 0
                    ? "This modifier has no runtime stacks. Use Rebuild Runtime Modifiers from Editor Modifiers."
                    : "This modifier has no stacks.";
                return;
            }

            var missing = new List<string>();
            foreach (MeshModifier.Modifier stack in modifier.runtimeModifiers)
            {
                if (stack == null || stack.adjustments == null || stack.adjustments.vertexAdjustments == null || stack.adjustments.vertexAdjustments.Count == 0)
                {
                    continue;
                }
                SlotDataAsset slot = FindSlot(stack.SlotName);
                if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null)
                {
                    string name = string.IsNullOrEmpty(stack.SlotName) ? "(no slot name)" : stack.SlotName;
                    if (!missing.Contains(name)) missing.Add(name);
                    continue;
                }
                PreviewSlot entry = previewSlots.Find(s => s.slot == slot);
                if (entry == null)
                {
                    entry = new PreviewSlot { slot = slot };
                    previewSlots.Add(entry);
                }
                entry.stacks.Add(stack);
                entry.adjustmentCount += stack.adjustments.vertexAdjustments.Count;
                previewAdjustments += stack.adjustments.vertexAdjustments.Count;
            }
            if (missing.Count > 0)
            {
                previewProblem = "Not in the Global Library, so not shown: " + string.Join(", ", missing) + ".";
            }
            else if (previewSlots.Count == 0)
            {
                previewProblem = "No stack has any adjustments.";
            }
            if (previewSlots.Count == 0)
            {
                return;
            }

            string ignored = null;
            foreach (PreviewSlot entry in previewSlots)
            {
                ApplyStacks(entry, 1f, out entry.changesNormals, ref ignored);
            }

            var skinned = new List<SlotDataAsset>();
            foreach (PreviewSlot entry in previewSlots)
            {
                if (entry.slot.meshData.umaBones != null && entry.slot.meshData.umaBones.Length > 0)
                {
                    skinned.Add(entry.slot);
                }
            }
            if (skinned.Count > 0)
            {
                previewRig = UMARigRestPose.FromSlots(skinned, skinned[0].meshData.RootBoneName, Matrix4x4.identity);
                previewUpright = MeshHideInspector.GetUprightRotation(previewRig);
            }
        }

        /// <summary>
        /// The slot a stack's key names: its logical name, or the asset-name form ("..._slot") older
        /// modifiers were saved with.
        /// </summary>
        private static SlotDataAsset FindSlot(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            UMAAssetIndexer index = UMAAssetIndexer.Instance;
            if (index == null)
            {
                return null;
            }
            if (index.HasAsset<SlotDataAsset>(key))
            {
                return index.GetAsset<SlotDataAsset>(key);
            }
            const string legacySuffix = "_slot";
            if (key.EndsWith(legacySuffix, StringComparison.OrdinalIgnoreCase))
            {
                string logical = key.Substring(0, key.Length - legacySuffix.Length);
                if (index.HasAsset<SlotDataAsset>(logical))
                {
                    SlotDataAsset slot = index.GetAsset<SlotDataAsset>(logical);
                    return slot != null && MeshModifier.IsLegacySlotKey(key, slot) ? slot : null;
                }
            }
            return null;
        }

        /// <summary>Builds the preview mesh again when the amount or the modifier changed.</summary>
        private void EnsurePreviewMesh()
        {
            if (previewMesh != null && Mathf.Approximately(builtAmount, previewAmount))
            {
                return;
            }
            builtAmount = previewAmount;

            var positions = new List<Vector3>();
            var bakedNormals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var keepBakedNormal = new List<bool>();
            string failure = null;
            foreach (PreviewSlot entry in previewSlots)
            {
                UMAMeshData source = entry.slot.meshData;
                UMAMeshData applied = ApplyStacks(entry, previewAmount, out _, ref failure);
                bool normalsChanged = entry.changesNormals;
                if (previewRig == null || !UMARigBodySurface.TryBakeToAvatar(applied, previewRig, out Vector3[] slotPositions, out Vector3[] slotNormals))
                {
                    slotPositions = (Vector3[])applied.vertices.Clone();
                    slotNormals = applied.normals != null && applied.normals.Length == slotPositions.Length ? applied.normals : null;
                }
                int offset = positions.Count;
                Color32[] slotColors = applied.colors32 != null && applied.colors32.Length == slotPositions.Length ? applied.colors32 : null;
                for (int i = 0; i < slotPositions.Length; i++)
                {
                    positions.Add(previewUpright * slotPositions[i]);
                    bakedNormals.Add(slotNormals != null ? previewUpright * slotNormals[i] : Vector3.zero);
                    keepBakedNormal.Add(normalsChanged && slotNormals != null);
                    Color tint = slotColors != null ? (Color)slotColors[i] : Color.white;
                    colors.Add(new Color(tint.r * PreviewSurfaceColor.r, tint.g * PreviewSurfaceColor.g, tint.b * PreviewSurfaceColor.b, 1f));
                }
                for (int s = 0; s < source.submeshes.Length; s++)
                {
                    int[] slotTriangles = source.submeshes[s] != null ? source.submeshes[s].getManagedTriangles(0) : null;
                    if (slotTriangles == null) continue;
                    for (int t = 0; t + 2 < slotTriangles.Length; t += 3)
                    {
                        triangles.Add(slotTriangles[t] + offset);
                        triangles.Add(slotTriangles[t + 1] + offset);
                        triangles.Add(slotTriangles[t + 2] + offset);
                    }
                }
            }

            Vector3[] normals = WeldedNormals(positions, triangles);
            for (int i = 0; i < normals.Length; i++)
            {
                if (keepBakedNormal[i] && bakedNormals[i].sqrMagnitude > 1e-12f)
                {
                    normals[i] = bakedNormals[i].normalized;
                }
            }

            if (previewMesh == null)
            {
                previewMesh = new Mesh { name = "Mesh modifier preview", hideFlags = HideFlags.HideAndDontSave };
            }
            previewMesh.Clear();
            previewMesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            previewMesh.SetVertices(positions);
            previewMesh.SetTriangles(triangles, 0, false);
            previewMesh.normals = normals;
            previewMesh.RecalculateBounds();
            previewBounds ??= previewMesh.bounds;   // keep the framing while the amount slides

            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            previewNormals = normals;
            previewBaseColors = new Color[colors.Count];
            for (int i = 0; i < colors.Count; i++)
            {
                previewBaseColors[i] = linear ? colors[i].linear : colors[i];
            }
            previewLitColors = new Color[colors.Count];
            litRotation = new Vector2(float.NaN, float.NaN);
            if (failure != null)
            {
                previewProblem = failure;
            }
        }

        /// <summary>
        /// The slot's mesh data with its stacks applied in order, each at its Scale times
        /// <paramref name="amount"/>, the way a build chains them. The slot asset is not changed.
        /// </summary>
        private static UMAMeshData ApplyStacks(PreviewSlot entry, float amount, out bool normalsChanged, ref string failure)
        {
            UMAMeshData source = entry.slot.meshData;
            UMAMeshData current = source;
            normalsChanged = false;
            foreach (MeshModifier.Modifier stack in entry.stacks)
            {
                float scale = stack.Scale * amount;
                if (scale == 0f)
                {
                    continue;
                }
                UMAMeshData working = current.ShallowClearCopy();
                working.boneWeights = source.boneWeights;
                try
                {
                    if (scale == 1f)
                    {
                        stack.adjustments.Apply(working, current);
                    }
                    else
                    {
                        stack.adjustments.ApplyScaled(working, current, scale);
                    }
                }
                catch (Exception e)
                {
                    failure = $"The stack for '{stack.SlotName}' could not be applied: {e.Message}";
                    continue;
                }
                normalsChanged |= working.normalsModified;
                current = working;
            }
            return current;
        }

        /// <summary>Smooth normals shared by vertices at the same position, so UV seams and slot borders do not show as creases.</summary>
        private static Vector3[] WeldedNormals(List<Vector3> positions, List<int> triangles)
        {
            var groupOf = new int[positions.Count];
            var groups = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i] * 100000f;
                var cell = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!groups.TryGetValue(cell, out int group))
                {
                    group = groups.Count;
                    groups.Add(cell, group);
                }
                groupOf[i] = group;
            }
            var sums = new Vector3[groups.Count];
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                sums[groupOf[a]] += face;
                sums[groupOf[b]] += face;
                sums[groupOf[c]] += face;
            }
            var normals = new Vector3[positions.Count];
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 sum = sums[groupOf[i]];
                normals[i] = sum.sqrMagnitude > 1e-20f ? sum.normalized : Vector3.up;
            }
            return normals;
        }

        /// <summary>
        /// Shades the vertex colors for the current view: the preview draws them unlit so the modifier's
        /// own vertex colors show on the white surface in every render pipeline.
        /// </summary>
        private void UpdateLighting()
        {
            if (!previewMaterialShowsColors || previewNormals == null || litRotation == previewRotation)
            {
                return;
            }
            litRotation = previewRotation;
            Quaternion view = Quaternion.Euler(previewRotation.x, previewRotation.y, 0f);
            for (int i = 0; i < previewNormals.Length; i++)
            {
                Vector3 n = view * previewNormals[i];
                float key = Vector3.Dot(n, PreviewKeyLight);
                // Cull is off, so the inside of a surface faces the camera with its normal away: light that side too.
                if (Vector3.Dot(n, Vector3.back) < 0f) key = -key;
                float shade = 0.22f + 0.7f * Mathf.Max(0f, key) + 0.18f * Mathf.Max(0f, Vector3.Dot(n, PreviewFillLight));
                Color c = previewBaseColors[i];
                previewLitColors[i] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
            }
            previewMesh.colors = previewLitColors;
        }

        private void EnsurePreviewUtility()
        {
            if (previewUtility == null)
            {
                previewUtility = new PreviewRenderUtility();
                previewUtility.cameraFieldOfView = 30f;
                previewUtility.camera.clearFlags = CameraClearFlags.Color;
                previewUtility.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
                previewUtility.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);
                previewUtility.lights[0].intensity = 1.4f;
                previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
                previewUtility.lights[1].intensity = 0.8f;
                previewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 0f);
            }
            if (previewMaterial == null)
            {
                // An unlit vertex-color shader that every pipeline draws; the shading is baked into the colors.
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                previewMaterialShowsColors = shader != null;
                if (shader != null)
                {
                    previewMaterial = new Material(shader) { name = "Mesh modifier preview", hideFlags = HideFlags.HideAndDontSave };
                    previewMaterial.SetColor("_Color", Color.white);
                    previewMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
                    previewMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
                    previewMaterial.SetInt("_ZWrite", 1);
                    previewMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                    previewMaterial.SetInt("_Cull", (int)CullMode.Off);
                    previewMaterial.renderQueue = (int)RenderQueue.Geometry;
                }
                else
                {
                    previewMaterial = MeshHideInspector.CreatePreviewMaterial(PreviewSurfaceColor, "Mesh modifier preview");
                }
                litRotation = new Vector2(float.NaN, float.NaN);
            }
        }

        private void DestroyPreviewMesh()
        {
            if (previewMesh != null) DestroyImmediate(previewMesh);
            previewMesh = null;
            previewNormals = null;
            previewBaseColors = previewLitColors = null;
        }

        private void OnDisable()
        {
            DestroyPreviewMesh();
            previewUtility?.Cleanup();
            previewUtility = null;
            if (previewMaterial != null) DestroyImmediate(previewMaterial);
            previewMaterial = null;
            previewSlots.Clear();
            previewKey = null;
        }
    }
}
