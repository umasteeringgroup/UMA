using System;
using System.Collections.Generic;
using System.Diagnostics;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    internal enum UMASelectedRebuildMode
    {
        Full,
        RigOnly,
        MeshOnly,
        TexturesOnly
    }

    /// <summary>
    /// Shared implementation for the UMA Scene View toolbar. Keeping selection,
    /// rebuild, and combiner logic here also makes the individual toolbar elements
    /// small and keeps their behavior consistent.
    /// </summary>
    internal static class UMAToolbarActions
    {
        private sealed class BuildTiming
        {
            public string description;
            public double milliseconds;
        }

        private static readonly Dictionary<int, Action<UMAData>> PendingBuildHandlers =
            new Dictionary<int, Action<UMAData>>();

        private static readonly Dictionary<int, BuildTiming> LastBuildTimings =
            new Dictionary<int, BuildTiming>();

        internal static event Action DiagnosticsChanged;

        internal static List<DynamicCharacterAvatar> GetSelectedAvatars()
        {
            var result = new List<DynamicCharacterAvatar>();
            var found = new HashSet<DynamicCharacterAvatar>();
            GameObject[] selectedObjects = Selection.gameObjects;

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject selected = selectedObjects[i];
                if (selected == null)
                {
                    continue;
                }

                DynamicCharacterAvatar parentAvatar = selected.GetComponentInParent<DynamicCharacterAvatar>(true);
                if (parentAvatar != null)
                {
                    if (found.Add(parentAvatar))
                    {
                        result.Add(parentAvatar);
                    }
                    continue;
                }

                DynamicCharacterAvatar[] childAvatars =
                    selected.GetComponentsInChildren<DynamicCharacterAvatar>(true);
                for (int avatarIndex = 0; avatarIndex < childAvatars.Length; avatarIndex++)
                {
                    DynamicCharacterAvatar avatar = childAvatars[avatarIndex];
                    if (avatar != null && found.Add(avatar))
                    {
                        result.Add(avatar);
                    }
                }
            }

            return result;
        }

        internal static DynamicCharacterAvatar GetActiveAvatar()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return null;
            }

            DynamicCharacterAvatar avatar = selected.GetComponentInParent<DynamicCharacterAvatar>(true);
            return avatar != null
                ? avatar
                : selected.GetComponentInChildren<DynamicCharacterAvatar>(true);
        }

        internal static void RebuildSelected(UMASelectedRebuildMode mode)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[UMA Toolbar] Selected rebuilds are only available in Edit Mode.");
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogWarning("[UMA Toolbar] Unity is compiling or updating assets. Try the rebuild again when it finishes.");
                return;
            }

            List<DynamicCharacterAvatar> avatars = GetSelectedAvatars();
            if (avatars.Count == 0)
            {
                Debug.LogWarning("[UMA Toolbar] Select a DynamicCharacterAvatar, one of its children, or a parent containing UMAs.");
                return;
            }

            string description = GetRebuildModeLabel(mode);
            int rebuilt = 0;
            for (int i = 0; i < avatars.Count; i++)
            {
                DynamicCharacterAvatar avatar = avatars[i];
                if (avatar == null)
                {
                    continue;
                }

                BeginBuildTiming(avatar, description);
                switch (mode)
                {
                    case UMASelectedRebuildMode.RigOnly:
                        avatar.RegenerateNow(true, false, false, true);
                        break;
                    case UMASelectedRebuildMode.MeshOnly:
                        avatar.RegenerateNow(false, false, true, true);
                        break;
                    case UMASelectedRebuildMode.TexturesOnly:
                        avatar.RegenerateNow(false, true, false, true);
                        break;
                    default:
                        avatar.GenerateSingleUMA(false, true);
                        break;
                }
                rebuilt++;
            }

            Debug.Log($"[UMA Toolbar] Requested {description.ToLowerInvariant()} for {rebuilt} selected UMA{(rebuilt == 1 ? string.Empty : "s")}.");
        }

        internal static string GetRebuildModeLabel(UMASelectedRebuildMode mode)
        {
            switch (mode)
            {
                case UMASelectedRebuildMode.RigOnly:
                    return "Rig-only rebuild";
                case UMASelectedRebuildMode.MeshOnly:
                    return "Mesh-only rebuild";
                case UMASelectedRebuildMode.TexturesOnly:
                    return "Texture-only rebuild";
                default:
                    return "Full rebuild";
            }
        }

        private static void BeginBuildTiming(DynamicCharacterAvatar avatar, string description)
        {
            int instanceId = avatar.GetInstanceID();
            Action<UMAData> previousHandler;
            if (PendingBuildHandlers.TryGetValue(instanceId, out previousHandler))
            {
                avatar.OnCharacterUpdated -= previousHandler;
                PendingBuildHandlers.Remove(instanceId);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Action<UMAData> completedHandler = null;
            completedHandler = updatedAvatar =>
            {
                stopwatch.Stop();
                if (updatedAvatar != null)
                {
                    updatedAvatar.OnCharacterUpdated -= completedHandler;
                }
                PendingBuildHandlers.Remove(instanceId);
                LastBuildTimings[instanceId] = new BuildTiming
                {
                    description = description,
                    milliseconds = stopwatch.Elapsed.TotalMilliseconds
                };
                DiagnosticsChanged?.Invoke();
            };

            PendingBuildHandlers[instanceId] = completedHandler;
            avatar.OnCharacterUpdated += completedHandler;
        }

        internal static bool TryGetLastBuildTiming(
            DynamicCharacterAvatar avatar,
            out string description,
            out double milliseconds)
        {
            description = null;
            milliseconds = 0d;
            if (avatar == null)
            {
                return false;
            }

            BuildTiming timing;
            if (!LastBuildTimings.TryGetValue(avatar.GetInstanceID(), out timing))
            {
                return false;
            }

            description = timing.description;
            milliseconds = timing.milliseconds;
            return true;
        }

        internal static UMAGenerator GetGenerator()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            return indexer != null ? indexer.generator : null;
        }

        internal static string GetCurrentCombinerName(UMAGenerator generator)
        {
            UMAMeshCombiner combiner = generator != null ? generator.meshCombiner : null;
            if (combiner is UMAJobifiedMeshCombiner)
            {
                return "Jobified";
            }
            if (combiner != null && combiner.GetType() == typeof(UMADefaultBoneBakingMeshCombiner))
            {
                return "Default Bone Baking";
            }
            if (combiner != null && combiner.GetType() == typeof(UMABoneBakingMeshCombiner))
            {
                return "Bone Baking Compatibility";
            }
            if (combiner is UMADefaultBoneBakingMeshCombiner)
            {
                return "Default Bone Baking";
            }
            if (combiner is UMADefaultMeshCombiner)
            {
                return "Default";
            }
            return combiner == null ? "None" : combiner.GetType().Name;
        }

        internal static bool IsCurrentCombiner<T>(UMAGenerator generator) where T : UMAMeshCombiner
        {
            return generator != null && generator.meshCombiner != null &&
                   generator.meshCombiner.GetType() == typeof(T);
        }

        internal static void UseMeshCombiner<T>(UMAGenerator generator) where T : UMAMeshCombiner
        {
            if (generator == null)
            {
                Debug.LogWarning("[UMA Toolbar] No UMA generator is assigned in the Global Library.");
                return;
            }

            if (IsCurrentCombiner<T>(generator))
            {
                return;
            }

            T meshCombiner = null;
            T[] candidates = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].GetType() == typeof(T))
                {
                    meshCombiner = candidates[i];
                    break;
                }
            }

            if (meshCombiner == null)
            {
                GameObject combinerObject = new GameObject(typeof(T).Name);
                if (Application.isPlaying)
                {
                    if (generator.transform.parent != null)
                    {
                        combinerObject.transform.SetParent(generator.transform.parent, false);
                    }
                    meshCombiner = combinerObject.AddComponent<T>();
                }
                else
                {
                    Undo.RegisterCreatedObjectUndo(combinerObject, "Create UMA Mesh Combiner");
                    if (generator.transform.parent != null)
                    {
                        Undo.SetTransformParent(combinerObject.transform, generator.transform.parent, "Parent UMA Mesh Combiner");
                    }
                    meshCombiner = Undo.AddComponent<T>(combinerObject);
                }
            }

            if (!Application.isPlaying)
            {
                Undo.RecordObject(generator, "Switch UMA Mesh Combiner");
            }

            generator.meshCombiner = meshCombiner;

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(generator);
                if (PrefabUtility.IsPartOfAnyPrefab(generator))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
                }
            }

            Debug.Log($"[UMA Toolbar] Mesh combiner switched to {typeof(T).Name}. Rebuild characters to apply it.");
            DiagnosticsChanged?.Invoke();
        }

        internal static Transform GetHips(DynamicCharacterAvatar avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            Transform hips = avatar.skeleton != null ? avatar.skeleton.GetBoneTransform("Hips") : null;
            if (hips == null && avatar.animator != null && avatar.animator.isHuman)
            {
                hips = avatar.animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            return hips;
        }

        internal static Transform GetRootBone(DynamicCharacterAvatar avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            SkinnedMeshRenderer[] renderers = avatar.GetRenderers();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].rootBone != null)
                    {
                        return renderers[i].rootBone;
                    }
                }
            }

            return avatar.skeleton != null ? avatar.skeleton.GetRootTransform() : null;
        }

        internal static SkinnedMeshRenderer GetFirstRenderer(DynamicCharacterAvatar avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            SkinnedMeshRenderer[] renderers = avatar.GetRenderers();
            if (renderers == null)
            {
                return null;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    return renderers[i];
                }
            }
            return null;
        }

        internal static void SelectAndFrame(Object target)
        {
            if (target == null)
            {
                return;
            }

            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.FrameSelected();
                sceneView.Repaint();
            }
        }
    }

    /// <summary>
    /// Draws the selected UMA skeleton directly from editor state. Unlike the
    /// UMABoneVisualizer helper this does not add a component to the scene.
    /// </summary>
    [InitializeOnLoad]
    internal static class UMAToolbarSkeletonRenderer
    {
        private const string ShowSkeletonSessionKey = "UMA.Toolbar.ShowSelectedSkeleton";
        private const string ShowBoneNamesSessionKey = "UMA.Toolbar.ShowSelectedBoneNames";

        static UMAToolbarSkeletonRenderer()
        {
            SceneView.duringSceneGui -= DrawSelectedSkeleton;
            SceneView.duringSceneGui += DrawSelectedSkeleton;
            Selection.selectionChanged -= RepaintSceneViews;
            Selection.selectionChanged += RepaintSceneViews;
        }

        internal static bool ShowSkeleton
        {
            get { return SessionState.GetBool(ShowSkeletonSessionKey, false); }
            set
            {
                SessionState.SetBool(ShowSkeletonSessionKey, value);
                SceneView.RepaintAll();
            }
        }

        internal static bool ShowBoneNames
        {
            get { return SessionState.GetBool(ShowBoneNamesSessionKey, false); }
            set
            {
                SessionState.SetBool(ShowBoneNamesSessionKey, value);
                SceneView.RepaintAll();
            }
        }

        private static void RepaintSceneViews()
        {
            if (ShowSkeleton)
            {
                SceneView.RepaintAll();
            }
        }

        private static void DrawSelectedSkeleton(SceneView sceneView)
        {
            if (!ShowSkeleton || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            DynamicCharacterAvatar avatar = UMAToolbarActions.GetActiveAvatar();
            if (avatar == null || avatar.skeleton == null || avatar.skeleton.boneHashData == null)
            {
                return;
            }

            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                foreach (UMASkeleton.BoneData bone in avatar.skeleton.boneHashData.Values)
                {
                    if (bone == null || bone.boneTransform == null)
                    {
                        continue;
                    }

                    UMASkeleton.BoneData parentBone;
                    Transform parentTransform = avatar.skeleton.boneHashData.TryGetValue(
                        bone.parentBoneNameHash,
                        out parentBone) && parentBone != null
                        ? parentBone.boneTransform
                        : null;

                    Transform boneTransform = bone.boneTransform;
                    Color color = boneTransform == Selection.activeTransform
                        ? Color.yellow
                        : parentTransform == null ? Color.green : new Color(0.1f, 0.65f, 1f, 1f);
                    float handleSize = HandleUtility.GetHandleSize(boneTransform.position);

                    using (new Handles.DrawingScope(color))
                    {
                        if (parentTransform != null)
                        {
                            Handles.DrawAAPolyLine(2f, parentTransform.position, boneTransform.position);
                        }
                        Handles.SphereHandleCap(
                            0,
                            boneTransform.position,
                            Quaternion.identity,
                            Mathf.Max(0.001f, handleSize * 0.015f),
                            EventType.Repaint);
                        if (ShowBoneNames)
                        {
                            Handles.Label(boneTransform.position, boneTransform.name);
                        }
                    }
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }
    }
}
