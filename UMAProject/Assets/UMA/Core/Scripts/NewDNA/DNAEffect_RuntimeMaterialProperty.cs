using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Applies a shader property through renderer MaterialPropertyBlocks without
    /// regenerating UMA textures or meshes.
    /// </summary>
    [Serializable]
    public sealed class DNAEffect_RuntimeMaterialProperty : DNAEffect
    {
        [Flags]
        public enum ParameterType
        {
            Float = 1,
            Color = 2,
            Vector = 4,
            Texture = 8,
            Both = Float | Color
        }

        [Tooltip("Shader property written by this effect.")]
        public string propertyName;

        [Tooltip("Optional renderer GameObject name. Empty targets every UMA renderer.")]
        public string rendererName;

        [Tooltip("Optional shared color used to restrict generated materials. Empty targets every matching renderer material.")]
        public string sharedColorName;

        [Tooltip("Material index to target. -1 targets every material index.")]
        public int materialIndex = -1;

        public ParameterType parameterType = ParameterType.Float;
        public float zeroFloatValue;
        public float oneFloatValue = 1f;
        public Color zeroColorValue = Color.clear;
        public Color oneColorValue = Color.white;
        public Vector4 zeroVectorValue;
        public Vector4 oneVectorValue = Vector4.one;
        public Texture zeroTextureValue;
        public Texture oneTextureValue;

        [NonSerialized]
        private MaterialPropertyBlock _propertyBlock;
        [NonSerialized]
        private readonly List<Material> _materials =
            new List<Material>(4);

        public override DNAInstanceCollection.DNABuildType AreaEffect =>
            DNAInstanceCollection.DNABuildType.None;

        public override ExpressionEffectPhase ExpressionPhases =>
            ExpressionEffectPhase.RuntimeMaterial;

        public override string Description =>
            "Writes a float, color, vector, and/or texture through a renderer " +
            "MaterialPropertyBlock. This is safe for continuously animated " +
            "wrinkle, blush, and similar shader controls.";

        public override void ApplyExpressionMaterial(
            UMAData avatar,
            DNA dna,
            float value)
        {
            if (avatar == null ||
                string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            SkinnedMeshRenderer[] renderers = avatar.GetRenderers();
            if (renderers == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            float mappedValue = GetMappedValue(value);
            int propertyId = Shader.PropertyToID(propertyName);
            float floatValue = Mathf.LerpUnclamped(
                zeroFloatValue,
                oneFloatValue,
                mappedValue);
            Color colorValue = Color.LerpUnclamped(
                zeroColorValue,
                oneColorValue,
                mappedValue);
            Vector4 vectorValue = Vector4.LerpUnclamped(
                zeroVectorValue,
                oneVectorValue,
                mappedValue);
            Texture textureValue = mappedValue < 0.5f
                ? zeroTextureValue
                : oneTextureValue;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null ||
                    (!string.IsNullOrEmpty(rendererName) &&
                     !string.Equals(
                         renderer.gameObject.name,
                         rendererName,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _materials.Clear();
                renderer.GetSharedMaterials(_materials);
                int firstIndex = materialIndex >= 0
                    ? materialIndex
                    : 0;
                int lastIndex = materialIndex >= 0
                    ? materialIndex
                    : _materials.Count - 1;

                for (int targetIndex = firstIndex;
                     targetIndex <= lastIndex;
                     targetIndex++)
                {
                    if (targetIndex < 0 ||
                        targetIndex >= _materials.Count ||
                        !MatchesSharedColor(
                            avatar,
                            renderer,
                            targetIndex))
                    {
                        continue;
                    }

                    renderer.GetPropertyBlock(
                        _propertyBlock,
                        targetIndex);
                    if ((parameterType & ParameterType.Float) != 0)
                    {
                        _propertyBlock.SetFloat(
                            propertyId,
                            floatValue);
                    }

                    if ((parameterType & ParameterType.Color) != 0)
                    {
                        _propertyBlock.SetColor(
                            propertyId,
                            colorValue);
                    }
                    if ((parameterType & ParameterType.Vector) != 0)
                    {
                        _propertyBlock.SetVector(
                            propertyId,
                            vectorValue);
                    }
                    if ((parameterType & ParameterType.Texture) != 0)
                    {
                        _propertyBlock.SetTexture(
                            propertyId,
                            textureValue);
                    }

                    renderer.SetPropertyBlock(
                        _propertyBlock,
                        targetIndex);
                }
            }
        }

        internal bool MatchesSharedColor(
            UMAData avatar,
            SkinnedMeshRenderer renderer,
            int targetMaterialIndex)
        {
            if (string.IsNullOrEmpty(sharedColorName))
            {
                return true;
            }

            UMAData.GeneratedMaterials generatedMaterials =
                avatar.generatedMaterials;
            if (generatedMaterials == null ||
                generatedMaterials.materials == null)
            {
                return false;
            }

            for (int i = 0;
                 i < generatedMaterials.materials.Count;
                 i++)
            {
                UMAData.GeneratedMaterial generated =
                    generatedMaterials.materials[i];
                if (generated == null ||
                    generated.skinnedMeshRenderer != renderer ||
                    generated.materialIndex != targetMaterialIndex)
                {
                    continue;
                }

                if (generated.umaMaterial != null &&
                    generated.umaMaterial.shaderParms != null)
                {
                    for (int parmIndex = 0;
                         parmIndex <
                         generated.umaMaterial.shaderParms.Length;
                         parmIndex++)
                    {
                        if (string.Equals(
                            generated.umaMaterial
                                .shaderParms[parmIndex]
                                .ColorName,
                            sharedColorName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                if (generated.materialFragments == null)
                {
                    continue;
                }

                for (int fragmentIndex = 0;
                     fragmentIndex <
                     generated.materialFragments.Count;
                     fragmentIndex++)
                {
                    UMAData.MaterialFragment fragment =
                        generated.materialFragments[fragmentIndex];
                    if (fragment == null ||
                        fragment.overlayData == null)
                    {
                        continue;
                    }

                    for (int overlayIndex = 0;
                         overlayIndex <
                         fragment.overlayData.Length;
                         overlayIndex++)
                    {
                        OverlayData overlay =
                            fragment.overlayData[overlayIndex];
                        if (overlay != null &&
                            overlay.colorData != null &&
                            string.Equals(
                                overlay.colorData.name,
                                sharedColorName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

#if UNITY_EDITOR
        public override void DoGui(
            bool showDescription,
            bool showHelp,
            out AnimationCurve curveToCopy)
        {
            base.DoGui(
                showDescription,
                showHelp,
                out curveToCopy);
            propertyName =
                UnityEditor.EditorGUILayout.TextField(
                    "Property Name",
                    propertyName);
            rendererName =
                UnityEditor.EditorGUILayout.TextField(
                    "Renderer Name",
                    rendererName);
            sharedColorName =
                UnityEditor.EditorGUILayout.TextField(
                    "Shared Color Name",
                    sharedColorName);
            materialIndex =
                UnityEditor.EditorGUILayout.IntField(
                    "Material Index",
                    materialIndex);
            parameterType =
                (ParameterType)UnityEditor.EditorGUILayout.EnumFlagsField(
                    "Parameter Type",
                    parameterType);

            if ((parameterType & ParameterType.Float) != 0)
            {
                zeroFloatValue =
                    UnityEditor.EditorGUILayout.FloatField(
                        "Zero Float",
                        zeroFloatValue);
                oneFloatValue =
                    UnityEditor.EditorGUILayout.FloatField(
                        "One Float",
                        oneFloatValue);
            }

            if ((parameterType & ParameterType.Color) != 0)
            {
                zeroColorValue =
                    UnityEditor.EditorGUILayout.ColorField(
                        "Zero Color",
                        zeroColorValue);
                oneColorValue =
                    UnityEditor.EditorGUILayout.ColorField(
                    "One Color",
                    oneColorValue);
            }
            if ((parameterType & ParameterType.Vector) != 0)
            {
                zeroVectorValue =
                    UnityEditor.EditorGUILayout.Vector4Field(
                        "Zero Vector",
                        zeroVectorValue);
                oneVectorValue =
                    UnityEditor.EditorGUILayout.Vector4Field(
                        "One Vector",
                        oneVectorValue);
            }
            if ((parameterType & ParameterType.Texture) != 0)
            {
                zeroTextureValue =
                    UnityEditor.EditorGUILayout.ObjectField(
                        "Zero Texture",
                        zeroTextureValue,
                        typeof(Texture),
                        false) as Texture;
                oneTextureValue =
                    UnityEditor.EditorGUILayout.ObjectField(
                        "One Texture",
                        oneTextureValue,
                        typeof(Texture),
                        false) as Texture;
            }
        }
#endif
    }
}
