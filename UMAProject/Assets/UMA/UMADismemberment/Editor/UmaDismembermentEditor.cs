using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Dismemberment
{
    [CustomEditor(typeof(UmaDismemberment))]
    public sealed class UmaDismembermentEditor : UnityEditor.Editor
    {
        private SerializedProperty useEvents;
        private SerializedProperty legacyEvent;
        private SerializedProperty completedEvent;
        private SerializedProperty sliceFill;
        private SerializedProperty pipelineOverrides;
        private SerializedProperty generateCaps;
        private SerializedProperty requireClosedCaps;
        private SerializedProperty capUvMetersPerTile;
        private SerializedProperty seamWeldTolerance;
        private SerializedProperty globalThreshold;
        private SerializedProperty useSliceable;
        private SerializedProperty sliceableHumanBones;
        private SerializedProperty includeChildBones;
        private SerializedProperty rebuildPolicy;
        private ReorderableList sliceableList;

        private void OnEnable()
        {
            useEvents = serializedObject.FindProperty("useEvents");
            legacyEvent = serializedObject.FindProperty("DismemberedEvent");
            completedEvent = serializedObject.FindProperty("DismembermentCompleted");
            sliceFill = serializedObject.FindProperty("sliceFill");
            pipelineOverrides = serializedObject.FindProperty("pipelineSliceFillOverrides");
            generateCaps = serializedObject.FindProperty("generateCaps");
            requireClosedCaps = serializedObject.FindProperty("requireClosedCaps");
            capUvMetersPerTile = serializedObject.FindProperty("capUvMetersPerTile");
            seamWeldTolerance = serializedObject.FindProperty("seamWeldTolerance");
            globalThreshold = serializedObject.FindProperty("globalThreshold");
            useSliceable = serializedObject.FindProperty("useSliceable");
            sliceableHumanBones = serializedObject.FindProperty("sliceableHumanBones");
            includeChildBones = serializedObject.FindProperty("includeChildBones");
            rebuildPolicy = serializedObject.FindProperty("rebuildPolicy");

            sliceableList = new ReorderableList(serializedObject, sliceableHumanBones,
                true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect,
                    "Sliceable Human Bones, Cap UV and Physics"),
                drawElementCallback = DrawBoneElement,
                elementHeightCallback = GetBoneElementHeight,
                onAddCallback = AddBone
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "UMA 3 runtime slicer. Generated UMA meshes are never modified directly; owned " +
                "clones are restored before avatar regeneration.", MessageType.Info);

            EditorGUILayout.LabelField("Cap", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(generateCaps);
            using (new EditorGUI.DisabledScope(!generateCaps.boolValue))
            {
                EditorGUILayout.PropertyField(sliceFill, new GUIContent("Fallback Material"));
                EditorGUILayout.PropertyField(pipelineOverrides, new GUIContent("Pipeline Overrides"), true);
                EditorGUILayout.PropertyField(requireClosedCaps);
                EditorGUILayout.PropertyField(capUvMetersPerTile);
                EditorGUILayout.PropertyField(seamWeldTolerance);
            }
            DrawCapDiagnostics();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bone Selection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(globalThreshold);
            EditorGUILayout.PropertyField(includeChildBones);
            EditorGUILayout.PropertyField(useSliceable);
            using (new EditorGUI.DisabledScope(!useSliceable.boolValue))
                sliceableList.DoLayoutList();
            DrawBoneDiagnostics();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lifecycle", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rebuildPolicy);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useEvents, new GUIContent("Invoke Legacy Event"));
            using (new EditorGUI.DisabledScope(!useEvents.boolValue))
                EditorGUILayout.PropertyField(legacyEvent);
            EditorGUILayout.PropertyField(completedEvent);

            serializedObject.ApplyModifiedProperties();
            UmaDismemberment component = (UmaDismemberment)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Ready", component.IsReady ? "Yes" : "No");
                if (!string.IsNullOrEmpty(component.LastFailure))
                    EditorGUILayout.HelpBox($"{component.LastFailureReason}: {component.LastFailure}",
                        MessageType.Warning);
                if (GUILayout.Button("Undo Dismemberment"))
                {
                    if (!component.TryUndoDismemberment(out string failure))
                        Debug.LogWarning($"Could not completely undo dismemberment: {failure}",
                            component);
                }
            }
        }

        private void DrawCapDiagnostics()
        {
            if (!generateCaps.boolValue) return;
            UmaDismemberment component = (UmaDismemberment)target;
            Material resolved = component.ResolveSliceFillMaterial();
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineName = pipeline != null ? pipeline.name : "Built-in Render Pipeline";
            if (resolved == null)
                EditorGUILayout.HelpBox($"No cap material resolves for {pipelineName}.", MessageType.Error);
            else if (resolved.shader == null || !resolved.shader.isSupported)
                EditorGUILayout.HelpBox($"'{resolved.name}' is not supported by {pipelineName}.",
                    MessageType.Error);
        }

        private void DrawBoneDiagnostics()
        {
            if (!useSliceable.boolValue) return;
            var seen = new HashSet<int>();
            for (int i = 0; i < sliceableHumanBones.arraySize; i++)
            {
                SerializedProperty bone = sliceableHumanBones.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("humanBone");
                if (!seen.Add(bone.enumValueIndex))
                {
                    EditorGUILayout.HelpBox("Sliceable Human Bones contains duplicate entries.",
                        MessageType.Warning);
                    return;
                }
            }
        }

        private void DrawBoneElement(Rect rect, int index, bool active, bool focused)
        {
            SerializedProperty element = sliceableHumanBones.GetArrayElementAtIndex(index);
            SerializedProperty bone = element.FindPropertyRelative("humanBone");
            SerializedProperty threshold = element.FindPropertyRelative("threshold");
            SerializedProperty capUvMode = element.FindPropertyRelative("capUvMode");
            SerializedProperty centeredPadding = element.FindPropertyRelative(
                "centeredCapUvPadding");
            SerializedProperty physicsDefinitions = element.FindPropertyRelative(
                "physicsDefinitions");
            SerializedProperty physicsMode = element.FindPropertyRelative("physicsMode");
            SerializedProperty trimRig = element.FindPropertyRelative("trimDetachedRig");
            SerializedProperty ragdollMainBody = element.FindPropertyRelative(
                "ragdollMainBody");
            rect.y += EditorGUIUtility.standardVerticalSpacing;
            float gap = 6f;
            float boneWidth = Mathf.Max(100f, rect.width * 0.58f);
            Rect boneRect = new Rect(rect.x, rect.y, boneWidth, EditorGUIUtility.singleLineHeight);
            Rect thresholdRect = new Rect(rect.x + boneWidth + gap, rect.y,
                Mathf.Max(40f, rect.width - boneWidth - gap), EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(boneRect, bone, GUIContent.none);
            EditorGUI.PropertyField(thresholdRect, threshold, GUIContent.none);
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect uvModeRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(uvModeRect, capUvMode, new GUIContent("Cap UV Mapping"));
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect paddingRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(capUvMode.enumValueIndex !=
                (int)DismembermentCapUvMode.CenteredFit))
            {
                EditorGUI.PropertyField(paddingRect, centeredPadding,
                    new GUIContent("Centered UV Padding"));
            }
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect physicsModeRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(physicsModeRect, physicsMode,
                new GUIContent("Detached Physics Mode"));
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect trimRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(trimRect, trimRig,
                new GUIContent("Trim Detached Rig"));
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect mainBodyRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(mainBodyRect, ragdollMainBody,
                new GUIContent("Ragdoll Main Body",
                    "After a successful cut, activate the character's UMAPhysicsAvatar ragdoll."));
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect physicsRect = new Rect(rect.x, rect.y, rect.width,
                EditorGUI.GetPropertyHeight(physicsDefinitions, true));
            EditorGUI.PropertyField(physicsRect, physicsDefinitions,
                new GUIContent("Detached Physics Definitions"), true);
        }

        private float GetBoneElementHeight(int index)
        {
            SerializedProperty element = sliceableHumanBones.GetArrayElementAtIndex(index);
            SerializedProperty physicsDefinitions = element.FindPropertyRelative(
                "physicsDefinitions");
            return EditorGUIUtility.singleLineHeight * 6f +
                EditorGUI.GetPropertyHeight(physicsDefinitions, true) +
                EditorGUIUtility.standardVerticalSpacing * 8f;
        }

        private void AddBone(ReorderableList list)
        {
            int index = sliceableHumanBones.arraySize;
            sliceableHumanBones.InsertArrayElementAtIndex(index);
            SerializedProperty element = sliceableHumanBones.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("humanBone").enumValueIndex = FindUnusedBone();
            element.FindPropertyRelative("threshold").floatValue = 0.5f;
            element.FindPropertyRelative("capUvMode").enumValueIndex =
                (int)DismembermentCapUvMode.MeterScaledTiled;
            element.FindPropertyRelative("centeredCapUvPadding").floatValue =
                UmaDismemberment.DefaultCenteredCapUvPadding;
            element.FindPropertyRelative("physicsDefinitions").arraySize = 0;
            element.FindPropertyRelative("physicsMode").enumValueIndex =
                (int)DismemberedPhysicsMode.Automatic;
            element.FindPropertyRelative("trimDetachedRig").boolValue = false;
            element.FindPropertyRelative("ragdollMainBody").boolValue = false;
            list.index = index;
        }

        private int FindUnusedBone()
        {
            var used = new HashSet<int>();
            for (int i = 0; i < sliceableHumanBones.arraySize - 1; i++)
                used.Add(sliceableHumanBones.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("humanBone").enumValueIndex);
            string[] names = System.Enum.GetNames(typeof(HumanBodyBones));
            for (int i = 0; i < names.Length - 1; i++) if (!used.Contains(i)) return i;
            return 0;
        }
    }
}
