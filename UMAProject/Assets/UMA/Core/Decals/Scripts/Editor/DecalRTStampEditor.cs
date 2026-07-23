#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using System;
using System.IO;

public static class DecalRTStampEditor
{
    [MenuItem("UMA/Decals/Save Last Decal As Stamp Asset", priority = 2000)]
    private static void SaveLastStamp()
    {
        var last = DecalRenderTexture.LastStamp;
        if (last == null)
        {
            EditorUtility.DisplayDialog("Save Decal Stamp", "No decal has been stamped this session.", "OK");
            return;
        }

        // If the cached instance is not an asset, create a clone as an asset to avoid saving a transient instance
        var clone = ScriptableObject.CreateInstance<DecalRTStampAsset>();
        clone.bleedPixels = last.bleedPixels;
        clone.overlayGroup = last.overlayGroup;
        clone.sourceOverlay = last.sourceOverlay;
        clone.sourceOverlayName = last.sourceOverlayName;
        clone.forceLinearSampling = last.forceLinearSampling;
        clone.invertY = last.invertY;
        clone.slots = new System.Collections.Generic.List<DecalRTStampAsset.SlotStamp>(last.slots.Count);
        foreach (var s in last.slots)
        {
            var ns = new DecalRTStampAsset.SlotStamp
            {
                slotName = s.slotName,
                slotHash = s.slotHash,                
                slotGroup = s.slotGroup,
                umaMaterialName = s.umaMaterialName,
                normBaseUV = (s.normBaseUV != null) ? (Vector2[])s.normBaseUV.Clone() : new Vector2[0],
                overlayUV = (s.overlayUV != null) ? (Vector2[])s.overlayUV.Clone() : new Vector2[0],
                triangles = (s.triangles != null) ? (int[])s.triangles.Clone() : new int[0],
                recordedUVArea = s.recordedUVArea,
                debugDontUse = s.debugDontUse
            };
#if UNITY_EDITOR
            ns.triOrdinals = (s.triOrdinals != null) ? (int[])s.triOrdinals.Clone() : null;
            ns.slotRelativeTriangles = (s.slotRelativeTriangles != null) ? (int[])s.slotRelativeTriangles.Clone() : null;
#endif
            clone.slots.Add(ns);
        }

        string path = EditorUtility.SaveFilePanelInProject("Save DecalRT Stamp", "DecalRTStamp", "asset", "Choose location to save the stamp asset");
        if (string.IsNullOrEmpty(path)) return;

        AssetDatabase.CreateAsset(clone, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(clone);
    }

    [MenuItem("UMA/Decals/Restore Decal Stamp From Asset To Selected Avatar", priority = 2002)]
    private static void RestoreStampFromAssetToSelected()
    {
        var avatar = GetSelectedAvatar();
        if (avatar == null || avatar.umaData == null)
        {
            EditorUtility.DisplayDialog("Restore Decal Stamp", "Select a GameObject with DynamicCharacterAvatar (with built UMAData).", "OK");
            return;
        }

        string path = EditorUtility.OpenFilePanel("Select DecalRTStampAsset", Application.dataPath, "asset");
        if (string.IsNullOrEmpty(path)) return;

        if (path.StartsWith(Application.dataPath))
        {
            string projPath = "Assets" + path.Substring(Application.dataPath.Length);
         var stampObj = AssetDatabase.LoadAssetAtPath<DecalRTStampAsset>(projPath);
            if (stampObj == null)
            {
                EditorUtility.DisplayDialog("Restore Decal Stamp", "Unable to load asset.", "OK");
                return;
            }

            try
            {
                if (!RestoreStampToAvatar(avatar, stampObj))
                {
                    EditorUtility.DisplayDialog("Restore Decal Stamp", "Failed to apply stamp. See Console for details.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Restore Decal Stamp", "Exception thrown while applying stamp. See Console.", "OK");
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Restore Decal Stamp", "Please select an asset inside this project.", "OK");
        }
    }

    // NEW: Save cached stamp as JSON file
    [MenuItem("UMA/Decals/Save Last Decal As JSON...", priority = 2003)]
    private static void SaveLastStampAsJson()
    {
        var last = DecalRenderTexture.LastStamp;
        if (last == null)
        {
            EditorUtility.DisplayDialog("Save Decal Stamp (JSON)", "No decal has been stamped this session.", "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanel("Save DecalRT Stamp JSON", Application.dataPath, "DecalRTStamp", "json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = JsonUtility.ToJson(last, true);
            File.WriteAllText(path, json);
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
            EditorUtility.DisplayDialog("Save Decal Stamp (JSON)", "Saved JSON:\n" + path, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Save Decal Stamp (JSON)", "Failed to save JSON. See Console.", "OK");
        }
    }

    // NEW: Restore stamp from JSON file to selected avatar
    [MenuItem("UMA/Decals/Restore Decal Stamp From JSON To Selected Avatar", priority = 2004)]
    private static void RestoreStampFromJsonToSelected()
    {
        var avatar = GetSelectedAvatar();
        if (avatar == null || avatar.umaData == null)
        {
            EditorUtility.DisplayDialog("Restore Decal Stamp (JSON)", "Select a GameObject with DynamicCharacterAvatar (with built UMAData).", "OK");
            return;
        }

        string path = EditorUtility.OpenFilePanel("Select DecalRTStamp JSON", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            string json = File.ReadAllText(path);
            var stamp = ScriptableObject.CreateInstance<DecalRTStampAsset>();
            stamp.name = Path.GetFileNameWithoutExtension(path);
            stamp.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            JsonUtility.FromJsonOverwrite(json, stamp);

            if (!RestoreStampToAvatar(avatar, stamp))
            {
                EditorUtility.DisplayDialog("Restore Decal Stamp (JSON)", "Failed to apply stamp. See Console for details.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Restore Decal Stamp (JSON)", "Failed to restore from JSON. See Console.", "OK");
        }
    }

       private static bool RestoreStampToAvatar(DynamicCharacterAvatar avatar, DecalRTStampAsset stamp)
        {
            if (avatar == null || avatar.umaData == null || stamp == null)
            {
                return false;
            }

            var stampSlot = avatar.GetComponentInChildren<DecalRTStampSlot>(true);
            if (stampSlot == null)
            {
                EditorUtility.DisplayDialog("Restore Decal Stamp", "Selected avatar does not contain a DecalRTStampSlot.", "OK");
                return false;
            }

            Undo.RecordObject(stampSlot, "Restore Decal Stamp");
            stampSlot.AddStampToSet(stamp, "RestoredRTDecals");
            stampSlot.OnCharacterBegun(avatar.umaData);
            stampSlot.NotifyStampsChanged();
            EditorUtility.SetDirty(stampSlot);
            return true;
        }

    private static DynamicCharacterAvatar GetSelectedAvatar()
    {
        var go = Selection.activeGameObject;
        if (go == null) return null;
        return go.GetComponent<DynamicCharacterAvatar>();
    }
}
#endif
