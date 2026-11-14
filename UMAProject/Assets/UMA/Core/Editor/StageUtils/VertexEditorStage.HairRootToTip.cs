using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    // Hair root-to-tip vertex colors: black where a card grows from the head, white at its tip. The hair shader's
    // sway (UMAHairControl) and its root/tip shading read them. The colors go through the Vertex Paint state, so
    // they show in its preview, are saved with Save as MeshModifier or Save Colors to SlotDataAsset, and undo like
    // painting does.
    public partial class VertexEditorStage
    {
        [SerializeField] private bool showVertexColorsView;
        [SerializeField] private bool showHairRootToTipSection;
        [SerializeField] private HairRootToTip.EndRule hairTieRule = HairRootToTip.EndRule.TextureTop;
        [SerializeField] private List<string> hairHeadSlotNames = new List<string>();
        [SerializeField] private string hairHeadSlotsPickedFor;

        private readonly List<int> hairRootMarks = new List<int>();
        private readonly List<int> hairTipMarks = new List<int>();
        private string hairMarksSlot;
        private string hairLastResult;
        private MessageType hairLastResultType = MessageType.Info;
        private Material vertexColorViewMaterial;

        /// <summary>Whether the model is drawn with its vertex colors only.</summary>
        private bool IsVertexColorViewActive =>
            !showOriginalMaterials && (IsVertexPaintModeEnabled || showVertexColorsView);

        /// <summary>The Vertex Colors display toggle, beside Original Materials.</summary>
        private void DrawVertexColorsViewOption()
        {
            EditorGUI.BeginChangeCheck();
            bool view = EditorGUILayout.Toggle(
                new GUIContent(
                    "Vertex Colors",
                    "Draw the model with its vertex colors only, unlit and from both sides, to check hair root-to-tip colors. Vertex Paint always draws them."),
                showVertexColorsView);
            if (EditorGUI.EndChangeCheck())
            {
                SetVertexColorsView(view);
            }
            else if (showVertexColorsView && !vertexPaintPreviewInitialized && BakedMesh != null)
            {
                // The model was rebuilt: show its colors again.
                InitializeVertexPaintPreview();
                ApplyVertexDisplayOptions();
            }
        }

        private void SetVertexColorsView(bool view)
        {
            showVertexColorsView = view;
            if (view)
            {
                showOriginalMaterials = false;
                if (!vertexPaintPreviewInitialized)
                {
                    InitializeVertexPaintPreview();
                }
            }
            ApplyVertexDisplayOptions();
            SceneView.RepaintAll();
        }

        /// <summary>The vertex color material for the view: like the paint preview, but drawn from both sides.</summary>
        private Material GetVertexColorViewMaterial()
        {
            if (vertexColorViewMaterial != null)
            {
                return vertexColorViewMaterial;
            }
            Material preview = GetVertexColorPreviewMaterial();
            if (preview == null)
            {
                return null;
            }
            vertexColorViewMaterial = new Material(preview)
            {
                name = "Vertex Color View",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertexColorViewMaterial.HasProperty("_Cull"))
            {
                // Hair cards are single faces; seen from behind they would vanish.
                vertexColorViewMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            return vertexColorViewMaterial;
        }

        private void DestroyHairRootToTipResources()
        {
            if (vertexColorViewMaterial != null)
            {
                DestroyImmediate(vertexColorViewMaterial);
                vertexColorViewMaterial = null;
            }
        }

        /// <summary>
        /// The slot whose colors are set: the one editable slot. Null, with the reason, when there is none or more
        /// than one.
        /// </summary>
        private SlotData GetHairColorSlot(out string problem)
        {
            problem = null;
            EnsureVertexPaintSession();
            if (IsVertexPaintAllSlotsMode)
            {
                problem = "Several slots are editable. Solo the hair slot in the Slots panel.";
                return null;
            }
            if (vertexPaintSlot == null || vertexPaintColors == null || vertexPaintSlot.asset == null)
            {
                problem = "Make the hair slot editable in the Slots panel.";
                return null;
            }
            return vertexPaintSlot;
        }

        /// <summary>The "Hair Root to Tip" section, shown in Vertex Paint and in Define Vertex Set/State.</summary>
        private void DrawHairRootToTipSection()
        {
            if (!DrawSectionFoldout(ref showHairRootToTipSection, "Hair Root to Tip"))
            {
                return;
            }
            EditorGUILayout.LabelField(
                "Colors the hair slot black where each card grows from the head and white at its tip, for the hair shader's sway (Hair Control) and root-to-tip shading. Tick Vertex Colors above to see them.",
                EditorStyles.wordWrappedMiniLabel);

            SlotData hair = GetHairColorSlot(out string problem);
            if (hair == null)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Hair Slot", hair.slotName);

            List<SlotData> candidates = GetHairHeadCandidates(hair);
            if (hairHeadSlotsPickedFor != hair.slotName)
            {
                PickHairHeadSlots(hair, candidates);
            }

            GUILayout.Label(
                new GUIContent("Head Slots", "The slots the hair grows from: the head, the face, a scalp. Cards are rooted where they meet these."),
                EditorStyles.miniBoldLabel);
            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox("No other slot is shown. Show the head in the Slots panel.", MessageType.Warning);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                string slotName = candidates[i].slotName;
                bool on = hairHeadSlotNames.Contains(slotName);
                bool now = EditorGUILayout.ToggleLeft(slotName, on);
                if (now != on)
                {
                    if (now) hairHeadSlotNames.Add(slotName);
                    else hairHeadSlotNames.Remove(slotName);
                }
            }
            if (GUILayout.Button(new GUIContent("Find Head Slots", "Tick the slots named head, face or scalp, or else the slots the hair lies on."), EditorStyles.miniButton))
            {
                PickHairHeadSlots(hair, candidates);
            }

            hairTieRule = (HairRootToTip.EndRule)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Unclear Cards",
                    "How to find the root of a card that does not go into the head and whose two ends are about as near it (hair lying along the skull).\n" +
                    "Texture Top: the root is at the top of the hair texture, as hair textures are drawn. Right on every such card of the UMA hair slots.\n" +
                    "Higher End: the root is the higher end. Wrong for hair that sticks up.\n" +
                    "Nearer Head: the root is the end nearer the head, however small the difference."),
                hairTieRule);

            bool haveHead = HasHairHeadSlot(candidates);
            using (new EditorGUI.DisabledScope(!haveHead))
            {
                if (GUILayout.Button(new GUIContent(
                        "Recalculate Vertex Colors from Head",
                        "Find the root of every card where it meets the head slots, walk each card to its far end, and color it from black at the root to white at the tip.")))
                {
                    RecalculateHairColorsFromHead(hair, candidates);
                }
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("Fix Cards by Hand", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "In Define Vertex Set, select the root vertices of one or more cards and press Set Root; select their tip vertices and press Set Tip. Interpolate then colors every card that has both.",
                EditorStyles.wordWrappedMiniLabel);
            if (hairMarksSlot != hair.slotName)
            {
                hairRootMarks.Clear();
                hairTipMarks.Clear();
                hairMarksSlot = hair.slotName;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Set Root (" + hairRootMarks.Count + ")", "Mark the selected vertices of the hair slot as roots (shown black)."), EditorStyles.miniButtonLeft))
            {
                SetHairMarksFromSelection(hair, hairRootMarks, "root");
            }
            if (GUILayout.Button(new GUIContent("Set Tip (" + hairTipMarks.Count + ")", "Mark the selected vertices of the hair slot as tips (shown white)."), EditorStyles.miniButtonMid))
            {
                SetHairMarksFromSelection(hair, hairTipMarks, "tip");
            }
            using (new EditorGUI.DisabledScope(hairRootMarks.Count == 0 && hairTipMarks.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Clear", "Forget the root and tip marks."), EditorStyles.miniButtonRight))
                {
                    hairRootMarks.Clear();
                    hairTipMarks.Clear();
                    SceneView.RepaintAll();
                }
            }
            GUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(hairRootMarks.Count == 0 || hairTipMarks.Count == 0))
            {
                if (GUILayout.Button(new GUIContent(
                        "Interpolate Colors from Root to Tip",
                        "Color every card that has a root mark and a tip mark: black at the roots, white at the tips, and in between by how far along the card a vertex is.")))
                {
                    InterpolateHairColors(hair);
                }
            }

            if (!string.IsNullOrEmpty(hairLastResult))
            {
                EditorGUILayout.HelpBox(hairLastResult, hairLastResultType);
            }

            using (new EditorGUI.DisabledScope(!vertexPaintDirty))
            {
                if (GUILayout.Button(new GUIContent(
                        "Save Colors to SlotDataAsset",
                        "Write the hair slot's colors into its SlotDataAsset. Save as MeshModifier, in Vertex Paint, keeps them in a MeshModifier instead.")))
                {
                    SaveVertexPaintToSlotDataAsset();
                }
            }
        }

        /// <summary>The other slots shown on the stage, which the hair could grow from.</summary>
        private List<SlotData> GetHairHeadCandidates(SlotData hair)
        {
            var result = new List<SlotData>();
            List<SlotData> shown = GetSelectableSlots();
            for (int i = 0; i < shown.Count; i++)
            {
                SlotData slot = shown[i];
                if (slot == null || ReferenceEquals(slot, hair) || slot.slotName == hair.slotName || slot.asset == null)
                {
                    continue;
                }
                result.Add(slot);
            }
            return result;
        }

        private bool HasHairHeadSlot(List<SlotData> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (hairHeadSlotNames.Contains(candidates[i].slotName)) return true;
            }
            return false;
        }

        /// <summary>
        /// Ticks the head slots: those named head, face or scalp; when there are none (a UMA 3 head is part of the
        /// body, as UDIM tiles), the slots that most of the hair lies nearest to.
        /// </summary>
        private void PickHairHeadSlots(SlotData hair, List<SlotData> candidates)
        {
            hairHeadSlotsPickedFor = hair.slotName;
            hairHeadSlotNames.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                string slotName = candidates[i].slotName.ToLowerInvariant();
                if (slotName.Contains("head") || slotName.Contains("face") || slotName.Contains("scalp"))
                {
                    hairHeadSlotNames.Add(candidates[i].slotName);
                }
            }
            if (hairHeadSlotNames.Count > 0 || candidates.Count == 0)
            {
                return;
            }

            RefreshBakedMeshCaches();
            if (!TryGetBakedSlotMesh(hair, out Vector3[] hairPositions, out _))
            {
                return;
            }
            // Which slot is each of a sample of hair vertices nearest to, within a few centimeters?
            var surfaces = new HairRootToTip.HeadSurface[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                if (TryGetBakedSlotMesh(candidates[i], out Vector3[] positions, out int[] triangles))
                {
                    surfaces[i] = new HairRootToTip.HeadSurface(positions, triangles);
                }
            }
            var votes = new int[candidates.Count];
            int voted = 0;
            int step = Mathf.Max(1, hairPositions.Length / 400);
            for (int v = 0; v < hairPositions.Length; v += step)
            {
                int best = -1;
                float bestDistance = HairHeadSearchDistance;
                for (int i = 0; i < surfaces.Length; i++)
                {
                    if (surfaces[i] == null) continue;
                    float distance = Mathf.Abs(surfaces[i].SignedDistance(hairPositions[v], HairHeadSearchDistance));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    votes[best]++;
                    voted++;
                }
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                if (voted > 0 && votes[i] >= voted * 0.1f)
                {
                    hairHeadSlotNames.Add(candidates[i].slotName);
                }
            }
        }

        /// <summary>How far from the hair a slot can be and still be found as its head.</summary>
        private const float HairHeadSearchDistance = 0.03f;

        /// <summary>A slot's vertices as the stage shows them (in the stage model's space), and its triangles.</summary>
        private bool TryGetBakedSlotMesh(SlotData slot, out Vector3[] positions, out int[] triangles)
        {
            positions = null;
            triangles = null;
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return false;
            }
            RefreshBakedMeshCaches();
            UMAMeshData meshData = slot.asset.meshData;
            int count = meshData.vertexCount;
            int start = GetVisibleBakedVertexIndex(slot, 0);
            if (bakedVertices == null || start < 0 || start + count > bakedVertices.Length)
            {
                return false;
            }
            if (meshData.submeshes == null || meshData.submeshes.Length == 0)
            {
                return false;
            }
            int submesh = Mathf.Clamp(slot.asset.subMeshIndex, 0, meshData.submeshes.Length - 1);
            if (meshData.submeshes[submesh] == null)
            {
                return false;
            }
            triangles = meshData.submeshes[submesh].getManagedTriangles(0);
            if (triangles == null || triangles.Length == 0)
            {
                return false;
            }
            positions = new Vector3[count];
            Array.Copy(bakedVertices, start, positions, 0, count);
            return true;
        }

        private void RecalculateHairColorsFromHead(SlotData hair, List<SlotData> candidates)
        {
            if (!TryGetBakedSlotMesh(hair, out Vector3[] hairPositions, out int[] hairTriangles))
            {
                SetHairResult("The hair slot has no triangles to work on.", MessageType.Warning);
                return;
            }
            var headPositions = new List<Vector3[]>();
            var headTriangles = new List<int[]>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!hairHeadSlotNames.Contains(candidates[i].slotName)) continue;
                if (TryGetBakedSlotMesh(candidates[i], out Vector3[] positions, out int[] triangles))
                {
                    headPositions.Add(positions);
                    headTriangles.Add(triangles);
                }
            }
            if (headPositions.Count == 0)
            {
                SetHairResult("Tick a head slot first.", MessageType.Warning);
                return;
            }

            float[] values;
            HairRootToTip.HeadReport report;
            try
            {
                EditorUtility.DisplayProgressBar("Hair Root to Tip", "Finding where the hair grows from the head...", 0.3f);
                var graph = new HairRootToTip.CardGraph(hairPositions, hairTriangles);
                var head = new HairRootToTip.HeadSurface(headPositions, headTriangles);
                HairRootToTip.HeadSettings settings = HairRootToTip.HeadSettings.Default;
                settings.tieRule = hairTieRule;
                settings.up = VertexObject != null ? VertexObject.transform.InverseTransformDirection(Vector3.up) : Vector3.up;
                values = HairRootToTip.FromHead(graph, hairPositions, hair.asset.meshData.uv, head, settings, out report);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int changed = ApplyHairValues(values, "Recalculate Hair Vertex Colors");
            string message = report.Cards + " cards colored (" + changed + " vertices changed): " +
                report.CardsIntoHead + " grow from where they go into the head, " +
                report.CardsByNearerEnd + " from their end nearer the head, " +
                report.CardsByTieRule + " by " + ObjectNames.NicifyVariableName(hairTieRule.ToString()) + " (check these).";
            if (report.CardsTooFarFromHead > 0)
            {
                message += " " + report.CardsTooFarFromHead + " cards are more than " +
                    Mathf.RoundToInt(HairRootToTip.HeadSettings.Default.maxHeadDistance * 100f) + " cm from the head and were left as they were.";
            }
            if (report.Cards > 0)
            {
                message += "\n\nTo fix a card that came out the wrong way round (white at the head, black at the tip): " +
                    "in Define Vertex Set, select its root vertices and press Set Root, select its tip vertices and press Set Tip, " +
                    "then press Interpolate Colors from Root to Tip. Black and white dots show the marks, only that card is recolored, " +
                    "and this box then says \"1 card colored\". Mark several cards at once to fix them together.";
            }
            SetHairResult(message, report.Cards > 0 ? MessageType.Info : MessageType.Warning);
            if (!showVertexColorsView && !IsVertexPaintModeEnabled)
            {
                SetVertexColorsView(true);
            }
        }

        private void SetHairMarksFromSelection(SlotData hair, List<int> marks, string what)
        {
            var picked = new List<int>();
            if (SelectedVertexes != null)
            {
                for (int i = 0; i < SelectedVertexes.Count; i++)
                {
                    VertexSelection selection = SelectedVertexes[i];
                    if (selection == null || selection.slot == null || selection.slot.slotName != hair.slotName) continue;
                    if (!picked.Contains(selection.vertexIndexOnSlot)) picked.Add(selection.vertexIndexOnSlot);
                }
            }
            if (picked.Count == 0)
            {
                SetHairResult("Select vertices of " + hair.slotName + " first (Define Vertex Set), then press Set " + (what == "root" ? "Root" : "Tip") + ".", MessageType.Warning);
                return;
            }
            marks.Clear();
            marks.AddRange(picked);
            SetHairResult(picked.Count + " vertices marked as " + what + ".", MessageType.Info);
            SceneView.RepaintAll();
        }

        private void InterpolateHairColors(SlotData hair)
        {
            if (!TryGetBakedSlotMesh(hair, out Vector3[] hairPositions, out int[] hairTriangles))
            {
                SetHairResult("The hair slot has no triangles to work on.", MessageType.Warning);
                return;
            }
            var graph = new HairRootToTip.CardGraph(hairPositions, hairTriangles);
            float[] values = HairRootToTip.BetweenRootsAndTips(graph, hairRootMarks, hairTipMarks, out int cards);
            if (cards == 0)
            {
                SetHairResult("No card has both a root mark and a tip mark. Mark the root and the tip of the same card.", MessageType.Warning);
                return;
            }
            int changed = ApplyHairValues(values, "Interpolate Hair Vertex Colors");
            SetHairResult(cards + (cards == 1 ? " card" : " cards") + " colored from root to tip (" + changed + " vertices changed).", MessageType.Info);
            if (!showVertexColorsView && !IsVertexPaintModeEnabled)
            {
                SetVertexColorsView(true);
            }
        }

        private void SetHairResult(string message, MessageType type)
        {
            hairLastResult = message;
            hairLastResultType = type;
        }

        /// <summary>
        /// Writes root-to-tip values into the hair slot's colors (alpha is kept) and the preview. Values that are
        /// <see cref="HairRootToTip.Unset"/> leave their vertex as it was. Returns the number of vertices changed.
        /// </summary>
        private int ApplyHairValues(float[] values, string undoName)
        {
            EnsureVertexPaintSession();
            EndVertexPaintStroke(true);
            if (values == null || vertexPaintColors == null || vertexPaintColors.Length != values.Length || BakedMesh == null)
            {
                SetHairResult("The hair slot no longer matches the model on the stage. Close and reopen the stage.", MessageType.Warning);
                return 0;
            }

            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, undoName);
            int changed = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (float.IsNaN(values[i])) continue;
                Color32 color = HairRootToTip.ToColor(values[i], vertexPaintColors[i].a);
                if (ColorsEqual(color, vertexPaintColors[i])) continue;
                vertexPaintColors[i] = color;
                changed++;
            }
            if (changed > 0)
            {
                ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
                SyncVertexPaintAdjustments();
                vertexPaintDirtySlots.Add(GetVertexPaintSlotKey());
                vertexPaintDirty = true;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
            }
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            return changed;
        }

        /// <summary>Draws the root marks black and the tip marks white, on repaint.</summary>
        private void DrawHairRootTipMarks()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || VertexObject == null ||
                (hairRootMarks.Count == 0 && hairTipMarks.Count == 0) || string.IsNullOrEmpty(hairMarksSlot))
            {
                return;
            }
            SlotData slot = null;
            foreach (SlotData recipeSlot in RecipeSlots)
            {
                if (recipeSlot != null && recipeSlot.slotName == hairMarksSlot)
                {
                    slot = recipeSlot;
                    break;
                }
            }
            if (slot == null)
            {
                return;
            }
            RefreshBakedMeshCaches();
            int start = GetVisibleBakedVertexIndex(slot, 0);
            if (bakedVertices == null || start < 0)
            {
                return;
            }
            Color saved = Handles.color;
            var zTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            DrawHairMarks(hairRootMarks, start, Color.black, Color.white);
            DrawHairMarks(hairTipMarks, start, Color.white, Color.black);
            Handles.zTest = zTest;
            Handles.color = saved;
        }

        private void DrawHairMarks(List<int> marks, int start, Color fill, Color outline)
        {
            Transform transform = VertexObject.transform;
            for (int i = 0; i < marks.Count; i++)
            {
                int baked = start + marks[i];
                if (baked < 0 || baked >= bakedVertices.Length) continue;
                Vector3 position = transform.TransformPoint(bakedVertices[baked]);
                float size = HandleUtility.GetHandleSize(position) * 0.025f;
                Handles.color = outline;
                Handles.DotHandleCap(0, position, Quaternion.identity, size * 1.4f, EventType.Repaint);
                Handles.color = fill;
                Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
            }
        }
    }
}
