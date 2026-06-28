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

        UmaTPose compareTPose;
        List<string> comparisonDifferences = new List<string>();
        Vector2 comparisonScrollPos;
        bool showComparisonResults;

        List<string> validationIssues = new List<string>();
        Vector2 validationScrollPos;
        bool showValidationResults;

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

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Compare TPoses", EditorStyles.boldLabel);
            compareTPose = EditorGUILayout.ObjectField("Compare to", compareTPose, typeof(UmaTPose), false) as UmaTPose;
            using (new EditorGUI.DisabledScope(compareTPose == null || compareTPose == source))
            {
                if (GUILayout.Button("Compare TPoses"))
                {
                    CompareTPoses(source, compareTPose);
                    showComparisonResults = true;
                }
            }
            if (showComparisonResults && comparisonDifferences.Count > 0)
            {
                EditorGUILayout.LabelField($"Differences ({comparisonDifferences.Count}):", EditorStyles.boldLabel);
                comparisonScrollPos = EditorGUILayout.BeginScrollView(comparisonScrollPos, GUILayout.Height(200));
                foreach (var diff in comparisonDifferences)
                {
                    EditorGUILayout.LabelField(diff, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndScrollView();
            }
            else if (showComparisonResults)
            {
                EditorGUILayout.HelpBox("No differences found.", MessageType.Info);
            }

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
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.boneInfo.Length; i++)
                    {
                        var bone = source.boneInfo[i];
                        string newName = EditorGUILayout.DelayedTextField($"Bone {i}", bone.name);
                        if (newName != bone.name)
                        {
                            bone.name = newName;
                            source.boneInfo[i] = bone;
                        }
                    }
                    EditorGUI.indentLevel--;
                    if (EditorGUI.EndChangeCheck())
                    {
                        source.Serialize();
                        EditorUtility.SetDirty(source);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Bone Info is empty!", MessageType.Error);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Validate Bone Transforms"))
            {
                ValidateBoneTransforms();
                showValidationResults = true;
            }
            if (showValidationResults)
            {
                if (validationIssues.Count > 0)
                {
                    EditorGUILayout.HelpBox($"{validationIssues.Count} issue(s) found", MessageType.Warning);
                    validationScrollPos = EditorGUILayout.BeginScrollView(validationScrollPos, GUILayout.Height(150));
                    foreach (var issue in validationIssues)
                    {
                        EditorGUILayout.LabelField(issue, EditorStyles.wordWrappedLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("All bone transforms are valid.", MessageType.Info);
                }
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

        private void CompareTPoses(UmaTPose a, UmaTPose b)
        {
            comparisonDifferences.Clear();
            if (a == null || b == null) return;

            a.DeSerialize();
            b.DeSerialize();

            // Mecanim adjustments
            CompareField("armStretch", a.armStretch, b.armStretch);
            CompareField("legStretch", a.legStretch, b.legStretch);
            CompareField("feetSpacing", a.feetSpacing, b.feetSpacing);
            CompareField("lowerArmTwist", a.lowerArmTwist, b.lowerArmTwist);
            CompareField("upperArmTwist", a.upperArmTwist, b.upperArmTwist);
            CompareField("lowerLegTwist", a.lowerLegTwist, b.lowerLegTwist);
            CompareField("upperLegTwist", a.upperLegTwist, b.upperLegTwist);
            CompareField("mapJaw", a.mapJaw, b.mapJaw);

            // Bone Info — lookup by name, report missing and value diffs only
            if (a.boneInfo != null && b.boneInfo != null)
            {
                var aBones = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
                var bBones = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
                for (int i = 0; i < a.boneInfo.Length; i++)
                {
                    string name = a.boneInfo[i].name ?? $"<unnamed_{i}>";
                    aBones[name] = a.boneInfo[i];
                }
                for (int i = 0; i < b.boneInfo.Length; i++)
                {
                    string name = b.boneInfo[i].name ?? $"<unnamed_{i}>";
                    bBones[name] = b.boneInfo[i];
                }

                // Missing in source
                foreach (var kv in bBones)
                {
                    if (!aBones.ContainsKey(kv.Key))
                        comparisonDifferences.Add($"boneInfo \"{kv.Key}\": missing in source, present in compare");
                }
                // Missing in compare
                foreach (var kv in aBones)
                {
                    if (!bBones.ContainsKey(kv.Key))
                        comparisonDifferences.Add($"boneInfo \"{kv.Key}\": present in source, missing in compare");
                }
                // Same name — compare values
                foreach (var kv in aBones)
                {
                    if (bBones.TryGetValue(kv.Key, out var bb))
                    {
                        var ba = kv.Value;
                        string prefix = $"boneInfo \"{kv.Key}\"";
                        CompareVector3($"{prefix}.position", ba.position, bb.position);
                        CompareQuaternion($"{prefix}.rotation", ba.rotation, bb.rotation);
                        CompareVector3($"{prefix}.scale", ba.scale, bb.scale);
                    }
                }
            }
            else
            {
                if (a.boneInfo == null && b.boneInfo != null) comparisonDifferences.Add("boneInfo: null in source, populated in compare");
                else if (a.boneInfo != null && b.boneInfo == null) comparisonDifferences.Add("boneInfo: populated in source, null in compare");
            }

            // Human Info — lookup by boneName, report missing and value diffs only
            if (a.humanInfo != null && b.humanInfo != null)
            {
                var aHuman = new Dictionary<string, HumanBone>(StringComparer.Ordinal);
                var bHuman = new Dictionary<string, HumanBone>(StringComparer.Ordinal);
                for (int i = 0; i < a.humanInfo.Length; i++)
                {
                    string key = a.humanInfo[i].boneName ?? $"<unnamed_{i}>";
                    aHuman[key] = a.humanInfo[i];
                }
                for (int i = 0; i < b.humanInfo.Length; i++)
                {
                    string key = b.humanInfo[i].boneName ?? $"<unnamed_{i}>";
                    bHuman[key] = b.humanInfo[i];
                }

                // Missing in source
                foreach (var kv in bHuman)
                {
                    if (!aHuman.ContainsKey(kv.Key))
                        comparisonDifferences.Add($"humanInfo \"{kv.Key}\": missing in source, present in compare");
                }
                // Missing in compare
                foreach (var kv in aHuman)
                {
                    if (!bHuman.ContainsKey(kv.Key))
                        comparisonDifferences.Add($"humanInfo \"{kv.Key}\": present in source, missing in compare");
                }
                // Same boneName — compare values
                foreach (var kv in aHuman)
                {
                    if (bHuman.TryGetValue(kv.Key, out var hb))
                    {
                        var ha = kv.Value;
                        string prefix = $"humanInfo \"{kv.Key}\"";
                        CompareField($"{prefix}.humanName", ha.humanName, hb.humanName);
                        CompareField($"{prefix}.limit.useDefaultValues", ha.limit.useDefaultValues, hb.limit.useDefaultValues);
                        if (!ha.limit.useDefaultValues || !hb.limit.useDefaultValues)
                        {
                            CompareField($"{prefix}.limit.axisLength", ha.limit.axisLength, hb.limit.axisLength);
                            CompareVector3($"{prefix}.limit.min", ha.limit.min, hb.limit.min);
                            CompareVector3($"{prefix}.limit.max", ha.limit.max, hb.limit.max);
                            CompareVector3($"{prefix}.limit.center", ha.limit.center, hb.limit.center);
                        }
                    }
                }
            }
            else
            {
                if (a.humanInfo == null && b.humanInfo != null) comparisonDifferences.Add("humanInfo: null in source, populated in compare");
                else if (a.humanInfo != null && b.humanInfo == null) comparisonDifferences.Add("humanInfo: populated in source, null in compare");
            }

            // Human Pose (if extracted)
            bool aHasPose = a.HasExtractedHumanPose();
            bool bHasPose = b.HasExtractedHumanPose();
            if (aHasPose != bHasPose)
            {
                comparisonDifferences.Add($"HumanPose extracted: source={aHasPose}, compare={bHasPose}");
            }
            if (aHasPose && bHasPose)
            {
                var pa = a.GetHumanPose();
                var pb = b.GetHumanPose();
                CompareVector3("HumanPose.bodyPosition", pa.bodyPosition, pb.bodyPosition);
                CompareQuaternion("HumanPose.bodyRotation", pa.bodyRotation, pb.bodyRotation);
                if (pa.muscles != null && pb.muscles != null && pa.muscles.Length == pb.muscles.Length)
                {
                    for (int i = 0; i < pa.muscles.Length; i++)
                    {
                        CompareField($"HumanPose.muscles[{HumanTrait.MuscleName[i]}]", pa.muscles[i], pb.muscles[i]);
                    }
                }
            }
        }

        private void CompareField(string label, float a, float b)
        {
            if (!Mathf.Approximately(a, b))
                comparisonDifferences.Add($"{label}: {a} != {b}");
        }

        private void CompareField(string label, bool a, bool b)
        {
            if (a != b)
                comparisonDifferences.Add($"{label}: {a} != {b}");
        }

        private void CompareField(string label, string a, string b)
        {
            if (a != b)
                comparisonDifferences.Add($"{label}: \"{a}\" != \"{b}\"");
        }

        private void CompareVector3(string label, Vector3 a, Vector3 b)
        {
            if (a != b)
                comparisonDifferences.Add($"{label}: ({a.x:F4}, {a.y:F4}, {a.z:F4}) != ({b.x:F4}, {b.y:F4}, {b.z:F4})");
        }

        private void CompareQuaternion(string label, Quaternion a, Quaternion b)
        {
            if (Mathf.Abs(a.x - b.x) > 0.0001f || Mathf.Abs(a.y - b.y) > 0.0001f ||
                Mathf.Abs(a.z - b.z) > 0.0001f || Mathf.Abs(a.w - b.w) > 0.0001f)
                comparisonDifferences.Add($"{label}: ({a.x:F4}, {a.y:F4}, {a.z:F4}, {a.w:F4}) != ({b.x:F4}, {b.y:F4}, {b.z:F4}, {b.w:F4})");
        }

        private void ValidateBoneTransforms()
        {
            validationIssues.Clear();
            if (source == null) return;
            source.DeSerialize();

            var boneInfo = source.boneInfo;
            var humanInfo = source.humanInfo;

            if (boneInfo == null || boneInfo.Length == 0)
            {
                validationIssues.Add("boneInfo is null or empty.");
                return;
            }

            // Build lookup by name; detect duplicates
            var boneByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < boneInfo.Length; i++)
            {
                string name = boneInfo[i].name;
                if (string.IsNullOrEmpty(name))
                {
                    validationIssues.Add($"boneInfo[{i}]: name is null or empty.");
                    continue;
                }
                if (boneByName.ContainsKey(name))
                {
                    duplicateNames.Add(name);
                    validationIssues.Add($"boneInfo[{i}]: duplicate name \"{name}\" (also at index {boneByName[name]}).");
                }
                else
                {
                    boneByName[name] = i;
                }
            }

            // Validate each bone
            for (int i = 0; i < boneInfo.Length; i++)
            {
                var bone = boneInfo[i];
                string prefix = string.IsNullOrEmpty(bone.name) ? $"boneInfo[{i}]" : $"boneInfo \"{bone.name}\"";

                // Rotation validity
                if (!IsQuaternionValid(bone.rotation))
                {
                    validationIssues.Add($"{prefix}.rotation: invalid ({bone.rotation.x}, {bone.rotation.y}, {bone.rotation.z}, {bone.rotation.w}).");
                }
                else
                {
                    // Check for identity (often a sign of uninitialised data)
                    float mag = bone.rotation.x * bone.rotation.x + bone.rotation.y * bone.rotation.y +
                                bone.rotation.z * bone.rotation.z + bone.rotation.w * bone.rotation.w;
                    if (mag < 0.0001f || mag > 10000f)
                        validationIssues.Add($"{prefix}.rotation: degenerate magnitude {mag:F6}.");
                }

                // Position validity
                if (!IsVector3Valid(bone.position))
                    validationIssues.Add($"{prefix}.position: invalid ({bone.position.x}, {bone.position.y}, {bone.position.z}).");

                // Scale validity
                if (!IsVector3Valid(bone.scale))
                {
                    validationIssues.Add($"{prefix}.scale: invalid ({bone.scale.x}, {bone.scale.y}, {bone.scale.z}).");
                }
                else if (Mathf.Abs(bone.scale.x) < 0.0001f || Mathf.Abs(bone.scale.y) < 0.0001f || Mathf.Abs(bone.scale.z) < 0.0001f)
                {
                    validationIssues.Add($"{prefix}.scale: near-zero ({bone.scale.x:F6}, {bone.scale.y:F6}, {bone.scale.z:F6}) — will collapse geometry.");
                }
            }

            // Check humanInfo bones reference valid boneInfo entries
            if (humanInfo != null)
            {
                var humanBoneNames = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < humanInfo.Length; i++)
                {
                    string bn = humanInfo[i].boneName;
                    if (string.IsNullOrEmpty(bn))
                    {
                        validationIssues.Add($"humanInfo[{i}] (humanName=\"{humanInfo[i].humanName}\"): boneName is null or empty.");
                        continue;
                    }
                    if (!boneByName.ContainsKey(bn))
                        validationIssues.Add($"humanInfo \"{bn}\" (humanName=\"{humanInfo[i].humanName}\"): boneName not found in boneInfo.");
                    if (!humanBoneNames.Add(bn))
                        validationIssues.Add($"humanInfo \"{bn}\": duplicate boneName in humanInfo.");
                }
            }
        }

        private static bool IsVector3Valid(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static bool IsQuaternionValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w) &&
                   !float.IsInfinity(q.x) && !float.IsInfinity(q.y) && !float.IsInfinity(q.z) && !float.IsInfinity(q.w);
        }
    }
}
