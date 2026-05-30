using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.PoseTools;

namespace UMA
{
    [CustomEditor(typeof(UmaTPose))]
    public class UmaTPoseInspector : Editor
    {
        bool boneInfoFoldout = false;
        bool humanInfoFoldout = false;
        bool mecanimInfoFoldout = false;
        bool humanPoseFoldout = false;
        List<bool> foldouts = new List<bool>();

        UmaTPose source;
        UMABonePose bonePoseSource;
        string bonePoseCopyMessage;
        MessageType bonePoseCopyMessageType = MessageType.Info;

        void OnEnable()
        {
            source = target as UmaTPose;
            source.DeSerialize();
        }

        public override void OnInspectorGUI()
        {
            if (source == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            source.mapJaw = EditorGUILayout.Toggle("Map Jaw", source.mapJaw);
            serializedObject.Update();
            //base.DrawDefaultInspector();
            mecanimInfoFoldout = EditorGUILayout.Foldout(mecanimInfoFoldout, "Mecanim Adjustments");
            if (mecanimInfoFoldout)
            {
                source.armStretch = EditorGUILayout.FloatField("Arm Stretch", source.armStretch);
                source.legStretch = EditorGUILayout.FloatField("Leg Stretch", source.legStretch);
                source.feetSpacing = EditorGUILayout.FloatField("Feet Spacing", source.feetSpacing);
                source.lowerArmTwist = EditorGUILayout.FloatField("Lower Arm Twist", source.lowerArmTwist);
                source.upperArmTwist = EditorGUILayout.FloatField("Upper Arm Twist", source.upperArmTwist);
                source.lowerLegTwist = EditorGUILayout.FloatField("Lower Leg Twist", source.lowerLegTwist);
                source.upperLegTwist = EditorGUILayout.FloatField("Upper Leg Twist", source.upperLegTwist);
            }
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                source.Serialize();
                EditorUtility.SetDirty(source);
            }

            DrawBonePoseCopyControls();

            humanPoseFoldout = EditorGUILayout.Foldout(humanPoseFoldout, "Human Pose");
            if (humanPoseFoldout)
            {
                if (source.HasExtractedHumanPose())
                {
                    EditorGUILayout.HelpBox("Using extracted HumanPose data.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Using default HumanPose data.", MessageType.Warning);
                }
                var pose = source.GetHumanPose();
                EditorGUI.BeginChangeCheck();
                pose.bodyPosition = EditorGUILayout.Vector3Field("Body Position", pose.bodyPosition);
                Vector4 rot = EditorGUILayout.Vector4Field("Body Rotation (x,y,z,w)", new Vector4(pose.bodyRotation.x, pose.bodyRotation.y, pose.bodyRotation.z, pose.bodyRotation.w));
                pose.bodyRotation = new Quaternion(rot.x, rot.y, rot.z, rot.w);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Muscles", EditorStyles.boldLabel);
                if (pose.muscles != null && pose.muscles.Length == HumanTrait.MuscleCount)
                {
                    for (int i = 0; i < pose.muscles.Length; i++)
                    {
                        float min = HumanTrait.GetMuscleDefaultMin(i);
                        float max = HumanTrait.GetMuscleDefaultMax(i);
                        string label = HumanTrait.MuscleName[i];
                        pose.muscles[i] = EditorGUILayout.Slider(label, pose.muscles[i], min, max);
                    }
                }
                if (EditorGUI.EndChangeCheck())
                {
                    source.SetHumanPose(pose);
                    source.Serialize();
                    EditorUtility.SetDirty(source);
                }
            }

            boneInfoFoldout = EditorGUILayout.Foldout(boneInfoFoldout, "Bone Info");
            if (source.boneInfo != null)
            {
                if (boneInfoFoldout)
                {

                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.boneInfo.Length; i++)
                    {
                        EditorGUILayout.LabelField(source.boneInfo[i].name);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Bone Info is empty!", MessageType.Error);
            }

            if (foldouts.Count != source.humanInfo.Length)
            {
                foldouts.Clear();
                for (int i = 0; i < source.humanInfo.Length; i++)
                {
                    foldouts.Add(false);
                }
            }

            humanInfoFoldout = EditorGUILayout.Foldout(humanInfoFoldout, "Human Info");
            if (source.humanInfo != null)
            {
                if (humanInfoFoldout)
                {

                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.humanInfo.Length; i++)
                    {
                        //EditorGUILayout.BeginHorizontal();
                        foldouts[i]  = EditorGUILayout.Foldout(foldouts[i], $"{source.humanInfo[i].humanName} -> {source.humanInfo[i].boneName}");
                        // EditorGUILayout.LabelField(source.humanInfo[i].humanName);
                        // EditorGUILayout.LabelField(source.humanInfo[i].boneName);
                        if (foldouts[i])
                        {
                            UMA.Editors.GUIHelper.BeginVerticalPadded();
                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.LabelField("humanName", source.humanInfo[i].humanName);
                            source.humanInfo[i].boneName = EditorGUILayout.DelayedTextField("boneName", source.humanInfo[i].boneName);
                            EditorGUILayout.LabelField("limits");
                            EditorGUI.indentLevel ++;
                            source.humanInfo[i].limit.useDefaultValues = EditorGUILayout.Toggle("useDefault", source.humanInfo[i].limit.useDefaultValues);
                            if (!source.humanInfo[i].limit.useDefaultValues)
                            {
                                source.humanInfo[i].limit.axisLength = EditorGUILayout.FloatField("axisLength", source.humanInfo[i].limit.axisLength);
                                source.humanInfo[i].limit.min = EditorGUILayout.Vector3Field("min", source.humanInfo[i].limit.min, GUILayout.ExpandWidth(false));
                                source.humanInfo[i].limit.max = EditorGUILayout.Vector3Field("max", source.humanInfo[i].limit.max, GUILayout.ExpandWidth(false));
                                source.humanInfo[i].limit.center = EditorGUILayout.Vector3Field("center", source.humanInfo[i].limit.center, GUILayout.ExpandWidth(false));
                            }
                            EditorGUI.indentLevel--;
                            if (EditorGUI.EndChangeCheck())
                            {
                                serializedObject.ApplyModifiedProperties();
                                source.Serialize();
                                EditorUtility.SetDirty(source);
                            }
                            UMA.Editors.GUIHelper.EndVerticalPadded();
                        }
                       // EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Human Info is empty!", MessageType.Error);
            }


        }

        private void DrawBonePoseCopyControls()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Apply Bone Pose To TPose", EditorStyles.boldLabel);
            bonePoseSource = EditorGUILayout.ObjectField("Bone Pose", bonePoseSource, typeof(UMABonePose), false) as UMABonePose;

            bool canCopy = source != null && source.boneInfo != null && source.boneInfo.Length > 0 && bonePoseSource != null && bonePoseSource.poses != null && bonePoseSource.poses.Length > 0;
            using (new EditorGUI.DisabledScope(!canCopy))
            {
                if (GUILayout.Button("Apply Matching Bone Pose Transforms"))
                {
                    int replaced = ApplyBonePoseTransformsToTPose(bonePoseSource, out List<string> ignoredBones, out List<string> disabledBones);
                    if (replaced > 0)
                    {
                        bonePoseCopyMessage = $"Applied {replaced} matching bone pose transform{(replaced == 1 ? string.Empty : "s")}.";
                        if (ignoredBones.Count > 0)
                        {
                            bonePoseCopyMessage += "\nBones not found in TPose:\n" + string.Join("\n", ignoredBones);
                        }
                        if (disabledBones.Count > 0)
                        {
                            bonePoseCopyMessage += "\nDisabled pose bones skipped:\n" + string.Join("\n", disabledBones);
                        }
                        bonePoseCopyMessageType = MessageType.Info;
                    }
                    else
                    {
                        bonePoseCopyMessage = "No matching enabled TPose bones found.";
                        if (ignoredBones.Count > 0)
                        {
                            bonePoseCopyMessage += "\nBones not found in TPose:\n" + string.Join("\n", ignoredBones);
                        }
                        if (disabledBones.Count > 0)
                        {
                            bonePoseCopyMessage += "\nDisabled pose bones skipped:\n" + string.Join("\n", disabledBones);
                        }
                        bonePoseCopyMessageType = MessageType.Warning;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bonePoseCopyMessage))
            {
                EditorGUILayout.HelpBox(bonePoseCopyMessage, bonePoseCopyMessageType);
            }
        }

        private int ApplyBonePoseTransformsToTPose(UMABonePose bonePose, out List<string> ignoredBoneNames, out List<string> disabledBoneNames)
        {
            ignoredBoneNames = new List<string>();
            disabledBoneNames = new List<string>();
            if (source == null || bonePose == null || bonePose.poses == null)
            {
                return 0;
            }

            source.DeSerialize();
            if (source.boneInfo == null || source.boneInfo.Length == 0)
            {
                return 0;
            }

            Dictionary<string, UMABonePose.PoseBone> poseByName = new Dictionary<string, UMABonePose.PoseBone>(StringComparer.Ordinal);
            HashSet<string> disabledNames = new HashSet<string>(StringComparer.Ordinal);
            for (int poseIndex = 0; poseIndex < bonePose.poses.Length; poseIndex++)
            {
                UMABonePose.PoseBone poseBone = bonePose.poses[poseIndex];
                if (poseBone == null || string.IsNullOrEmpty(poseBone.bone))
                {
                    continue;
                }

                if (!poseBone.enabled)
                {
                    disabledNames.Add(poseBone.bone);
                    continue;
                }

                poseByName[poseBone.bone] = poseBone;
            }

            foreach (string copiedBoneName in poseByName.Keys)
            {
                disabledNames.Remove(copiedBoneName);
            }
            disabledBoneNames.AddRange(disabledNames);
            disabledBoneNames.Sort(StringComparer.Ordinal);

            if (poseByName.Count == 0)
            {
                return 0;
            }

            bool hasMatch = false;
            for (int boneIndex = 0; boneIndex < source.boneInfo.Length; boneIndex++)
            {
                string boneName = source.boneInfo[boneIndex].name;
                if (!string.IsNullOrEmpty(boneName) && poseByName.ContainsKey(boneName))
                {
                    hasMatch = true;
                    break;
                }
            }

            if (!hasMatch)
            {
                ignoredBoneNames.AddRange(poseByName.Keys);
                ignoredBoneNames.Sort(StringComparer.Ordinal);
                return 0;
            }

            Undo.RecordObject(source, "Copy Bone Pose Transforms To TPose");

            int replaced = 0;
            HashSet<string> matchedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int boneIndex = 0; boneIndex < source.boneInfo.Length; boneIndex++)
            {
                SkeletonBone skeletonBone = source.boneInfo[boneIndex];
                if (string.IsNullOrEmpty(skeletonBone.name) || !poseByName.TryGetValue(skeletonBone.name, out UMABonePose.PoseBone poseBone))
                {
                    continue;
                }

                skeletonBone.position += poseBone.position;
                skeletonBone.rotation = NormalizeSafe(skeletonBone.rotation * poseBone.rotation);
                skeletonBone.scale = Vector3.Scale(skeletonBone.scale, poseBone.scale);
                source.boneInfo[boneIndex] = skeletonBone;
                matchedNames.Add(skeletonBone.name);
                replaced++;
            }

            foreach (string poseBoneName in poseByName.Keys)
            {
                if (!matchedNames.Contains(poseBoneName))
                {
                    ignoredBoneNames.Add(poseBoneName);
                }
            }
            ignoredBoneNames.Sort(StringComparer.Ordinal);
            source.Serialize();
            EditorUtility.SetDirty(source);
            serializedObject.Update();
            return replaced;
        }

        private static Quaternion NormalizeSafe(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            if (magnitude <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            return new Quaternion(rotation.x / magnitude, rotation.y / magnitude, rotation.z / magnitude, rotation.w / magnitude);
        }
    }
}
