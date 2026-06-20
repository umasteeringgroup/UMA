using UnityEngine;
using System.Collections.Generic;

[ExecuteInEditMode]
public class UMATextureSaver : MonoBehaviour
    {
        public bool forceSave = false;
        public bool forceRestore = false;
        public bool forceClear = false;

        // TODO:
        // Should take an array of UV, and save an array of vertex indexes.
        // and a array of prefabs...
        [System.Serializable]
        public class UMAUVTextureInfo
        {
            public string objectNameRelativeToPrefab;
            public int materialIndex; // which material on the renderer this texture belongs to
            public string materialName; // material name for validation on restore
            public string materialKeyword;
            public Texture2D texture;
            public int rendererIndex; // disambiguates multiple Renderer components on the same GameObject (0-based index among same-path renderers)
        }

        public List<UMAUVTextureInfo> uvTextureInfos = new List<UMAUVTextureInfo>();

#if UNITY_EDITOR
        // editor only testing stuff
        public void Update() {
            if (forceClear) {
                // Clear the textures from the materials, but don't clear the uvTextureInfos list.
                // Intentionally uses sharedMaterials — this is a build-time tool that mutates material assets directly.
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat == null) continue;
                        var matTextures = mat.GetTexturePropertyNames();    
                        foreach (var texName in matTextures)
                        {
                            if (mat.HasProperty(texName))
                            { 
                                mat.SetTexture(texName, null);
                            }
                        }
                    }
                }
                forceClear = false;
            }
            if (forceSave) {
                SaveTextureReferences();
                forceSave = false;
            }
            if (forceRestore) {
                RestoreTextureReferences();
                forceRestore = false;
            }
        }
