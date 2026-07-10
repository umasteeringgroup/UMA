using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace UMA
{
    [CustomEditor(typeof(UMABoneVisualizer))]
    public class UMABoneVisualizerEditor : Editor
    {
        static GUIContent Warning = new GUIContent("This is a helper component and should be removed before your final build. It has no runtime functionality.");
        public override void OnInspectorGUI()
        {
            Rect labelRect = GUILayoutUtility.GetRect(Warning, "box");
            GUI.Box(labelRect, Warning);
            DrawDefaultInspector();
        }
    }

    [InitializeOnLoad]
    public static class UMABoneVisualizerSceneRenderer
    {
        static UMABoneVisualizerSceneRenderer()
        {
            SceneView.duringSceneGui -= DrawVisualizers;
            SceneView.duringSceneGui += DrawVisualizers;
        }

        private static void DrawVisualizers(SceneView sceneView)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            UMABoneVisualizer[] visualizers = UnityEngine.Object.FindObjectsByType<UMABoneVisualizer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (visualizers == null || visualizers.Length == 0)
            {
                return;
            }

            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            try
            {
                for (int i = 0; i < visualizers.Length; i++)
                {
                    UMABoneVisualizer visualizer = visualizers[i];
                    if (visualizer == null || !visualizer.enabled || !visualizer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    DrawVisualizer(visualizer);
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private static void DrawVisualizer(UMABoneVisualizer visualizer)
        {
            UMAData umaData = ResolveUMAData(visualizer);
            if (umaData == null || umaData.skeleton == null)
            {
                return;
            }

            foreach (UMASkeleton.BoneData bone in umaData.skeleton.boneHashData.Values)
            {
                DrawBone(visualizer, umaData, bone);
            }
        }

        private static UMAData ResolveUMAData(UMABoneVisualizer visualizer)
        {
            if (visualizer.umaData != null)
            {
                return visualizer.umaData;
            }

            UMAData umaData = visualizer.GetComponent<UMAData>();
            if (umaData == null)
            {
                umaData = visualizer.GetComponentInParent<UMAData>();
            }
            if (umaData == null)
            {
                umaData = visualizer.GetComponentInChildren<UMAData>();
            }

            return umaData;
        }

        private static void DrawBone(UMABoneVisualizer visualizer, UMAData umaData, UMASkeleton.BoneData bone)
        {
            if (bone == null || bone.boneTransform == null || !ShouldDrawBone(visualizer, bone.boneTransform))
            {
                return;
            }

            Transform boneTransform = bone.boneTransform;
            Transform parentTransform = GetParentTransform(umaData, bone);
            Color color = GetBoneColor(visualizer, boneTransform, parentTransform == null);
            float handleSize = HandleUtility.GetHandleSize(boneTransform.position);
            float jointSize = Mathf.Max(0.001f, visualizer.JointSize * handleSize);

            using (new Handles.DrawingScope(color))
            {
                if (parentTransform != null)
                {
                    Handles.DrawAAPolyLine(Mathf.Max(1f, visualizer.LineThickness), parentTransform.position, boneTransform.position);
                }

                Handles.SphereHandleCap(0, boneTransform.position, Quaternion.identity, jointSize, EventType.Repaint);

                if (visualizer.DrawBoneNames)
                {
                    Handles.Label(boneTransform.position, boneTransform.name);
                }
            }
        }

        private static Transform GetParentTransform(UMAData umaData, UMASkeleton.BoneData bone)
        {
            if (umaData == null || umaData.skeleton == null || bone == null)
            {
                return null;
            }

            UMASkeleton.BoneData parentBone;
            if (umaData.skeleton.boneHashData.TryGetValue(bone.parentBoneNameHash, out parentBone) && parentBone != null)
            {
                return parentBone.boneTransform;
            }

            return bone.boneTransform != null ? bone.boneTransform.parent : null;
        }

        private static bool ShouldDrawBone(UMABoneVisualizer visualizer, Transform boneTransform)
        {
            if (boneTransform == null)
            {
                return false;
            }

            if (!visualizer.DrawAdjustBones && boneTransform.name.ToLowerInvariant().Contains("adjust"))
            {
                return false;
            }

            return string.IsNullOrEmpty(visualizer.Filter) || boneTransform.name.ToLowerInvariant().Contains(visualizer.Filter.ToLowerInvariant());
        }

        private static Color GetBoneColor(UMABoneVisualizer visualizer, Transform boneTransform, bool isRoot)
        {
            if (boneTransform == Selection.activeTransform)
            {
                return visualizer.SelectedBoneColor;
            }

            return isRoot ? visualizer.RootBoneColor : visualizer.BoneColor;
        }
    }
}