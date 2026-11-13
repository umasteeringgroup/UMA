using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>Non-destructive, texture-compatible migration to the optional URP skin shaders.</summary>
    public static class UMARealisticSkinUtility
    {
        public const string ShaderName = "UMA/URP/Realistic Skin";
        /// <summary>The subsurface scattering version. It reads every input of <see cref="ShaderName"/>.</summary>
        public const string SSSShaderName = "UMA/URP/Realistic Skin SSS";
        public const string OriginalShaderName = "Shader Graphs/UMA3_SkinShader_URP";

        /// <summary>True for the three UMA3 URP skin shaders, which share one texture contract.</summary>
        public static bool IsSkinShader(Shader shader)
        {
            return shader != null && (shader.name == OriginalShaderName || shader.name == ShaderName || shader.name == SSSShaderName);
        }

        public static Material CreateMaterialCopy(Material source)
        {
            return CreateMaterialCopy(source, ShaderName, "_Realistic");
        }

        /// <summary>Creates a copy of a UMA3 URP skin material that uses the subsurface scattering shader.</summary>
        public static Material CreateSSSMaterialCopy(Material source)
        {
            return CreateMaterialCopy(source, SSSShaderName, "_RealisticSSS");
        }

        private static Material CreateMaterialCopy(Material source, string shaderName, string suffix)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!IsSkinShader(source.shader))
                throw new ArgumentException("Select a UMA3 URP skin material. Other shaders have different texture contracts.", nameof(source));
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("The shader '" + shaderName + "' is not installed.");
            var copy = new Material(shader) { name = source.name + suffix };
            // Copy only matching inputs, without inheriting Shader Graph keywords/render state.
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
            {
                string property = shader.GetPropertyName(i);
                if (!source.HasProperty(property)) continue;
                // The lookup texture belongs to the shader, never to a material.
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
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
        [MenuItem("Assets/UMA/Skin/Create Realistic SSS URP Copy", true)]
        private static bool CanCreateCopy()
        {
            var source = SourceMaterial(Selection.activeObject);
            return source != null && IsSkinShader(source.shader) && AssetDatabase.Contains(Selection.activeObject);
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
            CreateSelectedCopy(ShaderName, "_Realistic");
        }

        [MenuItem("Assets/UMA/Skin/Create Realistic SSS URP Copy")]
        private static void CreateSelectedSSSCopy()
        {
            CreateSelectedCopy(SSSShaderName, "_RealisticSSS");
        }

        private static void CreateSelectedCopy(string shaderName, string suffix)
        {
            var selected = Selection.activeObject;
            var source = SourceMaterial(selected);
            if (source == null) return;
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(selected)).Replace('\\', '/');
            if (!directory.StartsWith("Assets/", StringComparison.Ordinal)) directory = "Assets";
            var copy = CreateMaterialCopy(source, shaderName, suffix);
            string materialPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + copy.name + ".mat");
            AssetDatabase.CreateAsset(copy, materialPath);
            UnityEngine.Object result = copy;
            if (selected is UMAMaterial original)
            {
                var uma = UnityEngine.Object.Instantiate(original);
                uma.name = original.name + suffix;
                uma.objectName = uma.name;
                uma.material = copy;
                uma.MaterialName = copy.name;
                uma.ShaderName = shaderName;
                AssetDatabase.CreateAsset(uma, AssetDatabase.GenerateUniqueAssetPath(directory + "/" + uma.name + ".asset"));
                AssetDatabase.SaveAssetIfDirty(uma);
                result = uma;
            }
            // Only the new assets are written; other unsaved work in the project is left as it is.
            AssetDatabase.SaveAssetIfDirty(copy);
            Selection.activeObject = result;
            EditorGUIUtility.PingObject(result);
        }
    }
}
