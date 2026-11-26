using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Fits baked clothes to other body shapes. UMA's DNA moves the clothes with the body's bones, but bones do not
    /// carry everything: a bigger belly or chest pushes the skin out under the cloth. For each body shape (one DNA
    /// at one end of its range) a hidden character of the race wears the slot, its body is changed, the slot is
    /// moved with the body under it and relaxed (<see cref="SlotRefit"/>), and what the bones did not do is kept as
    /// a blendshape on the slot named after the DNA (<see cref="ShapeName"/>). Every garment uses the same names,
    /// so one blendshape effect on each DNA (<see cref="AddEffects"/>) drives the shapes of all of them.
    /// </summary>
    public static class BodyShapes
    {
        public const string Prefix = "DNA_";
        public const string EffectName = "Clothes Designer body shape";

        /// <summary>The blendshape for a DNA at one end: DNA_belly_Up for the top of its range, DNA_belly_Down for the bottom.</summary>
        public static string ShapeName(string dna, float value) => Prefix + dna + (value >= 0.5f ? "_Up" : "_Down");

        public sealed class Result
        {
            public readonly List<string> Messages = new List<string>();
            /// <summary>The blendshapes made, by name.</summary>
            public readonly List<string> Shapes = new List<string>();
            /// <summary>Per shape: vertices inside the body with the bones alone, and with the shape.</summary>
            public readonly Dictionary<string, Vector2Int> Inside = new Dictionary<string, Vector2Int>();
            /// <summary>Per shape: how far the shape moves the slot, at most and on average (mm).</summary>
            public readonly Dictionary<string, Vector2> MoveMm = new Dictionary<string, Vector2>();
            /// <summary>The shapes whose DNA asset has no blendshape effect for them yet.</summary>
            public readonly List<string> MissingEffects = new List<string>();
            public readonly Dictionary<string, Vector3[]> Deltas = new Dictionary<string, Vector3[]>();
        }

        /// <summary>The DNA names the race has (the new DNA system), sorted.</summary>
        public static List<string> DnaNames(RaceData race)
        {
            var names = new List<string>();
            if (race == null || race.DNACollection == null) return names;
            race.DNACollection.LoadDictionary();
            foreach (string n in race.DNACollection.GetDNANames()) if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>The race's DNA asset of that name, or null.</summary>
        public static DNA FindDna(RaceData race, string name)
        {
            if (race == null || race.DNACollection == null || string.IsNullOrEmpty(name)) return null;
            race.DNACollection.LoadDictionary();
            return race.DNACollection.dnaDictionary != null && race.DNACollection.dnaDictionary.TryGetValue(name, out DNA dna) ? dna : null;
        }

        /// <summary>True when the DNA asset already drives the shape for that end of its range.</summary>
        public static bool HasEffect(DNA dna, string shapeName)
        {
            if (dna == null || dna.effects == null) return false;
            foreach (DNAEffect e in dna.effects) if (e is DNAEffect_BlendShape b && b.BlendShapeName == shapeName) return true;
            return false;
        }

        /// <summary>
        /// Fits the slot to each body shape and returns the blendshapes; with <paramref name="save"/> they are written
        /// into the slot asset (replacing earlier ones of the same names). The fit's own shape is <paramref name="baseDna"/>.
        /// </summary>
        public static Result Bake(RaceData race, SlotDataAsset slot, OverlayDataAsset overlay, IList<DnaValue> baseDna, IList<BodyShapeTarget> targets,
            float clearance, bool save)
        {
            var result = new Result();
            if (race == null || slot == null || targets == null || targets.Count == 0) return result;
            if (overlay == null) { result.Messages.Add("No body shapes: they are fitted on a character wearing the clothes' overlay, which is made with the wardrobe recipe. Turn on Wardrobe Recipe."); return result; }
            var wanted = new List<BodyShapeTarget>();
            var names = new HashSet<string>();
            foreach (BodyShapeTarget t in targets)
            {
                if (t == null || string.IsNullOrEmpty(t.dna)) continue;
                if (FindDna(race, t.dna) == null) { result.Messages.Add("Body shapes: '" + race.raceName + "' has no DNA named '" + t.dna + "' (new DNA system); left out."); continue; }
                if (names.Add(ShapeName(t.dna, t.value))) wanted.Add(t);
            }
            if (wanted.Count == 0) return result;

            // The slot without earlier body shapes, so what is measured is the bones alone.
            SlotDataAsset plain = Object.Instantiate(slot);
            plain.hideFlags = HideFlags.HideAndDontSave;
            if (plain.meshData.blendShapes != null)
            {
                var kept = new List<UMABlendShape>();
                foreach (UMABlendShape s in plain.meshData.blendShapes) if (s != null && !s.shapeName.StartsWith(Prefix)) kept.Add(s);
                plain.meshData.blendShapes = kept.ToArray();
            }
            GameObject go = DesignerTemporaryObjects.Create(DesignerTemporaryObjects.BodyShapesName, HideFlags.HideAndDontSave);
            go.transform.position = new Vector3(2000f, 0f, 0f);
            UmaTPose modelled = null;
            var refit = new SlotRefit();
            try
            {
                var avatar = go.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                avatar.showPlaceholder = false;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                if (avatar.predefinedDNA == null) avatar.predefinedDNA = new UMAPredefinedDNA();
                avatar.ignoreMeshHideAssets = true;
                string overlayPath = AssetDatabase.GetAssetPath(overlay);
                avatar.GenerateNow();
                avatar.enabled = false;
                if (avatar.umaData == null) { result.Messages.Add("Body shapes: a character of '" + race.raceName + "' could not be made."); return result; }
                // Building a character unloads assets only scripts hold: load the overlay again.
                if (overlay == null && !string.IsNullOrEmpty(overlayPath)) overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath);
                var worn = new SlotData(plain);
                worn.AddOverlay(new OverlayData(overlay));
                UMAData.UMARecipe recipe = avatar.umaData.umaRecipe;
                recipe.SetSlot(recipe.slotDataList.Length, worn);
                modelled = VertexEditorStage.CreateTPoseWithoutHumanPose(avatar.umaData.GetTPose());
                if (modelled != null) avatar.umaData.OverrideTpose = modelled;

                SetDna(avatar, baseDna, null, 0f);
                Generate(avatar);
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                BodyReference body = BodyReference.Capture(avatar);
                if (!refit.Bind(avatar, renderer, body, plain)) { result.Messages.AddRange(refit.Messages); return result; }
                if (refit.BindRoundTrip > 0.001f) result.Messages.Add("Body shapes: the slot is " + (refit.BindRoundTrip * 1000f).ToString("0.0") + " mm from how it is worn; the shapes may be offset.");
                Vector3[] bindVertices = plain.meshData.vertices;

                foreach (BodyShapeTarget target in wanted)
                {
                    string shape = ShapeName(target.dna, target.value);
                    SetDna(avatar, baseDna, target.dna, target.value);
                    Generate(avatar);
                    renderer = avatar.umaData.GetRenderer(0);
                    BodyReference changed = BodyReference.Capture(avatar);
                    Vector3[] bonesOnly = SlotRefit.WornPositions(avatar, renderer, plain);
                    refit.Conform(changed, 20, clearance);
                    Vector3[] bind = refit.CurrentInBindSpace(avatar, renderer, out _);
                    var delta = new Vector3[bind.Length];
                    float max = 0f; double sum = 0;
                    for (int v = 0; v < bind.Length; v++)
                    {
                        delta[v] = bind[v] - bindVertices[v];
                        float m = delta[v].magnitude;
                        max = Mathf.Max(max, m); sum += m;
                    }
                    result.Deltas[shape] = delta;
                    result.Shapes.Add(shape);
                    result.MoveMm[shape] = new Vector2(max * 1000f, (float)(sum / Mathf.Max(1, bind.Length)) * 1000f);
                    result.Inside[shape] = new Vector2Int(refit.CountInside(changed, bonesOnly, 0.001f), refit.CountInside(changed, refit.Current, 0.001f));
                    if (!HasEffect(FindDna(race, target.dna), shape) && !result.MissingEffects.Contains(shape)) result.MissingEffects.Add(shape);
                }
                SetDna(avatar, baseDna, null, 0f);
            }
            finally
            {
                refit.Dispose();
                DesignerTemporaryObjects.Destroy(go);
                if (modelled != null) Object.DestroyImmediate(modelled);
                Object.DestroyImmediate(plain);
            }

            if (save && result.Deltas.Count > 0) Save(slot, result.Deltas);
            foreach (string shape in result.Shapes)
            {
                Vector2Int inside = result.Inside[shape];
                result.Messages.Add("Body shape " + shape + ": moves the clothes by up to " + result.MoveMm[shape].x.ToString("0") + " mm (" + result.MoveMm[shape].y.ToString("0.0") +
                    " mm on average); vertices inside the body with the bones alone " + inside.x + ", with the shape " + inside.y + ".");
            }
            if (result.MissingEffects.Count > 0)
            {
                result.Messages.Add("Body shapes: no DNA drives " + string.Join(", ", result.MissingEffects) + " yet. Use Drive Shapes from DNA in the Bake panel to add the effects (it changes those DNA assets for every character of the race).");
            }
            return result;
        }

        /// <summary>Writes the shapes into the slot, replacing earlier ones of the same names.</summary>
        public static void Save(SlotDataAsset slot, Dictionary<string, Vector3[]> deltas)
        {
            var shapes = new List<UMABlendShape>();
            if (slot.meshData.blendShapes != null) foreach (UMABlendShape s in slot.meshData.blendShapes) if (s != null && !deltas.ContainsKey(s.shapeName)) shapes.Add(s);
            foreach (KeyValuePair<string, Vector3[]> kv in deltas)
            {
                var frame = new UMABlendFrame(kv.Value.Length, false, false);
                System.Array.Copy(kv.Value, frame.deltaVertices, kv.Value.Length);
                shapes.Add(new UMABlendShape { shapeName = kv.Key, frames = new[] { frame } });
            }
            slot.meshData.blendShapes = shapes.ToArray();
            EditorUtility.SetDirty(slot);
            AssetDatabase.SaveAssetIfDirty(slot);
        }

        /// <summary>
        /// Adds to each DNA asset a blendshape effect for its shape: weight 0 at the DNA's middle rising to full at
        /// the end it was fitted to. DNA assets that already have unsaved changes are left alone and named in
        /// <paramref name="skipped"/>. Returns how many effects were added.
        /// </summary>
        public static int AddEffects(RaceData race, IEnumerable<BodyShapeTarget> targets, List<string> skipped)
        {
            int added = 0;
            foreach (BodyShapeTarget t in targets)
            {
                if (t == null) continue;
                DNA dna = FindDna(race, t.dna);
                string shape = ShapeName(t.dna, t.value);
                if (dna == null || HasEffect(dna, shape)) continue;
                if (EditorUtility.IsDirty(dna)) { skipped?.Add(dna.name); continue; }
                if (dna.effects == null) dna.effects = new List<DNAEffect>();
                dna.effects.Add(CreateEffect(shape, t.value >= 0.5f));
                EditorUtility.SetDirty(dna);
                AssetDatabase.SaveAssetIfDirty(dna);
                added++;
            }
            return added;
        }

        /// <summary>A blendshape effect for one end of a DNA's range: 0 from the bottom (or top) to the middle, then straight up to 1.</summary>
        public static DNAEffect_BlendShape CreateEffect(string shape, bool up)
        {
            AnimationCurve curve = up
                ? new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 0f, 0f, 2f), new Keyframe(1f, 1f, 2f, 0f))
                : new AnimationCurve(new Keyframe(0f, 1f, 0f, -2f), new Keyframe(0.5f, 0f, -2f, 0f), new Keyframe(1f, 0f, 0f, 0f));
            return new DNAEffect_BlendShape { BlendShapeName = shape, curve = curve, minMapping = 0f, maxMapping = 1f, EffectName = EffectName };
        }

        private static void SetDna(DynamicCharacterAvatar avatar, IList<DnaValue> baseDna, string changed, float value)
        {
            Dictionary<string, DnaSetter> dna = avatar.GetDNA();
            foreach (KeyValuePair<string, DnaSetter> kv in dna)
            {
                float want = 0.5f;
                if (baseDna != null) foreach (DnaValue v in baseDna) if (v != null && v.name == kv.Key) want = v.value;
                if (kv.Key == changed) want = value;
                kv.Value.Set(want);
            }
        }

        private static void Generate(DynamicCharacterAvatar avatar)
        {
            avatar.umaData.Dirty(true, true, true);
            UMAAssetIndexer.Instance.Generator.GenerateSingleUMA(avatar.umaData, false);
            VertexEditorStage.ApplyModelledPose(avatar);
        }
    }
}
