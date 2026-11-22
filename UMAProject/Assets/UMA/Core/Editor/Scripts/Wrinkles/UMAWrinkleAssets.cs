using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// The assets that take the wrinkle maps through the UMA atlas: a skin UMA Material with the two wrinkle channels, and
    /// an overlay that carries the maps in those channels (and nothing in the others).
    /// </summary>
    public static class UMAWrinkleAssets
    {
        public const string UMA3SourceUMAMaterialPath = "Assets/UMA/SRP/Shaders/Skin/Examples/UMA3_SkinRealistic_Natural.asset";
        public const string UMA3UMAMaterialPath = "Assets/UMA/SRP/Shaders/Skin/Examples/UMA3_SkinRealisticSSS_Wrinkles.asset";
        public const string UMA3MaterialPath = "Assets/UMA/SRP/Shaders/Skin/Examples/UMA3_SkinRealisticSSS_Wrinkles.mat";
        public const string UMA3OverlayPath = "Assets/UMA/UMA3/Overlays/" + UMAWrinkleZones.UMA3OverlayName + ".asset";
        const string CopySuffix = "_Wrinkles";

        /// <summary>
        /// The two channels, appended after the skin's own channels. Linear data at full atlas resolution, never
        /// compressed: block compression moves zone ids off their levels and bends the wrinkle normals.
        /// </summary>
        public static UMAMaterial.MaterialChannel WrinkleChannel(string property)
        {
            return new UMAMaterial.MaterialChannel
            {
                channelType = UMAMaterial.ChannelType.Texture,
                textureFormat = RenderTextureFormat.ARGB32,
                materialPropertyName = property,
                sourceTextureName = property,
                DownSample = 1,
                NeverCompress = true,
            };
        }

        /// <summary>
        /// A copy of <paramref name="source"/> that uses <paramref name="material"/> and has the wrinkle channels after its own
        /// (channels it already has are kept). The skin's own overlays keep working: they have no textures for the new
        /// channels, so those stay empty outside the head.
        /// </summary>
        public static UMAMaterial CreateWrinkleUMAMaterial(UMAMaterial source, Material material, string name)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (material == null) throw new ArgumentNullException(nameof(material));
            var uma = UnityEngine.Object.Instantiate(source);
            uma.name = name;
            uma.objectName = name;
            uma.material = material;
            uma.MaterialName = material.name;
            uma.ShaderName = material.shader.name;
            var channels = new System.Collections.Generic.List<UMAMaterial.MaterialChannel>(source.channels ?? new UMAMaterial.MaterialChannel[0]);
            foreach (var property in new[] { UMAWrinkleZones.WrinkleMapProperty, UMAWrinkleZones.ControlMapProperty })
                if (channels.FindIndex(c => c.materialPropertyName == property) < 0) channels.Add(WrinkleChannel(property));
            uma.channels = channels.ToArray();
            return uma;
        }

        /// <summary>Index of a wrinkle channel in <paramref name="material"/>, or -1.</summary>
        public static int ChannelIndex(UMAMaterial material, string property)
        {
            if (material == null || material.channels == null) return -1;
            for (int i = 0; i < material.channels.Length; i++)
                if (material.channels[i].materialPropertyName == property) return i;
            return -1;
        }

        /// <summary>
        /// Makes (or updates) the wrinkle overlay: the maps in the material's two wrinkle channels and no texture in the
        /// others, so as an additional overlay it changes nothing but the wrinkle channels. Returns null when
        /// <paramref name="material"/> has no wrinkle channels.
        /// </summary>
        public static OverlayDataAsset CreateOrUpdateOverlay(string path, UMAMaterial material, Texture wrinkleMap, Texture controlMap)
        {
            int wrinkleChannel = ChannelIndex(material, UMAWrinkleZones.WrinkleMapProperty);
            int controlChannel = ChannelIndex(material, UMAWrinkleZones.ControlMapProperty);
            if (wrinkleChannel < 0 || controlChannel < 0) return null;
            var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(path);
            bool created = overlay == null;
            if (created) overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            int count = material.channels.Length;
            var textures = new Texture[count];
            textures[wrinkleChannel] = wrinkleMap;
            textures[controlChannel] = controlMap;
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = textures[i] != null ? textures[i].name : "";

            bool changed = created || overlay.material != material || overlay.textureList == null || overlay.textureList.Length != count;
            for (int i = 0; !changed && i < count; i++) changed = overlay.textureList[i] != textures[i];
            if (!changed) return overlay;

            if (!created) Undo.RecordObject(overlay, "Update Wrinkle Overlay");
            string name = Path.GetFileNameWithoutExtension(path);
            overlay.name = name;
            overlay._oldOverlayName = name;
            overlay.textureList = textures;
            overlay.textureNames = names;
            overlay.overlayBlend = new OverlayDataAsset.OverlayBlend[count];
            overlay.material = material;
            overlay.materialName = material.name;
            overlay.tags = new[] { "Head", "Wrinkles" };
            if (created) AssetDatabase.CreateAsset(overlay, path);
            else EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);
            return overlay;
        }

        /// <summary>
        /// Makes the UMA 3 wrinkle assets that are missing: the wrinkle copy of the UMA 3 skin (Natural) and the head's
        /// wrinkle overlay. Existing assets are only updated when they point at other maps or another material.
        /// </summary>
        public static OverlayDataAsset EnsureUMA3Assets(out string message)
        {
            var wrinkleMap = AssetDatabase.LoadAssetAtPath<Texture2D>(UMAWrinkleMapBaker.UMA3WrinkleMapPath);
            var controlMap = AssetDatabase.LoadAssetAtPath<Texture2D>(UMAWrinkleMapBaker.UMA3ControlMapPath);
            if (wrinkleMap == null || controlMap == null)
            {
                message = "The wrinkle maps have not been baked yet. Use UMA > Skin > Bake Procedural Wrinkle Maps (UMA 3 Head) first.";
                return null;
            }
            var uma = AssetDatabase.LoadAssetAtPath<UMAMaterial>(UMA3UMAMaterialPath);
            if (uma == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<UMAMaterial>(UMA3SourceUMAMaterialPath);
                if (source == null || source.material == null)
                {
                    message = "The UMA 3 skin UMA Material was not found at " + UMA3SourceUMAMaterialPath + ".";
                    return null;
                }
                var material = AssetDatabase.LoadAssetAtPath<Material>(UMA3MaterialPath);
                if (material == null)
                {
                    material = UMARealisticSkinUtility.CreateWrinkleMaterialCopy(source.material);
                    material.name = Path.GetFileNameWithoutExtension(UMA3MaterialPath);
                    AssetDatabase.CreateAsset(material, UMA3MaterialPath);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                uma = CreateWrinkleUMAMaterial(source, material, Path.GetFileNameWithoutExtension(UMA3UMAMaterialPath));
                AssetDatabase.CreateAsset(uma, UMA3UMAMaterialPath);
                AssetDatabase.SaveAssetIfDirty(uma);
            }
            var overlay = CreateOrUpdateOverlay(UMA3OverlayPath, uma, wrinkleMap, controlMap);
            message = overlay != null ? "Wrinkle overlay: " + UMA3OverlayPath : "The UMA Material " + UMA3UMAMaterialPath + " has no wrinkle channels.";
            return overlay;
        }

        /// <summary>
        /// Rules of the UMA 3 profile, from the facial muscles each expression moves: brow raise (frontalis) folds the
        /// forehead, brows in and down (corrugator, procerus) the glabella and nose root, a squint (orbicularis oculi)
        /// the crow's feet, a smile (zygomaticus) the nasolabial fold, a pucker (orbicularis oris, mentalis) the lips and
        /// chin. Movements that pull skin the other way stretch the zone (negative amounts).
        /// </summary>
        public static UMAWrinkleProfile.Rule[] UMA3ProfileRules()
        {
            const UMAWrinkleZone FL = UMAWrinkleZone.ForeheadLeft, FR = UMAWrinkleZone.ForeheadRight, Gl = UMAWrinkleZone.Glabella,
                NR = UMAWrinkleZone.NoseRoot, Bu = UMAWrinkleZone.BunnyLines, CL = UMAWrinkleZone.CrowsFeetLeft, CR = UMAWrinkleZone.CrowsFeetRight,
                NL = UMAWrinkleZone.NasolabialLeft, NRt = UMAWrinkleZone.NasolabialRight, ML = UMAWrinkleZone.MouthCornerLeft,
                MR = UMAWrinkleZone.MouthCornerRight, Li = UMAWrinkleZone.LipLines, Ch = UMAWrinkleZone.Chin;
            var rules = new System.Collections.Generic.List<UMAWrinkleProfile.Rule>();
            void Add(string expression, UMAWrinkleZone zone, float positive, float negative) => rules.Add(new UMAWrinkleProfile.Rule(expression, zone, positive, negative));
            foreach (var side in new[] { ("left", FL, CL, NL, ML), ("right", FR, CR, NRt, MR) })
            {
                string s = side.Item1;
                // Brow up folds the forehead; brow down stretches it and pushes skin into the glabella and nose root.
                Add(s + "BrowUp_Down", side.Item2, 1f, -0.5f);
                // The frontalis is one sheet: one brow up also folds the other half a little, so the lines fade across
                // the midline instead of stopping where the two forehead zones meet.
                Add(s + "BrowUp_Down", side.Item2 == FL ? FR : FL, 0.2f, 0f);
                Add(s + "BrowUp_Down", Gl, 0f, 0.3f);
                Add(s + "BrowUp_Down", NR, 0f, 0.25f);
                // Squint (negative) gathers the eye corner; a puffed cheek stretches the fold and the corner.
                Add(s + "CheekPuff_Squint", side.Item3, -0.2f, 1f);
                Add(s + "CheekPuff_Squint", side.Item4, -0.6f, 0.3f);
                Add(s + "CheekPuff_Squint", side.Item5, -0.3f, 0f);
                // Closing the eye tightens its corner a little; opening it wide stretches it.
                Add(s + "EyeOpen_Close", side.Item3, -0.2f, 0.2f);
                // Smile deepens the nasolabial fold and lifts the cheek into the crow's feet; a frown folds the corner and chin.
                Add(s + "MouthSmile_Frown", side.Item4, 1f, -0.3f);
                Add(s + "MouthSmile_Frown", side.Item3, 0.4f, 0f);
                Add(s + "MouthSmile_Frown", side.Item5, 0.3f, 0.7f);
                Add(s + "MouthSmile_Frown", Ch, 0f, 0.3f);
                // Upper lip raise (levator) deepens the upper fold and the bunny lines.
                Add(s + "UpperLipUp_Down", side.Item4, 0.5f, -0.3f);
                Add(s + "UpperLipUp_Down", Bu, 0.25f, 0f);
                // Lower lip pushed up (mentalis) dimples the chin.
                Add(s + "LowerLipUp_Down", Ch, 0.5f, -0.3f);
                Add(s + "LowerLipUp_Down", Li, 0.2f, 0f);
            }
            // Inner brows: raised they fold the middle of the forehead; lowered they fold the glabella and nose root.
            Add("midBrowUp_Down", FL, 0.6f, -0.4f);
            Add("midBrowUp_Down", FR, 0.6f, -0.4f);
            Add("midBrowUp_Down", Gl, 0f, 0.5f);
            Add("midBrowUp_Down", NR, 0f, 0.7f);
            Add("browsIn", Gl, 1f, -0.5f);
            Add("browsIn", NR, 0.3f, 0f);
            Add("noseSneer", Bu, 1f, 0f);
            Add("noseSneer", NR, 0.5f, 0f);
            Add("noseSneer", Gl, 0.2f, 0f);
            Add("noseSneer", NL, 0.4f, 0f);
            Add("noseSneer", NRt, 0.4f, 0f);
            // Pucker (positive) gathers the lips and chin and smooths the folds.
            Add("mouthNarrow_Pucker", Li, 1f, 0f);
            Add("mouthNarrow_Pucker", Ch, 0.5f, 0f);
            Add("mouthNarrow_Pucker", ML, 0.4f, 0f);
            Add("mouthNarrow_Pucker", MR, 0.4f, 0f);
            Add("mouthNarrow_Pucker", NL, -0.3f, 0f);
            Add("mouthNarrow_Pucker", NRt, -0.3f, 0f);
            // An open jaw stretches the chin, lips and folds.
            Add("jawOpen_Close", Ch, -0.4f, 0f);
            Add("jawOpen_Close", Li, -0.4f, 0f);
            Add("jawOpen_Close", NL, -0.2f, 0f);
            Add("jawOpen_Close", NRt, -0.2f, 0f);
            // Whole-face presets move the face themselves, not the channels above, so they need rules of their own.
            // Their values only go up from neutral. Amounts read from renders of each preset on the UMA 3 head.
            void Both(string expression, UMAWrinkleZone left, UMAWrinkleZone right, float amount) { Add(expression, left, amount, 0f); Add(expression, right, amount, 0f); }
            Both("HappyFace", NL, NRt, 1f); Both("HappyFace", CL, CR, 0.8f); Both("HappyFace", ML, MR, 0.3f); Add("HappyFace", Bu, 0.2f, 0f);
            Both("JovialFace", NL, NRt, 0.8f); Both("JovialFace", CL, CR, 1f); Both("JovialFace", FL, FR, 0.3f);
            Both("SadFace", ML, MR, 0.7f); Add("SadFace", Ch, 0.6f, 0f); Add("SadFace", Gl, 0.3f, 0f); Both("SadFace", FL, FR, 0.4f);
            Both("SourFace", FL, FR, 0.6f); Add("SourFace", Gl, 0.3f, 0f); Both("SourFace", ML, MR, 0.2f);
            Both("DespairFace", FL, FR, 0.7f); Both("DespairFace", NL, NRt, -0.3f); Add("DespairFace", Ch, -0.5f, 0f); Add("DespairFace", Li, -0.5f, 0f);
            Add("StupidFace", Gl, 0.3f, 0f); Add("StupidFace", Ch, -0.3f, 0f); Add("StupidFace", Li, -0.3f, 0f);
            Both("YawnFace", CL, CR, 0.7f); Add("YawnFace", NR, 0.3f, 0f); Add("YawnFace", Ch, -0.4f, 0f); Add("YawnFace", Li, -0.4f, 0f); Both("YawnFace", FL, FR, -0.3f);
            // Visemes rest at 0 and only go up. Amounts follow how far each pose moves the lip bones on the UMA 3 head
            // (Full set): the mouth corners pull in by 12.5 mm for U and CH, 9 for O, 8 for nn, 6.5 for RR, 5 for kk and
            // 4 for DD (lips purse: lip lines, chin, corners); E, I and SS spread them by 3.6 mm and aa, O, E and I open
            // the jaw (stretch). PP presses the lips together and FF tucks the lower lip under the teeth (chin).
            // sil and TH barely move the lips and get no rules.
            Add("viseme_U", Li, 0.9f, 0f); Add("viseme_U", Ch, 0.4f, 0f); Both("viseme_U", ML, MR, 0.3f); Both("viseme_U", NL, NRt, -0.2f);
            Add("viseme_CH", Li, 0.8f, 0f); Add("viseme_CH", Ch, 0.3f, 0f); Both("viseme_CH", ML, MR, 0.3f);
            Add("viseme_O", Li, 0.5f, 0f); Both("viseme_O", ML, MR, 0.2f); Add("viseme_O", Ch, -0.2f, 0f);
            Add("viseme_RR", Li, 0.4f, 0f); Both("viseme_RR", ML, MR, 0.15f);
            Add("viseme_nn", Li, 0.3f, 0f);
            Add("viseme_kk", Li, 0.2f, 0f);
            Add("viseme_DD", Li, 0.15f, 0f);
            Add("viseme_PP", Li, 0.3f, 0f); Add("viseme_PP", Ch, 0.5f, 0f);
            Add("viseme_FF", Ch, 0.3f, 0f);
            foreach (var spread in new[] { "viseme_E", "viseme_I" }) { Add(spread, Li, -0.3f, 0f); Both(spread, NL, NRt, 0.2f); Add(spread, Ch, -0.2f, 0f); }
            Add("viseme_SS", Li, -0.2f, 0f); Both("viseme_SS", NL, NRt, 0.15f);
            Add("viseme_aa", Ch, -0.5f, 0f); Add("viseme_aa", Li, -0.4f, 0f); Both("viseme_aa", NL, NRt, -0.2f);
            return rules.ToArray();
        }

        /// <summary>Makes the UMA 3 wrinkle profile if it is missing. An existing profile is left as it is.</summary>
        public static UMAWrinkleProfile EnsureUMA3Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UMAWrinkleProfile>(UMAWrinkleZones.UMA3ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<UMAWrinkleProfile>();
            profile.rules = new System.Collections.Generic.List<UMAWrinkleProfile.Rule>(UMA3ProfileRules());
            AssetDatabase.CreateAsset(profile, UMAWrinkleZones.UMA3ProfilePath);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        [MenuItem("Assets/UMA/Skin/Create Expression Wrinkles Copy", true)]
        private static bool CanCreateCopy()
        {
            var selected = Selection.activeObject;
            var material = selected is UMAMaterial uma ? uma.material : selected as Material;
            return material != null && UMARealisticSkinUtility.IsSkinShader(material.shader) && AssetDatabase.Contains(selected);
        }

        [MenuItem("Assets/UMA/Skin/Create Expression Wrinkles Copy")]
        private static void CreateSelectedCopy()
        {
            var selected = Selection.activeObject;
            var sourceUma = selected as UMAMaterial;
            var source = sourceUma != null ? sourceUma.material : selected as Material;
            if (source == null) return;
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(selected)).Replace('\\', '/');
            if (!directory.StartsWith("Assets/", StringComparison.Ordinal)) directory = "Assets";
            var copy = UMARealisticSkinUtility.CreateWrinkleMaterialCopy(source);
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(directory + "/" + copy.name + ".mat"));
            UnityEngine.Object result = copy;
            if (sourceUma != null)
            {
                var uma = CreateWrinkleUMAMaterial(sourceUma, copy, sourceUma.name + CopySuffix);
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
