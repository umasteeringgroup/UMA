#if UNITY_EDITOR
using System.IO;
using UMA;
using UnityEditor;
using UnityEngine;

/// <summary>Creates small, shader-agnostic runtime material examples.</summary>
public static class UMAExpressionExampleAssetFactory
{
    [MenuItem("Assets/Create/UMA/Expression Runtime Material Examples",
        priority = 212)]
    private static void CreateExamples()
    {
        string folder = GetSelectedFolder();

        DNA wrinkle = ScriptableObject.CreateInstance<DNA>();
        wrinkle.name = "WrinkleStrength_ExpressionDNA";
        wrinkle.displayName = "Wrinkle Strength";
        wrinkle.description =
            "Example immediate wrinkle intensity. Change the property name " +
            "to match the target shader.";
        wrinkle.defaultValue = 0f;
        wrinkle.effects.Add(new DNAEffect_RuntimeMaterialProperty
        {
            EffectName = "Wrinkle Strength",
            propertyName = "_WrinkleStrength",
            parameterType =
                DNAEffect_RuntimeMaterialProperty.ParameterType.Float,
            zeroFloatValue = 0f,
            oneFloatValue = 1f,
            minMapping = 0f,
            maxMapping = 1f,
            curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
        });

        DNA blush = ScriptableObject.CreateInstance<DNA>();
        blush.name = "CheekTint_ExpressionDNA";
        blush.displayName = "Cheek Tint";
        blush.description =
            "Example immediate cheek tint. Change the property name and " +
            "colors to match the target shader.";
        blush.defaultValue = 0f;
        blush.effects.Add(new DNAEffect_RuntimeMaterialProperty
        {
            EffectName = "Cheek Tint",
            propertyName = "_CheekTint",
            parameterType =
                DNAEffect_RuntimeMaterialProperty.ParameterType.Color,
            zeroColorValue = Color.clear,
            oneColorValue = new Color(1f, 0.18f, 0.2f, 0.65f),
            minMapping = 0f,
            maxMapping = 1f,
            curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
        });

        UMAExpressionGroup group =
            ScriptableObject.CreateInstance<UMAExpressionGroup>();
        group.name = "RuntimeMaterial_ExpressionGroup";
        group.expressions.Add(new UMAExpressionDefinition
        {
            id = "wrinkle_strength",
            displayName = "Wrinkle Strength",
            dna = wrinkle,
            roles = ExpressionRole.Custom,
            affectedJoints = ExpressionJoint.None,
            priority = 0
        });
        group.expressions.Add(new UMAExpressionDefinition
        {
            id = "cheek_tint",
            displayName = "Cheek Tint",
            dna = blush,
            roles = ExpressionRole.Emotion,
            affectedJoints = ExpressionJoint.None,
            priority = 1
        });

        string wrinklePath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/" + wrinkle.name + ".asset");
        string blushPath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/" + blush.name + ".asset");
        string groupPath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/" + group.name + ".asset");
        AssetDatabase.CreateAsset(wrinkle, wrinklePath);
        AssetDatabase.CreateAsset(blush, blushPath);
        AssetDatabase.CreateAsset(group, groupPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = group;
        EditorGUIUtility.PingObject(group);
    }

    private static string GetSelectedFolder()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path)) return "Assets";
        if (File.Exists(path))
            path = Path.GetDirectoryName(path)?.Replace('\\', '/');
        return AssetDatabase.IsValidFolder(path) ? path : "Assets";
    }
}
#endif
