#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

/// <summary>Converts legacy pose-pair sets to stable-ID expression DNA.</summary>
public static class UMAExpressionSetConverter
{
    public sealed class ConversionResult
    {
        public UMAExpressionGroup group;
        public readonly List<DNA> dnaAssets = new List<DNA>();
    }

    /// <summary>
    /// Creates the converted objects in memory. The caller owns the objects.
    /// This deterministic path is shared by the asset command and tests.
    /// </summary>
    public static ConversionResult ConvertInMemory(UMAExpressionSet source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        ConversionResult result = new ConversionResult
        {
            group = ScriptableObject.CreateInstance<UMAExpressionGroup>()
        };
        result.group.name = source.name + "_ExpressionGroup";

        int poseCount = ExpressionPlayer.PoseCount;
        for (int i = 0; i < poseCount; i++)
        {
            DNA dna = ScriptableObject.CreateInstance<DNA>();
            string id = ExpressionPlayer.PoseNames[i];
            dna.name = id + "_ExpressionDNA";
            dna.displayName = id;
            dna.description = "Converted from " + source.name +
                " legacy expression channel " + id + ".";
            dna.defaultValue = 0.5f;

            UMAExpressionSet.PosePair pair =
                source.posePairs != null && i < source.posePairs.Length
                    ? source.posePairs[i] : null;
            if (pair != null)
            {
                if (pair.primary != null)
                    dna.effects.Add(CreatePoseEffect(pair.primary, false));
                if (pair.inverse != null)
                    dna.effects.Add(CreatePoseEffect(pair.inverse, true));
            }

            result.dnaAssets.Add(dna);
            result.group.expressions.Add(new UMAExpressionDefinition
            {
                id = id,
                displayName = ObjectNames.NicifyVariableName(id),
                dna = dna,
                roles = GetRoles(i),
                affectedJoints = GetJoints(i),
                priority = i,
                blendMode = ExpressionBlendMode.Override,
                blinkClosedValue = 0f
            });
        }
        return result;
    }

    public static ConversionResult ConvertToAssets(UMAExpressionSet source,
        string destinationFolder, RaceData assignToRace = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!AssetDatabase.IsValidFolder(destinationFolder))
            throw new ArgumentException(
                "Destination must be an existing Assets folder.",
                nameof(destinationFolder));

        ConversionResult result = ConvertInMemory(source);
        string groupPath = AssetDatabase.GenerateUniqueAssetPath(
            JoinAssetPath(destinationFolder,
                source.name + "_ExpressionGroup.asset"));
        AssetDatabase.StartAssetEditing();
        try
        {
            AssetDatabase.CreateAsset(result.group, groupPath);
            for (int i = 0; i < result.dnaAssets.Count; i++)
            {
                DNA dna = result.dnaAssets[i];
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    JoinAssetPath(destinationFolder,
                        MakeFileName(dna.name) + ".asset"));
                AssetDatabase.CreateAsset(dna, path);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        if (assignToRace != null)
        {
            List<ExpressionValidationMessage> validation =
                new List<ExpressionValidationMessage>();
            if (!result.group.Validate(validation))
                throw new InvalidOperationException(
                    "The converted expression group did not validate and " +
                    "was not assigned to the race.");
            Undo.RecordObject(assignToRace, "Assign Expression Group");
            assignToRace.expressionGroup = result.group;
            EditorUtility.SetDirty(assignToRace);
        }

        EditorUtility.SetDirty(result.group);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return result;
    }