#endif


        public void SaveTextureReferences() {
            uvTextureInfos.Clear();

            var renderers = GetComponentsInChildren<Renderer>(true);

            // Assign a rendererIndex per unique path to disambiguate multiple Renderers on the same GameObject.
            var pathCounts = new Dictionary<string, int>();
            var rendererIndices = new Dictionary<Renderer, int>();
            foreach (var renderer in renderers)
            {
                var relPath = GetRelativePath(transform, renderer.transform);
                if (relPath == null) continue;
                if (!pathCounts.TryGetValue(relPath, out int count))
                    count = 0;
                rendererIndices[renderer] = count;
                pathCounts[relPath] = count + 1;
            }

            foreach (var renderer in renderers)
            {
                var relPath = GetRelativePath(transform, renderer.transform);
                if (relPath == null) continue;
                if (!rendererIndices.TryGetValue(renderer, out int rIdx))
                    rIdx = 0;

                var materials = renderer.sharedMaterials;
                for (int matIdx = 0; matIdx < materials.Length; matIdx++)
                {
                    var mat = materials[matIdx];
                    if (mat == null) continue;
                    // Save all the textures in the material to uvTextureInfos
                    var matTextures = mat.GetTexturePropertyNames();    
                    foreach (var texName in matTextures)
                    {
                        var tex = mat.GetTexture(texName) as Texture2D;
                        if (tex != null)
                        {
                            uvTextureInfos.Add(new UMAUVTextureInfo
                            {
                                objectNameRelativeToPrefab = relPath,
                                materialIndex = matIdx,
                                materialName = StripMaterialInstanceSuffix(mat.name),
                                materialKeyword = texName,
                                texture = tex,
                                rendererIndex = rIdx
                            });
                            Debug.Log("SVTXR: Saved texture reference for renderer " + renderer.name + " material " + mat.name + " texture property " + texName + " texture " + tex.name);
                        }
                    }
                }
            }
        }

        public void Awake() {
            RestoreTextureReferences();
        }


        public void RestoreTextureReferences()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            // Build a dictionary of relative path -> list of renderers (handles multiple Renderer components on the same GameObject).
            var rendererDict = new Dictionary<string, List<Renderer>>();
            foreach (var renderer in renderers)
            {
                string relativePath = GetRelativePath(transform, renderer.transform);
                if (relativePath == null) continue;
                if (!rendererDict.TryGetValue(relativePath, out var list))
                {
                    list = new List<Renderer>();
                    rendererDict.Add(relativePath, list);
                }
                list.Add(renderer);
            }

            // Restore textures using the dictionary.
            foreach (var uvInfo in uvTextureInfos)
            {
                bool found = false;
                if (rendererDict.TryGetValue(uvInfo.objectNameRelativeToPrefab, out var rendererList))
                {
                    // Select the correct renderer by index; clamp to avoid out-of-range on data mismatch.
                    int rIdx = Mathf.Clamp(uvInfo.rendererIndex, 0, rendererList.Count - 1);
                    var renderer = rendererList[rIdx];

                    var materials = renderer.sharedMaterials;
                    if (uvInfo.materialIndex < 0 || uvInfo.materialIndex >= materials.Length)
                    {
                        Debug.LogWarning("SVTXR: Material index " + uvInfo.materialIndex + " is out of range for renderer " + renderer.name);
                        continue;
                    }

                    var mat = materials[uvInfo.materialIndex];
                    if (mat == null) 
                    {
                        Debug.LogWarning("SVTXR: Material is null for renderer " + renderer.name + " at index " + uvInfo.materialIndex);  
                        continue;
                    }
                    // Validate material name matches (in case materials were reordered).
                    // Compare against the stripped base name to handle Unity's " (Instance)" suffix on material instances.
                    if (!string.IsNullOrEmpty(uvInfo.materialName))
                    {
                        string currentName = StripMaterialInstanceSuffix(mat.name);
                        if (currentName != uvInfo.materialName)
                        {
                            Debug.LogWarning("SVTXR: Material name mismatch. Expected '" + uvInfo.materialName + "' but found '" + currentName + "' on renderer " + renderer.name);
                        }
                    }
                    if (mat.shader.name == "Hidden/InternalErrorShader") 
                    {
                        string original = mat.GetTag("OriginalShader", false, "");
                        if(string.IsNullOrEmpty(original)) {
                            Debug.LogWarning("SVTXR: Material " + mat.name + " has error shader but no OriginalShader tag");
                            continue;
                        }
                        if(original == "Hidden/InternalErrorShader") {
                            Debug.LogWarning("SVTXR: Material " + mat.name + " has error shader as OriginalShader tag");
                            continue;
                        }
                        var restoredShader = Shader.Find(original);
                        if(restoredShader != null) {
                            mat.shader = restoredShader;
                        }
                        else {
                            Debug.LogWarning("SVTXR: Could not find original shader " + original + " for material " + mat.name);
                            continue;
                        }
                    }
                    if (mat.HasProperty(uvInfo.materialKeyword))
                    {
                        mat.SetTexture(uvInfo.materialKeyword, uvInfo.texture);
                        Debug.Log("SVTXR: Restored texture reference for renderer " + renderer.name + " material " + mat.name + " texture property " + uvInfo.materialKeyword + " texture " + uvInfo.texture.name);
                        found = true;
                    }
                    else
                    {
                        Debug.LogWarning("SVTXR: Material " + mat.name + " does not have property " + uvInfo.materialKeyword);
                    }
                }
                if (!found)
                {
                    Debug.LogWarning("SVTXR: Could not find renderer or property for UV info: " + uvInfo.objectNameRelativeToPrefab);
                }
            }
        }

        /// <summary>
        /// Strips Unity's " (Instance)" suffix from material names so saved names match across instantiations.
        /// </summary>
        private static string StripMaterialInstanceSuffix(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return materialName;
            const string suffix = " (Instance)";
            if (materialName.EndsWith(suffix))
                return materialName.Substring(0, materialName.Length - suffix.Length);
            return materialName;
        }

        public static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            // get the path from root to target
            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            if (current == null)
            {
                Debug.LogWarning("SVTXR: Target " + target.name + " is not a descendant of root " + root.name);
                return null;
            }
            return path;
        }
    }