using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    public static partial class UMAResourceReuse
    {
        // Numeric, exact signatures: the hash selects a bucket, all bytes are compared.
        // Scratch owns no Unity objects and allocates only when its capacity grows.
        private static readonly AtlasSignature materialSignature = new AtlasSignature();
        private static readonly HashSet<int> appliedMaterialProperties = new HashSet<int>();

        private static void DescribeMaterial(AtlasSignature s, Material material, UMAAtlasBinding[] atlases = null)
        {
            s.Asset(material.shader);
            s.Add(material.renderQueue); s.Add(material.enableInstancing); s.Add(material.doubleSidedGI);
            s.Add((int)material.globalIlluminationFlags);
            s.Text(material.GetTag("RenderType", false)); s.Text(material.GetTag("Queue", false));
            s.Text(material.GetTag("DisableBatching", false)); s.Text(material.GetTag("ForceNoShadowCasting", false));
            s.Text(material.GetTag("IgnoreProjector", false)); s.Text(material.GetTag("RenderPipeline", false));
            var keywords = material.shaderKeywords;
            Array.Sort(keywords, StringComparer.Ordinal);
            s.Add(keywords.Length);
            foreach (string keyword in keywords) s.Text(keyword);
            var layout = GetShaderLayout(material.shader);
            s.Add(layout.Ids.Length);
            for (int i = 0; i < layout.Ids.Length; i++)
            {
                int id = layout.Ids[i]; s.Add(id); s.Add((int)layout.Types[i]);
                switch (layout.Types[i])
                {
                    case ShaderPropertyType.Color: s.Add(material.GetColor(id)); break;
                    case ShaderPropertyType.Vector: s.Add(material.GetVector(id)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: s.Add(material.GetFloat(id)); break;
                    case ShaderPropertyType.Int: s.Add(material.GetInteger(id)); break;
                    case ShaderPropertyType.Texture:
                        var texture = material.GetTexture(id);
                        UMAAtlasBinding binding = null;
                        if (atlases != null)
                            foreach (var atlas in atlases)
                                if (atlas != null && atlas.Property == layout.Names[i] && atlas.Atlas.Texture == texture)
                                { binding = atlas; break; }
                        s.Add(binding != null);
                        s.Asset(binding != null ? binding.Atlas : texture);
                        s.Add(material.GetTextureScale(id)); s.Add(material.GetTextureOffset(id));
                        break;
                    default: throw new NotSupportedException("Unsupported material property type.");
                }
            }
            s.Add(material.passCount);
            for (int i = 0; i < material.passCount; i++)
            { string pass = material.GetPassName(i); s.Text(pass); s.Add(material.GetShaderPassEnabled(pass)); }
        }

        private static void DescribeMaterialInputs(AtlasSignature s, UMAData.GeneratedMaterial gm)
        {
            s.Add(gm.materialFragments.Count);
            foreach (var fragment in gm.materialFragments)
            {
                var overlays = fragment.overlayData;
                s.Add(overlays?.Length ?? 0);
                if (overlays == null) continue;
                foreach (var overlay in overlays)
                {
                    var properties = overlay?.colorData?.PropertyBlock?.shaderProperties;
                    s.Add(properties?.Count ?? 0);
                    if (properties == null) continue;
                    foreach (var property in properties)
                    {
                        s.Add(property != null);
                        if (property == null) continue;
                        var type = property.GetType(); s.Text(property.name);
                        if (type == typeof(UMAFloatProperty)) { s.Add(0); s.Add(((UMAFloatProperty)property).Value); }
                        else if (type == typeof(UMAIntProperty)) { s.Add(1); s.Add(((UMAIntProperty)property).Value); }
                        else if (type == typeof(UMAColorProperty)) { s.Add(2); s.Add(((UMAColorProperty)property).Value); }
                        else if (type == typeof(UMAVectorProperty)) { s.Add(3); s.Add(((UMAVectorProperty)property).Value); }
                        else if (type == typeof(UMATextureProperty)) { s.Add(4); s.Asset(((UMATextureProperty)property).Value); }
                        else if (type == typeof(UMAOverlayTransformProperty))
                        {
                            var p = (UMAOverlayTransformProperty)property;
                            s.Add(5); s.Add(p.Translate); s.Add(p.Rotate); s.Add(p.Scale);
                        }
                        else if (type == typeof(UMAMatrixProperty)) { s.Add(6); s.Add(((UMAMatrixProperty)property).Value); }
                        else if (type == typeof(UMAFloatArrayProperty))
                        { s.Add(7); var values = ((UMAFloatArrayProperty)property).Value; s.Add(values?.Length ?? -1); if (values != null) foreach (var value in values) s.Add(value); }
                        else if (type == typeof(UMAVectorArrayProperty))
                        { s.Add(8); var values = ((UMAVectorArrayProperty)property).Value; s.Add(values?.Length ?? -1); if (values != null) foreach (var value in values) s.Add(value); }
                        else if (type == typeof(UMAMatrixArrayProperty))
                        { s.Add(9); var values = ((UMAMatrixArrayProperty)property).Value; s.Add(values?.Length ?? -1); if (values != null) foreach (var value in values) s.Add(value); }
                        else throw new NotSupportedException("Custom/buffer material properties require private material instances.");
                    }
                }
            }
        }

        // Built-in scalar setters have no side effects beyond their target property.
        // Resolve only the last write, and skip properties absent from the shader without
        // entering native code. Custom/array setters keep their original ordered path.
        internal static bool TryApplyScalarMaterialParameters(UMAData data, UMAData.GeneratedMaterial gm, Material material)
        {
            // Preserve the legacy warning path for malformed/uninitialized recipes.
            if (data.umaRecipe == null && gm.umaMaterial.shaderParms != null) return false;
            var layout = GetShaderLayout(material.shader);
            bool compositor = layout.PropertyIds.ContainsKey("_OverlayCount");
            foreach (var fragment in gm.materialFragments)
            {
                if (fragment.overlayData == null) continue;
                for (int o = 0; o < fragment.overlayData.Length; o++)
                {
                    var overlay = fragment.overlayData[o];
                    if (overlay == null || !overlay.colorData.HasProperties) continue;
                    foreach (var property in overlay.colorData.PropertyBlock.shaderProperties)
                    {
                        var type = property?.GetType();
                        if (type != typeof(UMAFloatProperty) && type != typeof(UMAIntProperty) &&
                            type != typeof(UMAColorProperty) && type != typeof(UMAVectorProperty) &&
                            type != typeof(UMAOverlayTransformProperty)) return false;
                        if (type == typeof(UMAOverlayTransformProperty)) continue;
                        bool vector = type == typeof(UMAColorProperty) || type == typeof(UMAVectorProperty);
                        if (!ScalarTypeMatches(layout, property.name, vector) ||
                            (compositor && !ScalarTypeMatches(layout, property.GetPropertyName(o), vector))) return false;
                    }
                }
            }
            var ids = layout.PropertyIds;
            appliedMaterialProperties.Clear();
            var mappings = gm.umaMaterial.shaderParms;
            // Mismatched setter types can be rejected by Unity instead of overwriting
            // an earlier valid value. Keep the ordered path for those unusual inputs.
            if (mappings != null)
                foreach (var mapping in mappings)
                    if (!ScalarTypeMatches(layout, mapping.ParameterName, true)) return false;
            if (mappings != null && data.umaRecipe?.sharedColors != null)
            {
                for (int i = mappings.Length - 1; i >= 0; i--)
                {
                    var mapping = mappings[i];
                    if (mapping.ParameterName == null || !ids.TryGetValue(mapping.ParameterName, out int id)) continue;
                    foreach (var color in data.umaRecipe.sharedColors)
                        if (color.name == mapping.ColorName)
                        {
                            if (appliedMaterialProperties.Add(id)) material.SetColor(id, color.color);
                            break;
                        }
                }
            }
            for (int f = gm.materialFragments.Count - 1; f >= 0; f--)
            {
                var overlays = gm.materialFragments[f].overlayData;
                if (overlays == null) continue;
                for (int o = overlays.Length - 1; o >= 0; o--)
                {
                    var overlay = overlays[o];
                    if (overlay == null || !overlay.colorData.HasProperties) continue;
                    var properties = overlay.colorData.PropertyBlock.shaderProperties;
                    for (int p = properties.Count - 1; p >= 0; p--)
                    {
                        var property = properties[p];
                        if (property.GetType() == typeof(UMAOverlayTransformProperty)) continue;
                        if (compositor) ApplyScalar(property, property.GetPropertyName(o), material, ids);
                        ApplyScalar(property, property.name, material, ids);
                    }
                }
            }
            return true;
        }

        private static bool ScalarTypeMatches(ShaderLayout layout, string name, bool vector)
        {
            if (name == null || !layout.PropertyTypes.TryGetValue(name, out var type)) return true;
            return vector ? type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector :
                type == ShaderPropertyType.Float || type == ShaderPropertyType.Range;
        }

        private static void ApplyScalar(UMAProperty property, string name, Material material, Dictionary<string, int> ids)
        {
            if (name == null || !ids.TryGetValue(name, out int id) || !appliedMaterialProperties.Add(id)) return;
            if (property is UMAFloatProperty f) material.SetFloat(id, f.Value);
            else if (property is UMAIntProperty i) material.SetInt(id, i.Value);
            else if (property is UMAColorProperty c) material.SetColor(id, c.Value);
            else if (property is UMAVectorProperty v) material.SetVector(id, v.Value);
        }
    }
}
