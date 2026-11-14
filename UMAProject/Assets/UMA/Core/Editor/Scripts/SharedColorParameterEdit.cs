using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>The kind of value a color parameter holds, taken from the shader property it targets.</summary>
    public enum ColorParameterKind { Float, Int, Color, Vector }

    /// <summary>Where a color parameter's new value comes from.</summary>
    public enum ColorParameterValueSource
    {
        /// <summary>The same value on every color.</summary>
        FixedValue,
        /// <summary>A parameter the color already has, multiplied.</summary>
        ScaledParameter
    }

    /// <summary>What to do with a color that does not have the source parameter.</summary>
    public enum MissingSourceAction
    {
        /// <summary>Use the material's value of the source parameter, multiplied: what the color showed so far.</summary>
        UseMaterialValue,
        /// <summary>Use the fixed value, not multiplied.</summary>
        UseFixedValue,
        /// <summary>Leave the color unchanged.</summary>
        SkipColor
    }

    /// <summary>One edit to apply to every color of one or more shared color tables.</summary>
    [Serializable]
    public sealed class ColorParameterEdit
    {
        public string parameterName = string.Empty;
        public ColorParameterKind kind;
        public ColorParameterValueSource source;
        public float floatValue;
        public int intValue;
        public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public string sourceParameter = string.Empty;
        public float multiplier = 1f;
        public MissingSourceAction whenSourceMissing;
    }

    /// <summary>The planned result of a <see cref="ColorParameterEdit"/> for one color.</summary>
    public sealed class ColorParameterChange
    {
        public SharedColorTable Table;
        public int ColorIndex;
        public string ColorName;
        public string Before;
        public string After;
        /// <summary>Why the color is skipped, or a remark on how its value was found. Empty when there is nothing to say.</summary>
        public string Note = string.Empty;
        /// <summary>The property to set, or null when the color is skipped.</summary>
        public UMAProperty NewProperty;
        public bool Skipped => NewProperty == null;
        public bool Unchanged;
    }

    /// <summary>
    /// Adds or updates one shader parameter on every color of shared color tables, with a fixed value or with a
    /// parameter the color already has, multiplied.
    /// </summary>
    public static class SharedColorParameterEditor
    {
        public static bool TryGetKind(ShaderPropertyType type, out ColorParameterKind kind)
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: kind = ColorParameterKind.Float; return true;
                case ShaderPropertyType.Int: kind = ColorParameterKind.Int; return true;
                case ShaderPropertyType.Color: kind = ColorParameterKind.Color; return true;
                case ShaderPropertyType.Vector: kind = ColorParameterKind.Vector; return true;
                default: kind = ColorParameterKind.Float; return false;
            }
        }

        /// <summary>True when a value of <paramref name="from"/> can feed a parameter of <paramref name="to"/>.</summary>
        public static bool AreCompatible(ColorParameterKind from, ColorParameterKind to)
        {
            bool fromScalar = from == ColorParameterKind.Float || from == ColorParameterKind.Int;
            bool toScalar = to == ColorParameterKind.Float || to == ColorParameterKind.Int;
            return fromScalar ? toScalar : from == to;
        }

        /// <summary>The material's value of a parameter, or false when the material has no supported property of that name.</summary>
        public static bool TryGetMaterialValue(Material material, string parameterName, out ColorParameterKind kind, out Vector4 value)
        {
            kind = ColorParameterKind.Float;
            value = Vector4.zero;
            if (material == null || material.shader == null || string.IsNullOrEmpty(parameterName)) return false;
            int index = material.shader.FindPropertyIndex(parameterName);
            if (index < 0 || !TryGetKind(material.shader.GetPropertyType(index), out kind)) return false;
            switch (kind)
            {
                case ColorParameterKind.Float: value = new Vector4(material.GetFloat(parameterName), 0, 0, 0); break;
                case ColorParameterKind.Int: value = new Vector4(material.GetInteger(parameterName), 0, 0, 0); break;
                case ColorParameterKind.Color: value = material.GetColor(parameterName); break;
                case ColorParameterKind.Vector: value = material.GetVector(parameterName); break;
            }
            return true;
        }

        /// <summary>Works out the new value for every color, without changing anything.</summary>
        public static List<ColorParameterChange> Preview(IEnumerable<SharedColorTable> tables, ColorParameterEdit edit, Material material)
        {
            var changes = new List<ColorParameterChange>();
            if (tables == null || edit == null) return changes;
            foreach (SharedColorTable table in Distinct(tables))
            {
                if (table.colors == null) continue;
                for (int i = 0; i < table.colors.Length; i++)
                {
                    OverlayColorData color = table.colors[i];
                    if (color == null) continue;
                    changes.Add(Plan(table, i, color, edit, material));
                }
            }
            return changes;
        }

        /// <summary>
        /// Applies the edit to every color, with undo, and saves only the tables that changed.
        /// Returns the number of colors that were given a new or different value.
        /// </summary>
        public static int Apply(IEnumerable<SharedColorTable> tables, ColorParameterEdit edit, Material material)
        {
            List<ColorParameterChange> changes = Preview(tables, edit, material);
            var touched = new List<SharedColorTable>();
            foreach (ColorParameterChange change in changes)
                if (!change.Skipped && !change.Unchanged && !touched.Contains(change.Table)) touched.Add(change.Table);
            if (touched.Count == 0) return 0;

            Undo.RecordObjects(touched.ToArray(), "Manage Color Parameters");
            int count = 0;
            foreach (ColorParameterChange change in changes)
            {
                if (change.Skipped || change.Unchanged) continue;
                OverlayColorData color = change.Table.colors[change.ColorIndex];
                if (color.PropertyBlock == null) color.PropertyBlock = new UMAMaterialPropertyBlock();
                // SetProperty also drops duplicates and a property of the same name but another type.
                color.PropertyBlock.SetProperty(change.NewProperty.Clone());
                count++;
            }
            foreach (SharedColorTable table in touched)
            {
                EditorUtility.SetDirty(table);
                if (EditorUtility.IsPersistent(table)) AssetDatabase.SaveAssetIfDirty(table);
            }
            return count;
        }

        private static ColorParameterChange Plan(SharedColorTable table, int index, OverlayColorData color, ColorParameterEdit edit, Material material)
        {
            var change = new ColorParameterChange { Table = table, ColorIndex = index, ColorName = string.IsNullOrEmpty(color.name) ? $"Color {index + 1}" : color.name };
            UMAProperty current = Find(color, edit.parameterName, out _);
            change.Before = current != null ? Describe(current) : "(none)";
            if (string.IsNullOrEmpty(edit.parameterName))
            {
                change.Note = "No parameter chosen.";
                change.After = change.Before;
                return change;
            }

            Vector4 value;
            if (edit.source == ColorParameterValueSource.FixedValue)
            {
                value = FixedValue(edit);
            }
            else
            {
                UMAProperty sourceProperty = Find(color, edit.sourceParameter, out bool sourceReadable);
                if (sourceProperty != null && sourceReadable && TryRead(sourceProperty, out ColorParameterKind sourceKind, out Vector4 sourceValue))
                {
                    if (!AreCompatible(sourceKind, edit.kind))
                    {
                        change.Note = $"{edit.sourceParameter} is a {sourceKind}, which cannot fill a {edit.kind}.";
                        change.After = change.Before;
                        return change;
                    }
                    value = Scale(sourceValue, edit.kind, edit.multiplier);
                }
                else if (sourceProperty != null)
                {
                    change.Note = $"{edit.sourceParameter} has a type this tool cannot read.";
                    change.After = change.Before;
                    return change;
                }
                else
                {
                    switch (edit.whenSourceMissing)
                    {
                        case MissingSourceAction.UseMaterialValue:
                            if (!TryGetMaterialValue(material, edit.sourceParameter, out ColorParameterKind materialKind, out Vector4 materialValue) || !AreCompatible(materialKind, edit.kind))
                            {
                                change.Note = $"No {edit.sourceParameter} on the color or the material.";
                                change.After = change.Before;
                                return change;
                            }
                            value = Scale(materialValue, edit.kind, edit.multiplier);
                            change.Note = $"No {edit.sourceParameter}: used the material's value.";
                            break;
                        case MissingSourceAction.UseFixedValue:
                            value = FixedValue(edit);
                            change.Note = $"No {edit.sourceParameter}: used the fixed value.";
                            break;
                        default:
                            change.Note = $"No {edit.sourceParameter}: skipped.";
                            change.After = change.Before;
                            return change;
                    }
                }
            }

            change.NewProperty = Create(edit.parameterName, edit.kind, value);
            change.After = Describe(change.NewProperty);
            change.Unchanged = current != null && current.GetType() == change.NewProperty.GetType() && change.After == change.Before && CountNamed(color, edit.parameterName) == 1;
            return change;
        }

        private static Vector4 FixedValue(ColorParameterEdit edit)
        {
            switch (edit.kind)
            {
                case ColorParameterKind.Int: return new Vector4(edit.intValue, 0, 0, 0);
                case ColorParameterKind.Color: return edit.colorValue;
                case ColorParameterKind.Vector: return edit.vectorValue;
                default: return new Vector4(edit.floatValue, 0, 0, 0);
            }
        }

        private static Vector4 Scale(Vector4 value, ColorParameterKind kind, float multiplier)
        {
            // A color keeps its alpha: the multiplier brightens or darkens it.
            if (kind == ColorParameterKind.Color) return new Vector4(value.x * multiplier, value.y * multiplier, value.z * multiplier, value.w);
            if (kind == ColorParameterKind.Vector) return value * multiplier;
            return new Vector4(value.x * multiplier, 0, 0, 0);
        }

        private static UMAProperty Create(string name, ColorParameterKind kind, Vector4 value)
        {
            switch (kind)
            {
                case ColorParameterKind.Int: return new UMAIntProperty { name = name, Value = Mathf.RoundToInt(value.x) };
                case ColorParameterKind.Color: return new UMAColorProperty { name = name, Value = value };
                case ColorParameterKind.Vector: return new UMAVectorProperty { name = name, Value = value };
                default: return new UMAFloatProperty { name = name, Value = value.x };
            }
        }

        private static bool TryRead(UMAProperty property, out ColorParameterKind kind, out Vector4 value)
        {
            switch (property)
            {
                case UMAFloatProperty f: kind = ColorParameterKind.Float; value = new Vector4(f.Value, 0, 0, 0); return true;
                case UMAIntProperty n: kind = ColorParameterKind.Int; value = new Vector4(n.Value, 0, 0, 0); return true;
                case UMAColorProperty c: kind = ColorParameterKind.Color; value = c.Value; return true;
                case UMAVectorProperty v: kind = ColorParameterKind.Vector; value = v.Value; return true;
                default: kind = ColorParameterKind.Float; value = Vector4.zero; return false;
            }
        }

        // The first readable property of that name, else the first of any type. Apply replaces every property of
        // that name with a single one.
        private static UMAProperty Find(OverlayColorData color, string name, out bool readable)
        {
            readable = false;
            List<UMAProperty> properties = color.PropertyBlock?.shaderProperties;
            if (properties == null || string.IsNullOrEmpty(name)) return null;
            UMAProperty any = null;
            foreach (UMAProperty p in properties)
            {
                if (p == null || p.name != name) continue;
                if (TryRead(p, out _, out _)) { readable = true; return p; }
                any ??= p;
            }
            return any;
        }

        private static int CountNamed(OverlayColorData color, string name)
        {
            int count = 0;
            List<UMAProperty> properties = color.PropertyBlock?.shaderProperties;
            if (properties != null) foreach (UMAProperty p in properties) if (p != null && p.name == name) count++;
            return count;
        }

        public static string Describe(UMAProperty property)
        {
            switch (property)
            {
                case UMAFloatProperty f: return f.Value.ToString("0.###", CultureInfo.InvariantCulture);
                case UMAIntProperty n: return n.Value.ToString(CultureInfo.InvariantCulture);
                case UMAColorProperty c: return "#" + ColorUtility.ToHtmlStringRGBA(c.Value);
                case UMAVectorProperty v: return v.Value.ToString("0.###");
                case null: return "(none)";
                default: return property.GetType().Name;
            }
        }

        private static IEnumerable<SharedColorTable> Distinct(IEnumerable<SharedColorTable> tables)
        {
            var seen = new HashSet<SharedColorTable>();
            foreach (SharedColorTable table in tables)
                if (table != null && seen.Add(table)) yield return table;
        }
    }
}
