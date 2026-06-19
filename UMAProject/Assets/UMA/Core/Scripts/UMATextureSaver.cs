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
            public string materialKeyword;
            public Texture2D texture;
        }

        public List<UMAUVTextureInfo> uvTextureInfos = new List<UMAUVTextureInfo>();


        public void Update() {
            if (forceClear) {
                // clear the textures from the materials, but don't clear the uvTextureInfos list
                GameObject prefab = this.gameObject; // Assuming this script is attached to the prefab instance. Adjust as needed.
                if (prefab == null)
                {
                    Debug.LogWarning("Prefab is null, cannot clear texture references.");
                    return;
                }
                var renderers = prefab.GetComponentsInChildren<Renderer>();
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


        public void SaveTextureReferences() {
            GameObject prefab = this.gameObject; // Assuming this script is attached to the prefab instance. Adjust as needed.
            if (prefab == null)
            {
                Debug.LogWarning("Prefab is null, cannot save texture references.");
                return;
            }
            uvTextureInfos.Clear();

            var renderers = prefab.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
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
                                objectNameRelativeToPrefab = GetRelativePath(prefab.transform, renderer.transform),
                                materialKeyword = texName,
                                texture = tex
                            });
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
            GameObject prefab = this.gameObject; // Assuming this script is attached to the prefab instance. Adjust as needed.
            if (prefab == null)
            {
                Debug.LogWarning("Prefab is null, cannot restore texture references.");
                return;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>();
            // build a dictionary of relative path to renderer
            Dictionary<string, Renderer> rendererDict = new Dictionary<string, Renderer>();
            foreach (var renderer in renderers)
            {
                string relativePath = GetRelativePath(prefab.transform, renderer.transform);
                if (!rendererDict.ContainsKey(relativePath))
                {
                    rendererDict.Add(relativePath, renderer);
                }
            }
            // restore textures using the dictionary
            foreach (var uvInfo in uvTextureInfos)
            {
                if (rendererDict.TryGetValue(uvInfo.objectNameRelativeToPrefab, out var renderer))
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty(uvInfo.materialKeyword))
                        {
                            mat.SetTexture(uvInfo.materialKeyword, uvInfo.texture);
                        }
                    }
                }
            }
        }

        public string GetRelativePath(Transform root, Transform target)
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
            return path;
        }
    }