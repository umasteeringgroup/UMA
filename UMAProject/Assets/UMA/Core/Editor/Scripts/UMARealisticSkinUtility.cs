using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>Non-destructive, texture-compatible migration to the optional URP skin shader.</summary>
    public static class UMARealisticSkinUtility
    {
        public const string ShaderName = "UMA/URP/Realistic Skin";
        public const string OriginalShaderName = "Shader Graphs/UMA3_SkinShader_URP";

        public static Material CreateMaterialCopy(Material source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.shader == null || (source.shader.name != OriginalShaderName && source.shader.name != ShaderName))
                throw new ArgumentException("Select a UMA3 URP skin material. Other shaders have different texture contracts.", nameof(source));
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("The UMA Realistic Skin shader is not installed.");
            var copy = new Material(shader) { name = source.name + "_Realistic" };
            // Copy only matching inputs, without inheriting Shader Graph keywords/render state.
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
            {
                string property = shader.GetPropertyName(i);
                if (!source.HasProperty(property)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: copy.SetColor(property, source.GetColor(property)); break;
                    case ShaderPropertyType.Vector: copy.SetVector(property, source.GetVector(property)); break;
                    case ShaderPropertyType.Texture:
                        copy.SetTexture(property, source.GetTexture(property));
                        copy.SetTextureScale(property, source.GetTextureScale(property));
                        copy.SetTextureOffset(property, source.GetTextureOffset(property));
                        break;
                    case ShaderPropertyType.Int: copy.SetInteger(property, source.GetInteger(property)); break;
                    default: copy.SetFloat(property, source.GetFloat(property)); break;
                }
            }
            copy.enableInstancing = source.enableInstancing;
            copy.doubleSidedGI = source.doubleSidedGI;
            copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return copy;
        }

        [MenuItem("Assets/UMA/Skin/Create Realistic URP Copy", true)]
        private static bool CanCreateCopy()
        {
            var source = SourceMaterial(Selection.activeObject);
            return source != null && source.shader != null &&
                (source.shader.name == OriginalShaderName || source.shader.name == ShaderName) &&
                AssetDatabase.Contains(Selection.activeObject);
        }

        private static Material SourceMaterial(UnityEngine.Object selected)
        {
            if (selected is Material material) return material;
            if (selected is UMAMaterial uma)
                return new SerializedObject(uma).FindProperty("_material").objectReferenceValue as Material;
            return null;
        }

        [MenuItem("Assets/UMA/Skin/Create Realistic URP Copy")]
        private static void CreateSelectedCopy()
        {
            var selected = Selection.activeObject;
            var source = SourceMaterial(selected);
            if (source == null) return;
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(selected)).Replace('\\', '/');
            if (!directory.StartsWith("Assets/", StringComparison.Ordinal)) directory = "Assets";
            var copy = CreateMaterialCopy(source);
            string materialPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + source.name + "_Realistic.mat");
            AssetDatabase.CreateAsset(copy, materialPath);
            UnityEngine.Object result = copy;
            if (selected is UMAMaterial original)
            {
                var uma = UnityEngine.Object.Instantiate(original);
                uma.name = original.name + "_Realistic";
                uma.objectName = uma.name;
                uma.material = copy;
                uma.MaterialName = copy.name;
                uma.ShaderName = ShaderName;
                AssetDatabase.CreateAsset(uma, AssetDatabase.GenerateUniqueAssetPath(directory + "/" + uma.name + ".asset"));
                result = uma;
            }
            AssetDatabase.SaveAssets();
            Selection.activeObject = result;
            EditorGUIUtility.PingObject(result);
        }
    }
}
