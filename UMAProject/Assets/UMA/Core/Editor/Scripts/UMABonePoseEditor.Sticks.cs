using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.PoseTools
{
    /// <summary>
    /// Blender-style "stick" rendering of the rig in the bone pose editor, with click-to-select.
    /// Each visible bone is drawn as a baton from its own pivot (head) to its tail:
    ///  - one offset child: the tail is that child;
    ///  - several children or none: a short stub continuing the parent-to-bone direction, with
    ///    dashed relationship lines to the other visible children;
    ///  - coincident with its parent and childless (typically an Adjust bone): a ring around the joint.
    /// </summary>
    public partial class UMABonePoseEditor
    {
        public enum BoneDrawStyle { Line, Stick }
        public static int boneDrawStyle = (int)BoneDrawStyle.Line;
        public static readonly string[] boneDrawStyleNames = { "Line", "Stick" };
        public static float stickBoneWidth = 5f;
        public const float StickBoneMinWidth = 2f;
        public const float StickBoneMaxWidth = 14f;

        private const float StickBoneEpsilon = 0.0001f;
        private const float StickStubMinLength = 0.01f;
        private const float StickStubMaxLength = 0.15f;
        private static readonly int StickBonePickHash = "UMABonePoseEditor.StickBonePick".GetHashCode();
        private static readonly Vector3[] stickSegmentBuffer = new Vector3[2];

        private static readonly Color StickOutlineColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        private static readonly Color StickBoneColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
        private static readonly Color StickAdjustBoneColor = new Color(0.6f, 0.7f, 0.82f, 0.95f);
        private static readonly Color StickActiveColor = new Color(0.25f, 0.95f, 0.35f, 1f);
        private static readonly Color StickMirrorColor = new Color(0.2f, 0.6f, 1f, 1f);
        private static readonly Color StickHoverColor = new Color(1f, 0.82f, 0.3f, 1f);
        private static readonly Color StickRelationshipColor = new Color(0.8f, 0.8f, 0.8f, 0.45f);

        private struct StickBone
        {
            public Transform bone;
            public Vector3 head;
            public Vector3 tail;
            public bool isPoint;
        }

        private readonly List<StickBone> stickBones = new List<StickBone>();
        private readonly List<Vector3> stickRelationshipLines = new List<Vector3>();
        private readonly List<Transform> stickVisibleBones = new List<Transform>();
        private readonly HashSet<Transform> stickVisibleSet = new HashSet<Transform>();
        private Transform pickedStickBone;
        private Transform hoveredStickBone;

        private static bool UseStickBones => boneDrawStyle == (int)BoneDrawStyle.Stick;

        private void DrawStickBoneSettingsGUI()
        {
            EditorGUI.BeginChangeCheck();
            boneDrawStyle = EditorGUILayout.Popup(new GUIContent("Bone Draw Style", "Line draws a thin line per bone. Stick draws Blender-style batons that can be clicked in the Scene view to select the bone (bones not yet in the pose are added)."), boneDrawStyle, boneDrawStyleNames);
            if (UseStickBones)
            {
                EditorGUI.indentLevel++;
                stickBoneWidth = EditorGUILayout.Slider("Stick Width", stickBoneWidth, StickBoneMinWidth, StickBoneMaxWidth);
                EditorGUI.indentLevel--;
            }
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawStickSkeleton()
        {
            Event evt = Event.current;
            if (evt == null) return;

            // Always claim the id so control ids stay stable across events.
            int controlId = GUIUtility.GetControlID(StickBonePickHash, FocusType.Passive);
            CollectStickBones();
            bool canPick = CanPickStickBones();

            switch (evt.type)
            {
                case EventType.Layout:
                    pickedStickBone = canPick ? LayoutStickBonePicking(controlId, evt.mousePosition) : null;
                    break;

                case EventType.MouseMove:
                    {
                        Transform hover = canPick && HandleUtility.nearestControl == controlId ? pickedStickBone : null;
                        if (hover != hoveredStickBone)
                        {
                            hoveredStickBone = hover;
                            HandleUtility.Repaint();
                        }
                    }
                    break;

                case EventType.MouseDown:
                    if (canPick && evt.button == 0 && !evt.alt && HandleUtility.nearestControl == controlId && pickedStickBone != null)
                    {
                        Transform bone = pickedStickBone;
                        evt.Use();
                        SelectStickBone(bone);
                    }
                    break;

                case EventType.Repaint:
                    hoveredStickBone = canPick && HandleUtility.nearestControl == controlId ? pickedStickBone : null;
                    RenderStickBones();
                    break;
            }
        }

        private bool CanPickStickBones()
        {
            return !useIKEditor
                && !IsEditorBusy
                && targetPose != null
                && serializedObject != null
                && serializedObject.targetObject != null
                && context != null
                && context.activeUMA != null;
        }

        #region Collection

        private void CollectStickBones()
        {
            stickBones.Clear();
            stickRelationshipLines.Clear();
            stickVisibleBones.Clear();
            stickVisibleSet.Clear();

            if (displayMode == (int)DisplayMode.PoseBones)
            {
                CollectPoseStickBones();
            }
            else
            {
                Transform hips = FindSkeletonHips();
                if (hips != null)
                {
                    string filter = displayMode == (int)DisplayMode.Filtered ? BoneListFilter : null;
                    CollectSkeletonStickBonesRecursive(hips, string.IsNullOrEmpty(filter) ? null : filter);
                }
            }

            for (int i = 0; i < stickVisibleBones.Count; i++)
            {
                stickBones.Add(BuildStickBone(stickVisibleBones[i]));
            }
        }

        private Transform FindSkeletonHips()
        {
            if (context == null || context.activeUMA == null || context.activeUMA.umaRoot == null) return null;
            Transform global = context.activeUMA.umaRoot.transform.Find("Global");
            Transform position = global != null ? global.Find("Position") : null;
            return position != null ? position.Find("Hips") : null;
        }

        private void AddStickVisibleBone(Transform bone)
        {
            if (bone != null && stickVisibleSet.Add(bone))
            {
                stickVisibleBones.Add(bone);
            }
        }

        private void CollectPoseStickBones()
        {
            if (sourceUMA == null || sourceUMA.skeleton == null) return;
            if (serializedObject == null || serializedObject.targetObject == null) return;

            SerializedProperty poses = serializedObject.FindProperty("poses");
            if (poses == null) return;

            string filter = BoneListFilter;
            bool useFilter = !string.IsNullOrEmpty(filter);
            for (int i = 0; i < poses.arraySize; i++)
            {
                SerializedProperty bone = poses.GetArrayElementAtIndex(i)?.FindPropertyRelative("bone");
                if (bone == null) continue;

                string boneName = bone.stringValue;
                if (string.IsNullOrEmpty(boneName)) continue;
                if (useFilter && boneName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                AddStickVisibleBone(sourceUMA.skeleton.GetBoneTransform(boneName));
            }
        }

        private void CollectSkeletonStickBonesRecursive(Transform bone, string filter)
        {
            if (bone == null) return;
            if (filter == null || bone.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddStickVisibleBone(bone);
            }
            for (int i = 0; i < bone.childCount; i++)
            {
                CollectSkeletonStickBonesRecursive(bone.GetChild(i), filter);
            }
        }

        private StickBone BuildStickBone(Transform bone)
        {
            StickBone stick = new StickBone { bone = bone, head = bone.position };
            float epsilonSqr = StickBoneEpsilon * StickBoneEpsilon;

            Transform tailChild = null;
            int offsetChildCount = 0;
            Vector3 childSum = Vector3.zero;
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if ((child.position - stick.head).sqrMagnitude <= epsilonSqr) continue;
                offsetChildCount++;
                tailChild = child;
                childSum += child.position;
            }

            if (offsetChildCount == 1)
            {
                stick.tail = tailChild.position;
            }
            else
            {
                tailChild = null;
                Vector3 direction;
                float length;
                Vector3 fromParent = bone.parent != null ? stick.head - bone.parent.position : Vector3.zero;
                float parentDistance = fromParent.magnitude;
                if (parentDistance > StickBoneEpsilon)
                {
                    direction = fromParent / parentDistance;
                    length = parentDistance * 0.5f;
                }
                else if (offsetChildCount > 1)
                {
                    Vector3 toChildren = childSum / offsetChildCount - stick.head;
                    float childDistance = toChildren.magnitude;
                    if (childDistance <= StickBoneEpsilon)
                    {
                        direction = Vector3.zero;
                        length = 0f;
                    }
                    else
                    {
                        direction = toChildren / childDistance;
                        length = childDistance * 0.5f;
                    }
                }
                else
                {
                    direction = Vector3.zero;
                    length = 0f;
                }

                if (direction == Vector3.zero)
                {
                    stick.isPoint = true;
                    stick.tail = stick.head;
                }
                else
                {
                    stick.tail = stick.head + direction * Mathf.Clamp(length, StickStubMinLength, StickStubMaxLength);
                }
            }

            // Dashed relationship lines to visible children that are not this bone's tail.
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (child == tailChild || !stickVisibleSet.Contains(child)) continue;
                if ((child.position - stick.head).sqrMagnitude <= epsilonSqr) continue;
                stickRelationshipLines.Add(stick.head);
                stickRelationshipLines.Add(child.position);
            }

            return stick;
        }

        #endregion

        #region Picking

        private static float GetStickPixelsToWorld(Vector3 position)
        {
            // GetHandleSize returns the world size of 80 GUI points at this position.
            return HandleUtility.GetHandleSize(position) / 80f;
        }

        private static float GetStickCapRadius(Vector3 position)
        {
            return GetStickPixelsToWorld(position) * Mathf.Max(2f, stickBoneWidth * 0.75f + 1f);
        }

        private Transform LayoutStickBonePicking(int controlId, Vector2 mousePosition)
        {
            float halfWidth = stickBoneWidth * 0.5f;
            float bestDistance = float.MaxValue;
            float bestHeadDistance = float.MaxValue;
            Transform bestBone = null;

            for (int i = 0; i < stickBones.Count; i++)
            {
                StickBone stick = stickBones[i];
                float distance;
                if (stick.isPoint)
                {
                    distance = Mathf.Max(0f, HandleUtility.DistanceToCircle(stick.head, GetStickCapRadius(stick.head) * 1.6f));
                }
                else
                {
                    // Small bias so a ring sharing a joint with a stick wins at the joint itself.
                    distance = Mathf.Max(0f, HandleUtility.DistanceToLine(stick.head, stick.tail) - halfWidth) + 0.5f;
                }

                // Ties (e.g. at a joint where a parent's tail meets a child's head) go to the bone whose head is nearest.
                float headDistance = Vector2.Distance(HandleUtility.WorldToGUIPoint(stick.head), mousePosition);
                bool better = distance < bestDistance - 0.01f
                    || (Mathf.Abs(distance - bestDistance) <= 0.01f && headDistance < bestHeadDistance);
                if (better)
                {
                    bestDistance = distance;
                    bestHeadDistance = headDistance;
                    bestBone = stick.bone;
                }
            }

            if (bestBone != null)
            {
                HandleUtility.AddControl(controlId, bestDistance);
            }
            return bestBone;
        }

        private static int FindPoseIndexByBoneName(SerializedProperty poses, string boneName)
        {
            if (poses == null || string.IsNullOrEmpty(boneName)) return BAD_INDEX;
            for (int i = 0; i < poses.arraySize; i++)
            {
                SerializedProperty bone = poses.GetArrayElementAtIndex(i)?.FindPropertyRelative("bone");
                if (bone != null && bone.stringValue == boneName) return i;
            }
            return BAD_INDEX;
        }

        private void SelectStickBone(Transform bone)
        {
            if (bone == null || serializedObject == null || serializedObject.targetObject == null) return;

            serializedObject.Update();
            SerializedProperty poses = serializedObject.FindProperty("poses");
            if (poses == null) return;

            int index = FindPoseIndexByBoneName(poses, bone.name);
            if (index == BAD_INDEX)
            {
                Undo.SetCurrentGroupName("Add Pose Bone");
                AddABone(poses, bone.name);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                index = FindPoseIndexByBoneName(poses, bone.name);
                if (index == BAD_INDEX) return;
            }

            SerializedProperty boneProperty = poses.GetArrayElementAtIndex(index).FindPropertyRelative("bone");
            if (boneProperty != null)
            {
                boneProperty.isExpanded = true;
            }

            Tool tool = Tools.current == Tool.Rotate || Tools.current == Tool.Scale ? Tools.current : Tool.Move;
            SelectBoneTool(index, tool);
        }

        #endregion

        #region Rendering

        private Color GetStickBoneColor(Transform bone)
        {
            if (context != null && bone == context.activeTransform) return StickActiveColor;
            if (context != null && bone == context.mirrorTransform) return StickMirrorColor;
            if (bone == hoveredStickBone) return StickHoverColor;
            return IsAdjustBone(bone.name) ? StickAdjustBoneColor : StickBoneColor;
        }

        private bool IsStickBoneHighlighted(Transform bone)
        {
            return bone == hoveredStickBone
                || (context != null && (bone == context.activeTransform || bone == context.mirrorTransform));
        }

        private void RenderStickBones()
        {
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                Camera camera = Camera.current;
                Vector3 viewNormal = camera != null ? camera.transform.forward : Vector3.forward;

                Handles.color = StickRelationshipColor;
                for (int i = 0; i + 1 < stickRelationshipLines.Count; i += 2)
                {
                    Handles.DrawDottedLine(stickRelationshipLines[i], stickRelationshipLines[i + 1], 3f);
                }

                // Plain bones first, highlighted (active / mirror / hovered) on top.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool highlightedPass = pass == 1;
                    for (int i = 0; i < stickBones.Count; i++)
                    {
                        StickBone stick = stickBones[i];
                        if (stick.bone == null || IsStickBoneHighlighted(stick.bone) != highlightedPass) continue;
                        DrawStickBone(stick, GetStickBoneColor(stick.bone), viewNormal);
                    }
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
                Handles.color = previousColor;
            }
        }

        private static void DrawStickBone(StickBone stick, Color fill, Vector3 viewNormal)
        {
            if (stick.isPoint)
            {
                float ringRadius = GetStickCapRadius(stick.head) * 1.6f;
                Handles.color = StickOutlineColor;
                Handles.DrawWireDisc(stick.head, viewNormal, ringRadius, 4f);
                Handles.color = fill;
                Handles.DrawWireDisc(stick.head, viewNormal, ringRadius, 2f);
                return;
            }

            stickSegmentBuffer[0] = stick.head;
            stickSegmentBuffer[1] = stick.tail;
            Handles.color = StickOutlineColor;
            Handles.DrawAAPolyLine(stickBoneWidth + 2f, stickSegmentBuffer);
            Handles.color = fill;
            Handles.DrawAAPolyLine(stickBoneWidth, stickSegmentBuffer);

            DrawStickCap(stick.tail, fill, viewNormal);
            DrawStickCap(stick.head, fill, viewNormal);
        }

        private static void DrawStickCap(Vector3 position, Color fill, Vector3 viewNormal)
        {
            float radius = GetStickCapRadius(position);
            float outline = GetStickPixelsToWorld(position);
            Handles.color = StickOutlineColor;
            Handles.DrawSolidDisc(position, viewNormal, radius + outline);
            Handles.color = fill;
            Handles.DrawSolidDisc(position, viewNormal, radius);
        }

        #endregion
    }
}
