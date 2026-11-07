using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.PoseTools
{
    public partial class UMABonePoseEditor
    {
        [SerializeField] private string selectedBoneGroupName;
        private string boneGroupError;

        private void RefreshBoneGroupAfterUndo()
        {
            if (targetPose == null) return;
            serializedObject.Update();
            if (SelectedBoneGroup != null)
            {
                RegeneratePoseTargetPreviewIfNeeded();
                if (!dynamicDNAConverterMode) ApplySourceSkeletonPreview(true);
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private UMABonePose.BoneGroup SelectedBoneGroup => targetPose == null ? null :
            targetPose.boneGroups?.FirstOrDefault(g => g != null && g.name == selectedBoneGroupName);

        private void SelectBoneGroup(string name, Tool tool)
        {
            ClearActiveEditState();
            selectedBoneGroupName = name;
            useIKEditor = false;
            previewWeight = 1f;
            Tools.current = tool;
            protectBonePoseSceneSelection = true;
            SetBonePoseSelectionProtection(ShouldProtectBonePoseSceneSelection());
            SyncBoneToolWithUnity();
            OnUpdate();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawBoneGroups()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Bone Groups", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(linkedTranslationBoneNames.Count == 0))
                {
                    if (GUILayout.Button("Create bone group"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        UMABoneGroupNameWindow.Open(targetPose, linkedTranslationBoneNames, name =>
                        {
                            if (this == null || targetPose == null) return;
                            serializedObject.Update();
                            SelectBoneGroup(name, Tool.Rotate);
                        });
                    }
                }
            }
            EditorGUILayout.LabelField("Check bones below to choose group members.", EditorStyles.miniLabel);
            foreach (var group in targetPose.boneGroups ?? Array.Empty<UMABonePose.BoneGroup>())
            {
                if (group == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = SelectedBoneGroup == group;
                    if (GUILayout.Toggle(selected, group.name + " (" + (group.bones?.Length ?? 0) + ")", "Button") && !selected)
                        SelectBoneGroup(group.name, Tools.current == Tool.Move || Tools.current == Tool.Scale ? Tools.current : Tool.Rotate);
                    if (GUILayout.Button(new GUIContent("Select members", "Check this group's members in the bone list."), GUILayout.Width(105)))
                    {
                        linkedTranslationBoneNames.Clear();
                        foreach (string bone in group.bones ?? Array.Empty<string>()) linkedTranslationBoneNames.Add(bone);
                    }
                    if (GUILayout.Button(new GUIContent("Remove", "Remove this group; keep its bone transforms."), GUILayout.Width(65)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        UMABoneGroupEditing.Remove(targetPose, group);
                        if (selected) ClearActiveEditState();
                        serializedObject.Update();
                        break;
                    }
                }
            }
            var active = SelectedBoneGroup;
            if (active == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Editing group: " + active.name, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Each member is edited in its own local space. Rotation and scale keep each bone's pivot. Only group members are affected; automatic mirroring and linked translation are not added.", MessageType.Info);
                int available = (active.bones ?? Array.Empty<string>()).Distinct().Count(name =>
                    targetPose.poses.Any(p => p != null && p.bone == name));
                if (available < (active.bones?.Distinct().Count() ?? 0))
                    EditorGUILayout.HelpBox("Some members are no longer in this pose. Their names are retained, and available members can still be edited.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Move", EditorStyles.miniButtonLeft)) SelectBoneGroup(active.name, Tool.Move);
                    if (GUILayout.Button("Rotate", EditorStyles.miniButtonMid)) SelectBoneGroup(active.name, Tool.Rotate);
                    if (GUILayout.Button("Scale", EditorStyles.miniButtonRight)) SelectBoneGroup(active.name, Tool.Scale);
                }
                using (new EditorGUI.DisabledScope(available == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 position = EditorGUILayout.Vector3Field("Translation", active.position);
                    Vector3 angles = EditorGUILayout.Vector3Field("Rotation", active.rotation.eulerAngles);
                    Vector3 scale = EditorGUILayout.Vector3Field("Scale factors", active.scale);
                    if (EditorGUI.EndChangeCheck()) ApplyBoneGroupEdit(active, position, Quaternion.Euler(angles), scale);
                }
                if (!string.IsNullOrEmpty(boneGroupError)) EditorGUILayout.HelpBox(boneGroupError, MessageType.Error);
            }
        }

        private void ApplyBoneGroupEdit(UMABonePose.BoneGroup group, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            serializedObject.ApplyModifiedProperties();
            try
            {
                int count = UMABoneGroupEditing.Apply(targetPose, group, position, rotation, scale);
                boneGroupError = count == 0 ? "No members of this group are present in the pose." : null;
                if (count > 0)
                {
                    _poseEdited = true;
                    RegeneratePoseTargetPreviewIfNeeded();
                    if (!dynamicDNAConverterMode) ApplySourceSkeletonPreview(true);
                }
            }
            catch (ArgumentException exception) { boneGroupError = exception.Message; }
            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawBoneGroupSceneHandles()
        {
            var group = SelectedBoneGroup;
            if (group == null || !haveValidContext || context.activeUMA.skeleton == null) return;
            Transform anchor = null;
            foreach (string bone in group.bones ?? Array.Empty<string>())
            {
                if (!targetPose.poses.Any(p => p != null && p.bone == bone)) continue;
                var member = context.activeUMA.skeleton.GetBoneTransform(bone);
                if (member == null) continue;
                if (anchor == null) anchor = member;
                Handles.Label(member.position, bone);
            }
            if (anchor == null) return;
            // The first member locates/orients the handle only; there is no shared group pivot.
            Vector3 position = group.position;
            Quaternion rotation = group.rotation;
            Vector3 scale = group.scale;
            EditorGUI.BeginChangeCheck();
            switch (Tools.current)
            {
                case Tool.Move:
                    Vector3 moved = Handles.PositionHandle(anchor.position,
                        Tools.pivotRotation == PivotRotation.Local ? anchor.rotation : Quaternion.identity);
                    Vector3 delta = moved - anchor.position;
                    position += anchor.parent == null ? delta : anchor.parent.InverseTransformVector(delta);
                    break;
                case Tool.Rotate:
                    Quaternion turned = Handles.RotationHandle(anchor.rotation, anchor.position);
                    rotation = group.rotation * (Quaternion.Inverse(anchor.rotation) * turned);
                    break;
                case Tool.Scale:
                    scale = Handles.ScaleHandle(group.scale, anchor.position, anchor.rotation, HandleUtility.GetHandleSize(anchor.position));
                    break;
            }
            if (EditorGUI.EndChangeCheck()) ApplyBoneGroupEdit(group, position, rotation, scale);
        }
    }
}
