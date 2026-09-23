using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.CharacterSystem
{
    /// <summary>A reusable appearance preset. DNA and colors are selective; wardrobe is a complete replacement.</summary>
    [CreateAssetMenu(menuName = "UMA/Avatar Preset", fileName = "UMAPreset")]
    public class UMAPreset : ScriptableObject
    {
        public AvatarDefinition Definition;
        public Texture2D Icon;
        // Keep recipes reachable in builds, including recipes not loaded in the global index.
        public UMATextRecipe[] WardrobeRecipes = Array.Empty<UMATextRecipe>();

        /// <summary>Convert legacy JSON without modifying an avatar or inventing a race or icon.</summary>
        public static AvatarDefinition ConvertLegacyJson(string json, string raceName = null)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("The preset file is empty.", nameof(json));
            if (!HasLegacyRootField(json)) throw new ArgumentException("This is not a legacy UMA preset file.", nameof(json));
            var legacy = new LegacyUMAPreset();
            JsonUtility.FromJsonOverwrite(json, legacy);
            if (legacy == null || (legacy.PredefinedDNA == null && legacy.DefaultColors == null && legacy.DefaultWardrobe == null))
                throw new ArgumentException("This is not a legacy UMA preset file.", nameof(json));
            var dna = new List<DnaDef>();
            var colors = new List<SharedColorDef>();
            var wardrobe = new List<string>();
            // JsonUtility does not apply FormerlySerializedAs when importing these old JSON fields.
            var aliases = JsonUtility.FromJson<LegacyPresetColorAliases>(json);
            if (legacy.DefaultColors != null && aliases?.DefaultColors?.Colors != null &&
                (legacy.DefaultColors.Colors == null || legacy.DefaultColors.Colors.Count == 0))
            {
                legacy.DefaultColors.Colors = new List<DynamicCharacterAvatar.ColorValue>();
                foreach (var oldColor in aliases.DefaultColors.Colors)
                {
                    if (oldColor == null) continue;
                    var color = !string.IsNullOrEmpty(oldColor.Name)
                        ? new DynamicCharacterAvatar.ColorValue(oldColor.Name, oldColor.Color)
                        : new DynamicCharacterAvatar.ColorValue(oldColor);
                    if (!string.IsNullOrEmpty(oldColor.Name)) color.MetallicGloss = oldColor.MetallicGloss;
                    legacy.DefaultColors.Colors.Add(color);
                }
            }
            if (legacy.PredefinedDNA?.PreloadValues != null)
                foreach (var value in legacy.PredefinedDNA.PreloadValues)
                    if (value != null && !string.IsNullOrEmpty(value.Name)) dna.Add(new DnaDef(value.Name, value.Value));
            if (legacy.DefaultColors?.Colors != null)
                foreach (var color in legacy.DefaultColors.Colors)
                {
                    if (color == null) continue;
                    string name = color.Name; // Also migrates the original Name/Color/MetallicGloss fields.
                    if (string.IsNullOrEmpty(name)) continue;
                    var converted = new SharedColorDef(name, color.channelCount);
                    converted.channels = new ColorDef[color.channelCount];
                    for (int i = 0; i < color.channelCount; i++)
                        converted.channels[i] = new ColorDef(i, ColorDef.ToUInt(color.channelMask[i]),
                            ColorDef.ToUInt(color.channelAdditiveMask[i]));
                    if (color.HasProperties) converted.shaderParms = color.PropertyBlock.GetPropertyStrings();
                    colors.Add(converted);
                }
            if (legacy.DefaultWardrobe != null && legacy.DefaultWardrobe.loadDefaultRecipes && legacy.DefaultWardrobe.recipes != null)
                foreach (var recipe in legacy.DefaultWardrobe.recipes)
                    if (recipe != null && recipe._enabledInDefaultWardrobe && !string.IsNullOrEmpty(recipe._recipeName) && !wardrobe.Contains(recipe._recipeName))
                        wardrobe.Add(recipe._recipeName);
            return new AvatarDefinition { RaceName = raceName, Dna = dna.ToArray(), Colors = colors.ToArray(), Wardrobe = wardrobe.ToArray() };
        }

        private static bool HasLegacyRootField(string json)
        {
            int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']') { depth--; continue; }
                if (c != '"') continue;
                int start = ++i;
                while (i < json.Length)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') break;
                    i++;
                }
                if (i >= json.Length) return false;
                if (depth != 1) continue;
                string key = json.Substring(start, i - start);
                int next = i + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (next < json.Length && json[next] == ':' &&
                    (key == "PredefinedDNA" || key == "DefaultColors" || key == "DefaultWardrobe")) return true;
            }
            return false;
        }

        public void ApplyTo(DynamicCharacterAvatar avatar, bool rebuild = true)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            avatar.ApplyPreset(this, rebuild);
        }
    }

    // Retains import support for previously exported .umapreset JSON files.
    [Serializable]
    internal class LegacyPresetColorAliases
    {
        public LegacyColorAliasList DefaultColors;
    }

    [Serializable]
    internal class LegacyColorAliasList
    {
        public LegacyOriginalColor[] Colors;
    }

    [Serializable]
    internal class LegacyOriginalColor : OverlayColorData
    {
        public string Name;
        public Color Color = Color.white;
        public Color MetallicGloss;
    }

    [Serializable]
    internal class LegacyUMAPreset
    {
        public UMAPredefinedDNA PredefinedDNA;
        public DynamicCharacterAvatar.WardrobeRecipeList DefaultWardrobe;
        public DynamicCharacterAvatar.ColorValueList DefaultColors;
    }
}
