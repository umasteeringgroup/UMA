using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    [CustomEditor(typeof(TexturePaintRegionProfile))]
    public sealed class TexturePaintRegionProfileEditor : UnityEditor.Editor
    {
        private readonly HashSet<string> boneFoldouts = new HashSet<string>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Shared envelope settings refresh all layers using this profile. Side is selected on each layer. X/Z size is width/depth; Y is length along the bone. Offsets are multiples of the bone segment length.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            var regions = serializedObject.FindProperty("regions");
            var seen = new HashSet<int>();
            bool duplicates = false;
            int remove = -1;
            for (int i = 0; i < regions.arraySize; i++)
            {
                var entry = regions.GetArrayElementAtIndex(i);
                var region = entry.FindPropertyRelative("region");
                var kind = (TexturePaintAnatomicalRegion)region.intValue;
                if (!seen.Add(region.intValue)) duplicates = true;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, ObjectNames.NicifyVariableName(kind.ToString()), true);
                        if (GUILayout.Button("Remove", GUILayout.Width(64))) remove = i;
                    }
                    if (!entry.isExpanded) continue;
                    EditorGUILayout.PropertyField(region);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("size"), new GUIContent("Envelope Size"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("offset"), new GUIContent("Envelope Offset"));
                    EditorGUILayout.Slider(entry.FindPropertyRelative("rotation"), -180, 180, new GUIContent("Around Bone"));
                    EditorGUILayout.Slider(entry.FindPropertyRelative("feather"), .001f, 1, new GUIContent("Edge Feather"));
                    if (kind == TexturePaintAnatomicalRegion.Knees || kind == TexturePaintAnatomicalRegion.Elbows)
                        EditorGUILayout.Slider(entry.FindPropertyRelative("jointArc"), 1, 360, new GUIContent("Joint Coverage (degrees)"));
                    string key = entry.propertyPath;
                    bool bones = EditorGUILayout.Foldout(boneFoldouts.Contains(key), "Advanced: Bone Overrides", true);
                    if (bones) boneFoldouts.Add(key); else boneFoldouts.Remove(key);
                    if (bones)
                    {
                        Field("leftAnchor", "Left / Center Anchor"); Field("leftParent", "Left / Center Parent"); Field("leftChild", "Left / Center Child");
                        if (TexturePaintAnatomicalSettings.IsPaired(kind))
                        { Field("rightAnchor", "Right Anchor"); Field("rightParent", "Right Parent"); Field("rightChild", "Right Child"); }
                    }
                    void Field(string name, string label) => EditorGUILayout.PropertyField(entry.FindPropertyRelative(name), new GUIContent(label));
                }
            }
            if (duplicates) EditorGUILayout.HelpBox("Duplicate regions: only the first entry for each region is used. Remove duplicates to avoid editing an unused entry.", MessageType.Warning);
            if (remove >= 0) regions.DeleteArrayElementAtIndex(remove);
            serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("Add Missing Default Regions"))
            {
                var profile = (TexturePaintRegionProfile)target;
                Undo.RecordObject(profile, "Add Missing Regions");
                profile.regions ??= new List<TexturePaintAnatomicalSettings>();
                foreach (TexturePaintAnatomicalRegion kind in Enum.GetValues(typeof(TexturePaintAnatomicalRegion)))
                    if (profile.Get(kind) == null) profile.regions.Add(new TexturePaintAnatomicalSettings { region = kind });
                EditorUtility.SetDirty(profile);
            }
        }
    }
}
