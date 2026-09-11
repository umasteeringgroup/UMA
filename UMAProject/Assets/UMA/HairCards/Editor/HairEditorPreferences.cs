using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    // UserSettings is project-local, survives Unity restarts, and uses Unity serialization for
    // asset references (GUID/fileID). Never persist transient object identifiers in EditorPrefs.
    [FilePath("UserSettings/UMA.HairCards.Preferences.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class HairEditorPreferences : ScriptableSingleton<HairEditorPreferences>
    {
        [Serializable] private sealed class Entry { public string key; public string json; }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private SetupDefaults lastSetup;
        [SerializeField] private SetupDefaults ignoredSetup;
        // Unity's inline class serialization materializes null as an empty instance. Presence
        // must be explicit or a cleared template can come back as zero-valued settings.
        [SerializeField] private bool hasLastSetup;
        [SerializeField] private bool hasIgnoredSetup;
        internal static bool Suspended;
        internal void Flush() => Save(true);

        [Serializable] internal sealed class SetupDefaults
        {
            public HairCardProfileAsset profile;
            public HairAtlasProfileAsset atlas;
            public string resourceRevision;
            public HairChildSettings children;
            public float rootEmbedDepth, lodImportance;
            public HairGroupRole role;
            public bool visible, enabled;
            public Color color;
            public HairAtlasRegionSelectionMode regionSelection;
            public List<string> regionIds;
            public List<HairLodSettings> lods;
            public HairBakeSettings bake;
            public bool symmetryEnabled;
            public Vector3 symmetryPoint, symmetryNormal;
        }

        internal static string Context(UnityEngine.Object asset) => asset != null && EditorUtility.IsPersistent(asset)
            ? GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString() : null;

        internal void Remember(object target, string section, string context = null)
        {
            if (Suspended) return;
            bool changed = Put(section, HairPreferenceCodec.Capture(target, true));
            if (!string.IsNullOrEmpty(context)) changed |= Put(section + ":" + context, HairPreferenceCodec.Capture(target, false));
            if (changed) Save(true);
        }

        internal void Restore(object target, string section, string context = null)
        {
            if (Suspended) return;
            HairPreferenceCodec.Restore(target, Get(section));
            if (!string.IsNullOrEmpty(context)) HairPreferenceCodec.Restore(target, Get(section + ":" + context));
        }

        private string Get(string key) => entries.Find(entry => entry.key == key)?.json;
        private bool Put(string key, string json)
        {
            Entry entry = entries.Find(item => item.key == key);
            if (entry?.json == json) return false;
            if (entry == null) { entry = new Entry { key = key }; entries.Add(entry); }
            entry.json = json;
            return true;
        }

        internal void ClearEditorSettings()
        {
            entries.Clear();
            EditorPrefs.DeleteKey(HairGroomCreationLocation.LastFolderEditorPrefKey);
            Save(true);
        }

        internal void ClearSetupDefaults()
        {
            if (hasLastSetup) { ignoredSetup = lastSetup; hasIgnoredSetup = true; }
            // Dependency hashes can settle after resource creation/import. Forget the current
            // saved revision, not the hash from before those imports completed.
            if (hasIgnoredSetup && ignoredSetup != null)
                ignoredSetup.resourceRevision = ResourceRevision(ignoredSetup.profile) + ":" + ResourceRevision(ignoredSetup.atlas);
            lastSetup = null;
            hasLastSetup = false;
            Save(true);
        }

        private static string ResourceRevision(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.GetAssetDependencyHash(path).ToString();
        }

        internal void RememberSetup(HairGroomAsset groom, HairGroup group)
        {
            if (Suspended || groom == null || group == null || !EditorUtility.IsPersistent(groom)) return;
            var next = new SetupDefaults
            {
                profile = EditorUtility.IsPersistent(group.profile) ? group.profile : null,
                atlas = EditorUtility.IsPersistent(group.atlas) ? group.atlas : null,
                resourceRevision = ResourceRevision(group.profile) + ":" + ResourceRevision(group.atlas),
                children = group.children, rootEmbedDepth = group.rootEmbedDepth, lodImportance = group.lodImportance,
                role = group.role, color = group.color, visible = group.visible, enabled = group.enabled,
                regionSelection = group.atlasRegionSelection,
                regionIds = group.atlasRegionIds, lods = groom.Lods, bake = groom.BakeSettings,
                symmetryEnabled = groom.SymmetryEnabled, symmetryPoint = groom.SymmetryPlanePoint,
                symmetryNormal = groom.SymmetryPlaneNormal
            };
            string json = JsonUtility.ToJson(next);
            if (hasIgnoredSetup && ignoredSetup != null && JsonUtility.ToJson(ignoredSetup) == json) return;
            if (hasLastSetup && lastSetup != null && JsonUtility.ToJson(lastSetup) == json) return;
            // Deep copy mutable settings. Unity serializes the actual asset references when saving.
            lastSetup = JsonUtility.FromJson<SetupDefaults>(json);
            hasLastSetup = true;
            ignoredSetup = null;
            hasIgnoredSetup = false;
            Save(true);
        }

        internal void ApplySetupToNewGroom(HairGroomAsset groom)
        {
            if (groom == null || !hasLastSetup || lastSetup == null) return;
            SetupDefaults setup = JsonUtility.FromJson<SetupDefaults>(JsonUtility.ToJson(lastSetup));
            HairGroup group = groom.Groups[0];
            group.children = setup.children ?? new HairChildSettings();
            group.rootEmbedDepth = setup.rootEmbedDepth; group.lodImportance = setup.lodImportance;
            group.role = setup.role; group.color = setup.color;
            group.visible = setup.visible; group.enabled = setup.enabled;
            group.atlasRegionSelection = setup.regionSelection;
            group.atlasRegionIds = setup.regionIds ?? new List<string>();
            if (setup.profile != null) group.profile = CopyResource(groom, setup.profile, "profileId", "_RibbonProfile");
            if (setup.atlas != null) group.atlas = CopyResource(groom, setup.atlas, "atlasId", "_HairAtlas");
            else { group.atlasRegionSelection = HairAtlasRegionSelectionMode.All; group.atlasRegionIds.Clear(); }
            if (setup.lods != null && setup.lods.Count > 0) { groom.Lods.Clear(); groom.Lods.AddRange(setup.lods); }
            if (setup.bake != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(setup.bake), groom.BakeSettings);
            groom.SymmetryEnabled = setup.symmetryEnabled;
            groom.SetSymmetryPlane(setup.symmetryPoint, setup.symmetryNormal);
            groom.EnsureIntegrity();
        }

        private static T CopyResource<T>(HairGroomAsset groom, T source, string identityField, string suffix) where T : ScriptableObject
        {
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(groom))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("Save the groom before creating private setup resources.");
            T copy = UnityEngine.Object.Instantiate(source);
            copy.name = groom.name + suffix;
            copy.hideFlags = HideFlags.None;
            using (SerializedObject serialized = new SerializedObject(copy))
            { serialized.FindProperty(identityField).stringValue = HairStableId.Create(); serialized.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + copy.name + ".asset"));
            Undo.RegisterCreatedObjectUndo(copy, "Create Hair Setup Resource");
            AssetDatabase.SaveAssetIfDirty(copy);
            return copy;
        }

        internal void ResetCurrentSetup(HairGroomAsset groom, HairGroup group)
        {
            if (groom == null || group == null || group.locked) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(groom, "Reset Hair Setup to Defaults");
            // New private resources keep other grooms/groups and the old assets intact. Undo
            // restores assignments; no texture/material assets or authored hair are deleted.
            group.profile = HairCardMenu.CreateDefaultProfileNear(groom);
            group.atlas = HairCardMenu.CreateDefaultAtlasNear(groom);
            group.children = new HairChildSettings();
            group.rootEmbedDepth = 0f; group.lodImportance = 1f;
            group.role = HairGroupRole.Coverage; group.color = new HairGroup().color;
            group.visible = true; group.enabled = true;
            group.atlasRegionSelection = HairAtlasRegionSelectionMode.All;
            group.atlasRegionIds.Clear();
            groom.Lods.Clear(); groom.Lods.Add(new HairLodSettings());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new HairBakeSettings()), groom.BakeSettings);
            groom.BakeSettings.assetName = groom.name.Replace("_HairGroom", string.Empty);
            groom.SymmetryEnabled = true; groom.SetSymmetryPlane(Vector3.zero, Vector3.right);
            ClearSetupDefaults();
            HairGroomCommands.Commit(groom);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    internal static class HairPreferenceCodec
    {
        [Serializable] private sealed class Value<T> { public T value = default; }
        [Serializable] private sealed class FieldValue { public string name, json; }
        [Serializable] private sealed class Snapshot { public List<FieldValue> fields = new List<FieldValue>(); }
        private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
        // These refer to one groom/atlas, not reusable tool defaults. Asset references and runtime
        // interaction state are excluded entirely; all other serialized editor options are automatic.
        private static readonly HashSet<string> ContextFields = new HashSet<string>
        { "activeGroupId", "activeMapId", "activeGuideId", "activeLayerId", "activeModifierId", "collapsedLayerIds", "activeHelperId", "activeGuidePoint",
          "selectedGuideIds", "selectedVertices", "soloLayerId", "isolateSelectedGuides", "selectedId", "guidePage", "zoom", "pan" };

        private static FieldInfo[] GetFields(Type type)
        {
            if (Fields.TryGetValue(type, out FieldInfo[] cached)) return cached;
            var fields = new List<FieldInfo>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (field.IsDefined(typeof(SerializeField), false) && !HasObjectReference(field.FieldType) &&
                    field.Name != "atlasEditor") fields.Add(field);
            Fields[type] = fields.ToArray();
            return Fields[type];
        }
        private static bool HasObjectReference(Type type) => typeof(UnityEngine.Object).IsAssignableFrom(type) ||
            (type.IsArray && HasObjectReference(type.GetElementType())) ||
            (type.IsGenericType && Array.Exists(type.GetGenericArguments(), HasObjectReference));

        internal static string Capture(object target, bool global = false)
        {
            var snapshot = new Snapshot();
            foreach (FieldInfo field in GetFields(target.GetType()))
            {
                if (global && ContextFields.Contains(field.Name)) continue;
                Type boxType = typeof(Value<>).MakeGenericType(field.FieldType);
                object box = Activator.CreateInstance(boxType);
                boxType.GetField("value").SetValue(box, field.GetValue(target));
                snapshot.fields.Add(new FieldValue { name = field.Name, json = JsonUtility.ToJson(box) });
            }
            return JsonUtility.ToJson(snapshot);
        }

        internal static void Restore(object target, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Snapshot snapshot = JsonUtility.FromJson<Snapshot>(json);
                if (snapshot?.fields == null) return;
                foreach (FieldValue saved in snapshot.fields)
                {
                    FieldInfo field = Array.Find(GetFields(target.GetType()), item => item.Name == saved.name);
                    if (field == null) continue;
                    Type boxType = typeof(Value<>).MakeGenericType(field.FieldType);
                    object box = JsonUtility.FromJson(saved.json, boxType);
                    if (box != null) field.SetValue(target, boxType.GetField("value").GetValue(box));
                }
            }
            catch (ArgumentException) { Debug.LogWarning("Hair Cards: an invalid saved preference was ignored; use Reset to defaults to clear saved editor settings."); }
        }

        internal static void Reset(object target)
        {
            bool previous = HairEditorPreferences.Suspended;
            HairEditorPreferences.Suspended = true;
            object defaults = null;
            try
            {
                defaults = target is ScriptableObject ? ScriptableObject.CreateInstance(target.GetType()) : Activator.CreateInstance(target.GetType());
                Restore(target, Capture(defaults));
            }
            finally
            {
                if (defaults is UnityEngine.Object unityObject) UnityEngine.Object.DestroyImmediate(unityObject);
                HairEditorPreferences.Suspended = previous;
            }
        }
    }

}