    public static DNAEffect_BonePose CreatePoseEffect(UMABonePose pose,
        bool inverse)
    {
        DNAEffect_BonePose effect = new DNAEffect_BonePose
        {
            EffectName = inverse ? "Inverse Pose" : "Primary Pose",
            bonePose = pose,
            minMapping = 0f,
            maxMapping = 1f,
            curve = inverse
                ? new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.5f, 0f),
                    new Keyframe(1f, 0f))
                : new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, 0f),
                    new Keyframe(1f, 1f))
        };
        SetLinearTangents(effect.curve);
        return effect;
    }

    public static ExpressionRole GetRoles(int index)
    {
        switch (index)
        {
            case 26: return ExpressionRole.BlinkLeft;
            case 27: return ExpressionRole.BlinkRight;
            case 28: return ExpressionRole.EyeVerticalLeft;
            case 29: return ExpressionRole.EyeVerticalRight;
            case 30: return ExpressionRole.EyeHorizontalLeft;
            case 31: return ExpressionRole.EyeHorizontalRight;
        }
        if (index >= 44) return ExpressionRole.Emotion;
        if (index >= 6 && index <= 35) return ExpressionRole.Viseme;
        return ExpressionRole.Custom;
    }

    public static ExpressionJoint GetJoints(int index)
    {
        ExpressionPlayer.MecanimJoint legacy =
            index >= 0 && index < ExpressionPlayer.MecanimAlternate.Length
                ? ExpressionPlayer.MecanimAlternate[index]
                : ExpressionPlayer.MecanimJoint.None;
        ExpressionJoint result = ExpressionJoint.None;
        if ((legacy & ExpressionPlayer.MecanimJoint.Head) != 0)
            result |= ExpressionJoint.Head;
        if ((legacy & ExpressionPlayer.MecanimJoint.Neck) != 0)
            result |= ExpressionJoint.Neck;
        if ((legacy & ExpressionPlayer.MecanimJoint.Jaw) != 0)
            result |= ExpressionJoint.Jaw;
        if ((legacy & ExpressionPlayer.MecanimJoint.Eye) != 0)
            result |= ExpressionJoint.Eyes;
        if ((legacy & ExpressionPlayer.MecanimJoint.Hands) != 0)
            result |= ExpressionJoint.Hands;
        return result == ExpressionJoint.None
            ? ExpressionJoint.Other : result;
    }

    [MenuItem("Assets/UMA/Convert UMAExpressionSet to UMAExpressionGroup",
        true)]
    private static bool CanConvertSelection() =>
        Selection.activeObject is UMAExpressionSet;

    [MenuItem("Assets/UMA/Convert UMAExpressionSet to UMAExpressionGroup")]
    private static void ConvertSelection()
    {
        UMAExpressionSet source = Selection.activeObject as UMAExpressionSet;
        if (source == null) return;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string defaultFolder = Path.GetDirectoryName(sourcePath)
            ?.Replace('\\', '/') ?? "Assets";
        string absolute = EditorUtility.OpenFolderPanel(
            "Expression Group Destination",
            Path.GetFullPath(defaultFolder), string.Empty);
        if (string.IsNullOrEmpty(absolute)) return;
        string destination = AbsoluteToAssetPath(absolute);
        if (destination == null)
        {
            EditorUtility.DisplayDialog("Invalid Destination",
                "Choose a folder inside this project's Assets folder.", "OK");
            return;
        }

        RaceData race = FindSelectedRace();
        ConversionResult result = ConvertToAssets(source, destination, race);
        Selection.activeObject = result.group;
        EditorGUIUtility.PingObject(result.group);
    }

    private static RaceData FindSelectedRace()
    {
        UnityEngine.Object[] selected = Selection.objects;
        for (int i = 0; i < selected.Length; i++)
            if (selected[i] is RaceData race) return race;
        return null;
    }

    private static void SetLinearTangents(AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i,
                AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i,
                AnimationUtility.TangentMode.Linear);
        }
    }

    private static string MakeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i], '_');
        return value;
    }

    private static string JoinAssetPath(string folder, string file) =>
        (folder.TrimEnd('/', '\\') + "/" + file).Replace('\\', '/');

    private static string AbsoluteToAssetPath(string absolute)
    {
        string root = Path.GetFullPath(Application.dataPath)
            .Replace('\\', '/').TrimEnd('/');
        string path = Path.GetFullPath(absolute)
            .Replace('\\', '/').TrimEnd('/');
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        return "Assets" + path.Substring(root.Length);
    }
}
#endif
