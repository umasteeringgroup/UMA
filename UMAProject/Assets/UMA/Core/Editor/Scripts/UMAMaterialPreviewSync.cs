using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>
    /// Keeps the second pass of an UMAMaterial in step with the Material Preview in the UMAMaterial inspector, which edits the
    /// first-pass material. Properties changed on the first pass are copied to the second-pass material when it has them, except
    /// the ones that set up how each pass renders. It also refreshes a preview avatar's generated materials for both passes.
    /// </summary>
    public sealed class UMAMaterialPreviewSync
    {
        // The two passes of a material differ in these on purpose (for example an opaque or cutout first pass and a
        // transparent second pass), so an edit to the first pass never copies them to the second.
        private static readonly HashSet<string> PassSetupProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "_Surface", "_SurfaceType", "_Blend", "_BlendMode", "_BlendOp", "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha",
            "_AlphaSrcBlend", "_AlphaDstBlend", "_EnableBlendModePreserveSpecularLighting",
            "_ZWrite", "_ZWriteControl", "_ZTest", "_ZTestGBuffer", "_ZTestDepthEqualForOpaque", "_ZTestTransparent", "_TransparentZWrite",
            "_Cull", "_CullMode", "_CullModeForward", "_OpaqueCullMode", "_TransparentCullMode", "_DoubleSidedEnable",
            "_AlphaClip", "_AlphaCutoffEnable", "_AlphaToMask", "_AlphaToMaskInspectorValue", "_CastShadows",
            "_QueueOffset", "_QueueControl", "_RenderQueueType", "_TransparentSortPriority", "_TransparentBackfaceEnable",
            "_TransparentDepthPrepassEnable", "_TransparentDepthPostpassEnable",
        };

        private static readonly string[] PassSetupPrefixes = { "_Stencil", "_BUILTIN_" };

        private struct PropertyValue
        {
            public ShaderPropertyType Type;
            public Vector4 Value;
            public Texture Texture;

            public bool SameAs(PropertyValue other)
            {
                return Type == other.Type && Value == other.Value && Texture == other.Texture;
            }
        }

        private Material watched;
        private Shader watchedShader;
        private readonly Dictionary<string, PropertyValue> snapshot = new Dictionary<string, PropertyValue>(StringComparer.Ordinal);
        private readonly List<string> changed = new List<string>();

        /// <summary>True for properties that set up how a pass renders: surface type, blending, depth, culling, stencil,
        /// alpha clipping, shadow casting and queue. These are never copied from the first pass to the second.</summary>
        public static bool IsPassSetupProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }
            if (PassSetupProperties.Contains(propertyName))
            {
                return true;
            }
            for (int i = 0; i < PassSetupPrefixes.Length; i++)
            {
                if (propertyName.StartsWith(PassSetupPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Forgets the recorded values, so the next <see cref="MirrorChanges"/> only records.</summary>
        public void Reset()
        {
            watched = null;
            watchedShader = null;
            snapshot.Clear();
        }

        /// <summary>
        /// Compares the edited material with the values recorded at the previous call and copies every property that changed
        /// since then to <paramref name="secondPass"/>, when it has that property with the same kind of value and it is not a
        /// pass setup property. A property that has a shader keyword of the same name (NAME, or NAME_ON) in both shaders
        /// also takes that keyword's state. The first call for a material, and the first after its shader changed, only
        /// records its values. Returns how many properties were copied.
        /// </summary>
        public int MirrorChanges(Material edited, Material secondPass)
        {
            if (edited == null || edited.shader == null)
            {
                Reset();
                return 0;
            }
            if (edited != watched || edited.shader != watchedShader)
            {
                Record(edited);
                return 0;
            }

            changed.Clear();
            Shader shader = edited.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                PropertyValue now = Read(edited, name, shader.GetPropertyType(i));
                if (!snapshot.TryGetValue(name, out PropertyValue before) || !before.SameAs(now))
                {
                    changed.Add(name);
                }
                snapshot[name] = now;
            }

            if (secondPass == null || secondPass == edited || secondPass.shader == null)
            {
                return 0;
            }

            int copied = 0;
            for (int i = 0; i < changed.Count; i++)
            {
                string name = changed[i];
                if (IsPassSetupProperty(name) || !secondPass.HasProperty(name))
                {
                    continue;
                }
                if (CopyProperty(edited, secondPass, name))
                {
                    CopyMatchingKeyword(edited, secondPass, name);
                    copied++;
                }
            }
            return copied;
        }

        /// <summary>
        /// Refreshes a preview avatar's generated materials from the UMAMaterial's templates: first-pass materials from
        /// <see cref="UMAMaterial.material"/> and second-pass materials from <see cref="UMAMaterial.secondPass"/>. The textures of
        /// the UMAMaterial's channels stay as the generator set them. Asset materials are never changed. Returns how many
        /// materials were refreshed.
        /// </summary>
        public static int SyncGeneratedMaterials(UMAMaterial source, IList<UMAData.GeneratedMaterial> generated)
        {
            if (source == null || generated == null)
            {
                return 0;
            }
            Material firstTemplate = source.material;
            Material secondTemplate = source.secondPass;
            int refreshed = 0;
            for (int i = 0; i < generated.Count; i++)
            {
                UMAData.GeneratedMaterial gm = generated[i];
                if (gm == null || gm.umaMaterial == null)
                {
                    continue;
                }
                UMAMaterial owner = gm.umaMaterial;
                if (firstTemplate != null && (owner == source || owner.material == firstTemplate) &&
                    RefreshFromTemplate(firstTemplate, gm.material, owner))
                {
                    refreshed++;
                }
                if (secondTemplate != null && (owner == source || owner.secondPass == secondTemplate) &&
                    gm.secondPassMaterial != gm.material &&
                    RefreshFromTemplate(secondTemplate, gm.secondPassMaterial, owner))
                {
                    refreshed++;
                }
            }
            return refreshed;
        }

        private static bool RefreshFromTemplate(Material template, Material target, UMAMaterial owner)
        {
            if (target == null || target == template || target.shader != template.shader || EditorUtility.IsPersistent(target))
            {
                return false;
            }

            List<KeyValuePair<string, Texture>> channelTextures = null;
            if (owner.channels != null)
            {
                for (int c = 0; c < owner.channels.Length; c++)
                {
                    string property = owner.channels[c].materialPropertyName;
                    if (string.IsNullOrEmpty(property) || !target.HasProperty(property))
                    {
                        continue;
                    }
                    Texture texture = target.GetTexture(property);
                    if (texture != null)
                    {
                        channelTextures ??= new List<KeyValuePair<string, Texture>>();
                        channelTextures.Add(new KeyValuePair<string, Texture>(property, texture));
                    }
                }
            }

            target.CopyMatchingPropertiesFromMaterial(template);

            if (channelTextures != null)
            {
                foreach (KeyValuePair<string, Texture> pair in channelTextures)
                {
                    target.SetTexture(pair.Key, pair.Value);
                }
            }
            return true;
        }

        private void Record(Material material)
        {
            watched = material;
            watchedShader = material.shader;
            snapshot.Clear();
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                snapshot[name] = Read(material, name, shader.GetPropertyType(i));
            }
        }

        private static PropertyValue Read(Material material, string name, ShaderPropertyType type)
        {
            var value = new PropertyValue { Type = type };
            switch (type)
            {
                case ShaderPropertyType.Color:
                    value.Value = material.GetColor(name);
                    break;
                case ShaderPropertyType.Vector:
                    value.Value = material.GetVector(name);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    value.Value = new Vector4(material.GetFloat(name), 0f, 0f, 0f);
                    break;
                case ShaderPropertyType.Int:
                    value.Value = new Vector4(material.GetInteger(name), 0f, 0f, 0f);
                    break;
                case ShaderPropertyType.Texture:
                    value.Texture = material.GetTexture(name);
                    Vector2 scale = material.GetTextureScale(name);
                    Vector2 offset = material.GetTextureOffset(name);
                    value.Value = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    break;
            }
            return value;
        }

        private static bool CopyProperty(Material from, Material to, string name)
        {
            Shader fromShader = from.shader;
            Shader toShader = to.shader;
            int fromIndex = fromShader.FindPropertyIndex(name);
            int toIndex = toShader.FindPropertyIndex(name);
            if (fromIndex < 0 || toIndex < 0)
            {
                return false;
            }
            ShaderPropertyType fromType = fromShader.GetPropertyType(fromIndex);
            ShaderPropertyType toType = toShader.GetPropertyType(toIndex);
            switch (fromType)
            {
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    if (toType == ShaderPropertyType.Color)
                    {
                        to.SetColor(name, from.GetColor(name));
                        return true;
                    }
                    if (toType == ShaderPropertyType.Vector)
                    {
                        to.SetVector(name, from.GetVector(name));
                        return true;
                    }
                    return false;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    if (toType == ShaderPropertyType.Float || toType == ShaderPropertyType.Range)
                    {
                        to.SetFloat(name, from.GetFloat(name));
                        return true;
                    }
                    return false;
                case ShaderPropertyType.Int:
                    if (toType == ShaderPropertyType.Int)
                    {
                        to.SetInteger(name, from.GetInteger(name));
                        return true;
                    }
                    return false;
                case ShaderPropertyType.Texture:
                    if (toType == ShaderPropertyType.Texture && fromShader.GetPropertyTextureDimension(fromIndex) == toShader.GetPropertyTextureDimension(toIndex))
                    {
                        to.SetTexture(name, from.GetTexture(name));
                        to.SetTextureScale(name, from.GetTextureScale(name));
                        to.SetTextureOffset(name, from.GetTextureOffset(name));
                        return true;
                    }
                    return false;
            }
            return false;
        }

        private static void CopyMatchingKeyword(Material from, Material to, string propertyName)
        {
            string upper = propertyName.ToUpperInvariant();
            CopyKeyword(from, to, propertyName);
            if (upper != propertyName)
            {
                CopyKeyword(from, to, upper);
            }
            CopyKeyword(from, to, upper + "_ON");
        }

        private static void CopyKeyword(Material from, Material to, string keywordName)
        {
            LocalKeyword fromKeyword = from.shader.keywordSpace.FindKeyword(keywordName);
            LocalKeyword toKeyword = to.shader.keywordSpace.FindKeyword(keywordName);
            if (!fromKeyword.isValid || !toKeyword.isValid)
            {
                return;
            }
            to.SetKeyword(toKeyword, from.IsKeywordEnabled(fromKeyword));
        }
    }
}
