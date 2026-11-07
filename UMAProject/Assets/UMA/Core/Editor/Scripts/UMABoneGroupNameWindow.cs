using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.PoseTools
{
    public sealed class UMABoneGroupNameWindow : EditorWindow
    {
        [SerializeField] private UMABonePose pose;
        [SerializeField] private string[] selectedBones;
        [SerializeField] private string groupName = "";
        private Action<string> completed;
        private string error;
        private bool focusName = true;

        public static void Open(UMABonePose pose, IEnumerable<string> bones, Action<string> completed)
        {
            var window = CreateInstance<UMABoneGroupNameWindow>();
            window.titleContent = new GUIContent("Create bone group");
            window.pose = pose;
            window.selectedBones = bones.ToArray();
            window.completed = completed;
            window.minSize = new Vector2(360, 150);
            window.maxSize = new Vector2(600, 200);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (pose == null) { Close(); return; }
            EditorGUILayout.LabelField("Group name", EditorStyles.boldLabel);
            GUI.SetNextControlName("BoneGroupName");
            groupName = EditorGUILayout.TextField(groupName);
            if (focusName) { EditorGUI.FocusTextInControl("BoneGroupName"); focusName = false; }
            bool replaces = pose.boneGroups != null && pose.boneGroups.Any(g => g != null &&
                string.Equals(g.name, groupName.Trim(), StringComparison.OrdinalIgnoreCase));
            EditorGUILayout.HelpBox(replaces ? "This name exists. Its members will be replaced; existing bone transforms are kept." :
                "Create a named group from the checked bones. Existing names replace their membership.", MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.BeginHorizontal();
            bool accept;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(groupName)))
                accept = GUILayout.Button(replaces ? "Replace group" : "Create group");
            if (GUILayout.Button("Cancel")) Close();
            EditorGUILayout.EndHorizontal();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !string.IsNullOrWhiteSpace(groupName))
            { accept = true; Event.current.Use(); }
            if (!accept) return;
            try
            {
                var created = UMABoneGroupEditing.CreateOrReplace(pose, groupName, selectedBones);
                completed?.Invoke(created.name);
                Close();
            }
            catch (ArgumentException exception) { error = exception.Message; }
        }
    }
}
