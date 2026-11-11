using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Touchup Weights: the skeleton drawn over the character, and a Pose mode to turn its bones.
    /// </summary>
    /// <remarks>
    /// Weights can only be judged in a pose that bends the joint in question. Pose mode turns the
    /// bones of the character being edited, by rings in the Scene view or by sliders, and the mesh is
    /// skinned again each time, with the weights being edited. The pose is a working aid: it is not
    /// saved anywhere, it is not part of Undo, and every bone gets its own rotation back when Touchup
    /// Weights closes or scripts reload.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const string TouchupShowBonesPrefsKey = "UMA.TouchupWeights.ShowBones";
        private const string TouchupSlotBonesOnlyPrefsKey = "UMA.TouchupWeights.SlotBonesOnly";
        // A new key: the option used to be on unless turned off, which turned both legs unasked.
        private const string TouchupPoseMirrorPrefsKey = "UMA.TouchupWeights.PoseMirrorBothSides";
        private const string TouchupShowWeightColorsPrefsKey = "UMA.TouchupWeights.ShowWeightColors";
        private const float TouchupBonePickPixels = 9f;

        private static readonly Color TouchupBoneColor = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color TouchupBoneHoverColor = new Color(1f, 0.92f, 0.3f, 1f);
        private static readonly Color TouchupPoseBoneColor = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color TouchupWeightBoneColor = new Color(0.25f, 0.9f, 1f, 1f);
        private static readonly Color TouchupPosedBoneColor = new Color(1f, 0.75f, 0.45f, 0.8f);

        private bool touchupPosePrefsLoaded;
        private bool touchupShowBones = true;
        private bool touchupSlotBonesOnly = true;
        private bool touchupPoseMirror;
        private bool touchupShowWeightColors = true;
        private Transform touchupPoseBone;
        private Transform touchupHoverBone;
        // The local rotation each turned bone had before it was first turned.
        private readonly Dictionary<Transform, Quaternion> touchupPoseOriginal = new Dictionary<Transform, Quaternion>();
        // How far each turned bone is turned from that, as the sliders show it.
        private readonly Dictionary<Transform, Vector3> touchupPoseEuler = new Dictionary<Transform, Vector3>();
        private Mesh touchupPoseBakeMesh;

        // The bones drawn: rebuilt when the renderer, the slot or the filter changes.
        private readonly List<Transform> touchupDrawnBones = new List<Transform>();
        private readonly HashSet<Transform> touchupDrawnBoneSet = new HashSet<Transform>();
        private SkinnedMeshRenderer touchupDrawnBonesRenderer;
        private SlotData touchupDrawnBonesSlot;
        private bool touchupDrawnBonesSlotOnly;

        /// <summary>How many bones are turned away from the character's own pose.</summary>
        internal int TouchupPosedBoneCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<Transform, Quaternion> pair in touchupPoseOriginal)
                {
                    if (pair.Key != null && Quaternion.Angle(pair.Key.localRotation, pair.Value) > 0.01f)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>The bone Pose mode turns; null when none is chosen.</summary>
        internal Transform TouchupPoseBone => touchupPoseBone;

        private void LoadTouchupPosePrefs()
        {
            if (touchupPosePrefsLoaded)
            {
                return;
            }
            touchupPosePrefsLoaded = true;
            touchupShowBones = EditorPrefs.GetBool(TouchupShowBonesPrefsKey, true);
            touchupSlotBonesOnly = EditorPrefs.GetBool(TouchupSlotBonesOnlyPrefsKey, true);
            touchupPoseMirror = EditorPrefs.GetBool(TouchupPoseMirrorPrefsKey, false);
            touchupShowWeightColors = EditorPrefs.GetBool(TouchupShowWeightColorsPrefsKey, true);
        }

        // --- which bones, and where they are in the stage -------------------------------------------

        private SkinnedMeshRenderer TouchupPoseRenderer =>
            stageSkinnedMeshRenderer != null ? stageSkinnedMeshRenderer : GetCurrentSkinnedMeshRenderer();

        /// <summary>
        /// The stage shows the baked mesh at its own object, in the renderer's space: a point of the
        /// character in the world, as a point in the stage.
        /// </summary>
        private Vector3 TouchupWorldToStage(SkinnedMeshRenderer renderer, Vector3 world)
        {
            return VertexObject.transform.TransformPoint(renderer.transform.InverseTransformPoint(world));
        }

        private Quaternion TouchupStageRotation(SkinnedMeshRenderer renderer)
        {
            return VertexObject.transform.rotation * Quaternion.Inverse(renderer.transform.rotation);
        }

        private List<Transform> GetTouchupDrawnBones(SkinnedMeshRenderer renderer)
        {
            LoadTouchupPosePrefs();
            if (ReferenceEquals(touchupDrawnBonesRenderer, renderer) && ReferenceEquals(touchupDrawnBonesSlot, touchupWeightSlot) &&
                touchupDrawnBonesSlotOnly == touchupSlotBonesOnly && touchupDrawnBones.Count > 0 && touchupDrawnBones[0] != null)
            {
                return touchupDrawnBones;
            }
            touchupDrawnBones.Clear();
            touchupDrawnBoneSet.Clear();
            touchupDrawnBonesRenderer = renderer;
            touchupDrawnBonesSlot = touchupWeightSlot;
            touchupDrawnBonesSlotOnly = touchupSlotBonesOnly;
            if (renderer == null)
            {
                return touchupDrawnBones;
            }

            var skeleton = new HashSet<Transform>();
            foreach (Transform bone in renderer.bones)
            {
                if (bone != null)
                {
                    skeleton.Add(bone);
                }
            }
            HashSet<int> slotBones = null;
            UMAMeshData meshData = touchupWeightSlot != null && touchupWeightSlot.asset != null ? touchupWeightSlot.asset.meshData : null;
            if (touchupSlotBonesOnly && meshData != null && meshData.umaBones != null)
            {
                slotBones = new HashSet<int>();
                foreach (UMATransform bone in meshData.umaBones)
                {
                    if (bone != null)
                    {
                        slotBones.Add(bone.hash);
                    }
                }
            }
            foreach (Transform bone in skeleton)
            {
                if (slotBones != null && !slotBones.Contains(UMAUtils.StringToHash(bone.name)))
                {
                    continue;
                }
                // The bone, and the chain above it: turning a parent moves it too.
                for (Transform t = bone; t != null && skeleton.Contains(t); t = t.parent)
                {
                    if (!touchupDrawnBoneSet.Add(t))
                    {
                        break;
                    }
                    touchupDrawnBones.Add(t);
                }
            }
            return touchupDrawnBones;
        }

        private Transform FindTouchupBone(int nameHash)
        {
            SkinnedMeshRenderer renderer = TouchupPoseRenderer;
            if (renderer == null)
            {
                return null;
            }
            foreach (Transform bone in renderer.bones)
            {
                if (bone != null && UMAUtils.StringToHash(bone.name) == nameHash)
                {
                    return bone;
                }
            }
            return null;
        }

        /// <summary>The same bone on the other side of the character, by its Left/Right name; null when it has none.</summary>
        private Transform FindTouchupMirrorBone(Transform bone)
        {
            if (bone == null)
            {
                return null;
            }
            string name = bone.name;
            string other = null;
            if (name.Contains("Left")) other = name.Replace("Left", "Right");
            else if (name.Contains("Right")) other = name.Replace("Right", "Left");
            if (other == null || other == name)
            {
                return null;
            }
            return FindTouchupBone(UMAUtils.StringToHash(other));
        }

        // --- the pose ----------------------------------------------------------------------------------

        private static Vector3 SignedEuler(Quaternion rotation)
        {
            Vector3 e = rotation.eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        }

        private Quaternion TouchupPoseOriginalRotation(Transform bone)
        {
            if (!touchupPoseOriginal.TryGetValue(bone, out Quaternion original))
            {
                original = bone.localRotation;
                touchupPoseOriginal.Add(bone, original);
            }
            return original;
        }

        /// <summary>
        /// Turns a bone to a local rotation, and with Mirror its counterpart on the other side by the
        /// mirrored amount, then skins the mesh again.
        /// </summary>
        internal void SetTouchupPoseRotation(Transform bone, Quaternion localRotation, bool refreshSliders = true)
        {
            if (bone == null)
            {
                return;
            }
            LoadTouchupPosePrefs();
            Quaternion original = TouchupPoseOriginalRotation(bone);
            bone.localRotation = localRotation;
            Quaternion turn = Quaternion.Inverse(original) * localRotation;
            if (refreshSliders || !touchupPoseEuler.ContainsKey(bone))
            {
                touchupPoseEuler[bone] = SignedEuler(turn);
            }
            Transform other = touchupPoseMirror ? FindTouchupMirrorBone(bone) : null;
            if (other != null)
            {
                Quaternion otherOriginal = TouchupPoseOriginalRotation(other);
                // The two sides are mirror images across the character's middle.
                var mirrored = new Quaternion(turn.x, -turn.y, -turn.z, turn.w);
                other.localRotation = otherOriginal * mirrored;
                touchupPoseEuler[other] = SignedEuler(mirrored);
            }
            RefreshTouchupPosedMesh();
        }

        /// <summary>Turns a bone by Euler angles from its own rotation, as the sliders do.</summary>
        internal void SetTouchupPoseEuler(Transform bone, Vector3 euler)
        {
            if (bone == null)
            {
                return;
            }
            Quaternion original = TouchupPoseOriginalRotation(bone);
            touchupPoseEuler[bone] = euler;
            SetTouchupPoseRotation(bone, original * Quaternion.Euler(euler), false);
        }

        internal void ResetTouchupPoseBone(Transform bone)
        {
            if (bone == null || !touchupPoseOriginal.TryGetValue(bone, out Quaternion original))
            {
                return;
            }
            SetTouchupPoseRotation(bone, original);
        }

        /// <summary>Gives every turned bone its own rotation back and skins the mesh again.</summary>
        internal void ResetTouchupPose()
        {
            RestoreTouchupPoseBones();
            RefreshTouchupPosedMesh();
        }

        /// <summary>Gives every turned bone its own rotation back. Safe to call when the stage is closing.</summary>
        private void RestoreTouchupPoseBones()
        {
            foreach (KeyValuePair<Transform, Quaternion> pair in touchupPoseOriginal)
            {
                if (pair.Key != null)
                {
                    pair.Key.localRotation = pair.Value;
                }
            }
            touchupPoseOriginal.Clear();
            touchupPoseEuler.Clear();
        }

        /// <summary>
        /// The character was built again, which may have put the bones back: turns them as they were
        /// posed. Called before the stage bakes its mesh.
        /// </summary>
        private void ReapplyTouchupPose()
        {
            if (!touchupWeightsMode || touchupPoseOriginal.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<Transform, Vector3> pair in touchupPoseEuler)
            {
                if (pair.Key != null && touchupPoseOriginal.TryGetValue(pair.Key, out Quaternion original))
                {
                    pair.Key.localRotation = original * Quaternion.Euler(pair.Value);
                }
            }
        }

        private void DestroyTouchupPoseResources()
        {
            RestoreTouchupPoseBones();
            if (touchupPoseBakeMesh != null)
            {
                DestroyImmediate(touchupPoseBakeMesh);
            }
            touchupPoseBakeMesh = null;
            touchupPoseBone = null;
            touchupHoverBone = null;
            touchupDrawnBones.Clear();
            touchupDrawnBoneSet.Clear();
        }

        /// <summary>
        /// The skeleton moved: bakes the character's mesh in the new pose, then puts the weights being
        /// edited back on top, as Live update does.
        /// </summary>
        private void RefreshTouchupPosedMesh()
        {
            SkinnedMeshRenderer renderer = TouchupPoseRenderer;
            if (closing || renderer == null || BakedMesh == null || VertexObject == null)
            {
                return;
            }
            if (touchupPoseBakeMesh == null)
            {
                touchupPoseBakeMesh = new Mesh { name = "Touchup Pose Bake", hideFlags = HideFlags.HideAndDontSave };
            }
            renderer.BakeMesh(touchupPoseBakeMesh, true);
            if (touchupPoseBakeMesh.vertexCount != BakedMesh.vertexCount)
            {
                return;
            }
            BakedMesh.vertices = touchupPoseBakeMesh.vertices;
            Vector3[] normals = touchupPoseBakeMesh.normals;
            if (normals != null && normals.Length == BakedMesh.vertexCount)
            {
                BakedMesh.normals = normals;
            }
            BakedMesh.RecalculateBounds();
            // These hold where the weights being edited put each vertex against the old bake.
            touchupSkinningPositionOffsets.Clear();
            RefreshBakedMeshCaches();

            var edited = new HashSet<int>(touchupSavedPositionVertexIndices);
            edited.UnionWith(touchupPendingPaintWeights.Keys);
            edited.UnionWith(touchupPaintStrokeWeights.Keys);
            edited.UnionWith(touchupPreviewVertexIndices);
            if (edited.Count > 0)
            {
                RecalculateTouchupVertexPositions(edited);
            }
            RefreshTouchupSelectionPositions();
            RefreshSculptCollider();
            SceneView.RepaintAll();
        }

        // --- panel -------------------------------------------------------------------------------------

        /// <summary>
        /// Whether the slot being weighted is coloured by the selected bone's weight, as Blender does:
        /// red for full weight, through yellow and green, to blue for none. Off shows the slot with its
        /// own material. Remembered between sessions; on by default.
        /// </summary>
        internal bool TouchupShowWeightColors
        {
            get
            {
                LoadTouchupPosePrefs();
                return touchupShowWeightColors;
            }
            set
            {
                LoadTouchupPosePrefs();
                if (touchupShowWeightColors == value)
                {
                    return;
                }
                touchupShowWeightColors = value;
                EditorPrefs.SetBool(TouchupShowWeightColorsPrefsKey, value);
                if (touchupWeightsMode)
                {
                    RefreshTouchupWeightVisualization();
                    ApplyVertexDisplayOptions();
                }
                SceneView.RepaintAll();
            }
        }

        /// <summary>Shown in every Touchup Weights mode.</summary>
        private void DrawTouchupShowBonesOptions()
        {
            LoadTouchupPosePrefs();
            bool colors = EditorGUILayout.Toggle(
                new GUIContent("Weight Colors", "Color the slot being weighted by the selected bone's weight, as Blender does: red is full weight, through yellow and green, to blue for none. Off shows the slot with its own material."),
                touchupShowWeightColors);
            if (colors != touchupShowWeightColors)
            {
                TouchupShowWeightColors = colors;
            }
            bool show = EditorGUILayout.Toggle(
                new GUIContent("Show Bones", "Draw the skeleton over the character. Cyan: the bone being weighted. Orange: the bone Pose mode turns."),
                touchupShowBones);
            if (show != touchupShowBones)
            {
                touchupShowBones = show;
                EditorPrefs.SetBool(TouchupShowBonesPrefsKey, show);
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!touchupShowBones && touchupWeightTool != TouchupWeightTool.Pose))
            {
                bool slotOnly = EditorGUILayout.Toggle(
                    new GUIContent("Slot Bones Only", "Draw only the bones the current slot is weighted to, and the bones above them. Off: every bone of the character."),
                    touchupSlotBonesOnly);
                if (slotOnly != touchupSlotBonesOnly)
                {
                    touchupSlotBonesOnly = slotOnly;
                    EditorPrefs.SetBool(TouchupSlotBonesOnlyPrefsKey, slotOnly);
                    SceneView.RepaintAll();
                }
            }
            int posed = TouchupPosedBoneCount;
            if (posed > 0 && touchupWeightTool != TouchupWeightTool.Pose)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(posed + " bone(s) posed", EditorStyles.miniLabel);
                if (GUILayout.Button("Reset Pose", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    ResetTouchupPose();
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTouchupPoseOptions()
        {
            LoadTouchupPosePrefs();
            EditorGUILayout.HelpBox(
                "Click a bone in the Scene view, then turn it with the rings or the sliders. The pose is only for judging weights: " +
                "it is not saved and not part of Undo, and the character gets its own pose back when Touchup Weights closes.",
                MessageType.Info);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Bone", GUILayout.Width(72f));
            EditorGUILayout.LabelField(touchupPoseBone != null ? touchupPoseBone.name : "none: click one", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Transform weightBone = FindTouchupBone(touchupWeightBoneHash);
            using (new EditorGUI.DisabledScope(weightBone == null || weightBone == touchupPoseBone))
            {
                if (GUILayout.Button(new GUIContent("Pose Weight Bone", "Choose the bone selected in the Touchup Weights window as the bone to turn.")))
                {
                    touchupPoseBone = weightBone;
                    SceneView.RepaintAll();
                }
            }
            using (new EditorGUI.DisabledScope(touchupPoseBone == null || touchupPoseBone == weightBone))
            {
                if (GUILayout.Button(new GUIContent("Weight This Bone", "Make the bone being turned the bone that Paint mode and the Touchup Weights window work on.")))
                {
                    TouchupWeightBoneHash = UMAUtils.StringToHash(touchupPoseBone.name);
                    RepaintLinkedEditors();
                }
            }
            GUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(touchupPoseBone == null))
            {
                Vector3 euler = touchupPoseBone != null && touchupPoseEuler.TryGetValue(touchupPoseBone, out Vector3 stored) ? stored : Vector3.zero;
                EditorGUI.BeginChangeCheck();
                euler.x = EditorGUILayout.Slider(new GUIContent("Turn X", "Degrees about the bone's own X axis, from the character's own pose."), euler.x, -180f, 180f);
                euler.y = EditorGUILayout.Slider(new GUIContent("Turn Y", "Degrees about the bone's own Y axis, from the character's own pose."), euler.y, -180f, 180f);
                euler.z = EditorGUILayout.Slider(new GUIContent("Turn Z", "Degrees about the bone's own Z axis, from the character's own pose."), euler.z, -180f, 180f);
                if (EditorGUI.EndChangeCheck() && touchupPoseBone != null)
                {
                    SetTouchupPoseEuler(touchupPoseBone, euler);
                }
            }

            bool mirror = EditorGUILayout.Toggle(
                new GUIContent("Mirror Left/Right", "Turn the bone of the same name on the other side by the mirrored amount."),
                touchupPoseMirror);
            if (mirror != touchupPoseMirror)
            {
                touchupPoseMirror = mirror;
                EditorPrefs.SetBool(TouchupPoseMirrorPrefsKey, mirror);
            }

            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(touchupPoseBone == null || !touchupPoseOriginal.ContainsKey(touchupPoseBone)))
            {
                if (GUILayout.Button(new GUIContent("Reset Bone", "Give this bone its own rotation back (and its mirror, with Mirror on).")))
                {
                    ResetTouchupPoseBone(touchupPoseBone);
                }
            }
            int posed = TouchupPosedBoneCount;
            using (new EditorGUI.DisabledScope(touchupPoseOriginal.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Reset Pose" + (posed > 0 ? " (" + posed + ")" : string.Empty), "Give every turned bone its own rotation back.")))
                {
                    ResetTouchupPose();
                }
            }
            GUILayout.EndHorizontal();
        }

        // --- Scene view --------------------------------------------------------------------------------

        /// <summary>Draws the skeleton. In Pose mode the bone under the pointer is lit.</summary>
        private void DrawTouchupSkeleton(SkinnedMeshRenderer renderer, bool posing)
        {
            if (renderer == null || VertexObject == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            List<Transform> bones = GetTouchupDrawnBones(renderer);
            Transform weightBone = FindTouchupBone(touchupWeightBoneHash);
            var previousZTest = Handles.zTest;
            Color previousColor = Handles.color;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (Transform bone in bones)
            {
                if (bone == null)
                {
                    continue;
                }
                Vector3 position = TouchupWorldToStage(renderer, bone.position);
                Color color = TouchupBoneColor;
                float width = 2f;
                if (bone == touchupPoseBone && posing) { color = TouchupPoseBoneColor; width = 4f; }
                else if (bone == touchupHoverBone && posing) { color = TouchupBoneHoverColor; width = 4f; }
                else if (bone == weightBone) { color = TouchupWeightBoneColor; width = 3.5f; }
                else if (touchupPoseOriginal.TryGetValue(bone, out Quaternion original) && Quaternion.Angle(bone.localRotation, original) > 0.01f) { color = TouchupPosedBoneColor; width = 3f; }
                Handles.color = color;
                bool hasChild = false;
                for (int c = 0; c < bone.childCount; c++)
                {
                    Transform child = bone.GetChild(c);
                    if (!touchupDrawnBoneSet.Contains(child))
                    {
                        continue;
                    }
                    hasChild = true;
                    Handles.DrawAAPolyLine(width, position, TouchupWorldToStage(renderer, child.position));
                }
                float size = HandleUtility.GetHandleSize(position) * (hasChild ? 0.025f : 0.04f);
                Handles.DrawSolidDisc(position, Camera.current != null ? Camera.current.transform.forward : Vector3.forward, size);
            }
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        /// <summary>The drawn bone nearest the pointer, within a few pixels; null when none is.</summary>
        private Transform PickTouchupBone(SkinnedMeshRenderer renderer, Vector2 mousePosition)
        {
            Transform best = null;
            float bestDistance = TouchupBonePickPixels;
            foreach (Transform bone in GetTouchupDrawnBones(renderer))
            {
                if (bone == null)
                {
                    continue;
                }
                Vector3 position = TouchupWorldToStage(renderer, bone.position);
                bool hasChild = false;
                for (int c = 0; c < bone.childCount; c++)
                {
                    Transform child = bone.GetChild(c);
                    if (!touchupDrawnBoneSet.Contains(child))
                    {
                        continue;
                    }
                    hasChild = true;
                    float distance = HandleUtility.DistanceToLine(position, TouchupWorldToStage(renderer, child.position));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = bone;
                    }
                }
                if (!hasChild)
                {
                    float distance = (HandleUtility.WorldToGUIPoint(position) - mousePosition).magnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = bone;
                    }
                }
            }
            return best;
        }

        /// <summary>Pose mode in the Scene view: pick a bone by clicking it, turn it with the rings.</summary>
        private void HandleTouchupPoseSceneGUI(SceneView sceneView, Event currentEvent, bool mouseOverAnyWindow)
        {
            SkinnedMeshRenderer renderer = TouchupPoseRenderer;
            if (renderer == null || VertexObject == null)
            {
                return;
            }
            int defaultControl = GUIUtility.GetControlID(GetHashCode() ^ 0x50053, FocusType.Passive);
            if (currentEvent.type == EventType.Layout && !mouseOverAnyWindow && !currentEvent.alt)
            {
                HandleUtility.AddDefaultControl(defaultControl);
            }

            DrawTouchupSkeleton(renderer, true);

            if (touchupPoseBone != null)
            {
                Quaternion stageRotation = TouchupStageRotation(renderer);
                Vector3 position = TouchupWorldToStage(renderer, touchupPoseBone.position);
                Quaternion shown = stageRotation * touchupPoseBone.rotation;
                EditorGUI.BeginChangeCheck();
                Quaternion turned = Handles.RotationHandle(shown, position);
                if (EditorGUI.EndChangeCheck())
                {
                    Quaternion world = Quaternion.Inverse(stageRotation) * turned;
                    Quaternion local = touchupPoseBone.parent != null
                        ? Quaternion.Inverse(touchupPoseBone.parent.rotation) * world
                        : world;
                    SetTouchupPoseRotation(touchupPoseBone, local);
                    RepaintLinkedEditors();
                }
            }

            if (mouseOverAnyWindow || currentEvent.alt)
            {
                return;
            }
            if (currentEvent.type == EventType.MouseMove)
            {
                Transform hover = PickTouchupBone(renderer, currentEvent.mousePosition);
                if (hover != touchupHoverBone)
                {
                    touchupHoverBone = hover;
                    sceneView.Repaint();
                }
            }
            else if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 &&
                     GUIUtility.hotControl == 0 && HandleUtility.nearestControl == defaultControl)
            {
                Transform picked = PickTouchupBone(renderer, currentEvent.mousePosition);
                if (picked != null)
                {
                    touchupPoseBone = picked;
                    currentEvent.Use();
                    RepaintLinkedEditors();
                    sceneView.Repaint();
                }
            }
        }
    }
}
