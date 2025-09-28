using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UMA
{
    [PreferBinarySerialization]
    [CreateAssetMenu(fileName = "MaterialShaderRegistry", menuName = "UMA/Core/Material Shader Registry", order = 1000)]
    public class MaterialShaderRegistry : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Material to track")]
            public Material material;

            [Tooltip("Shader used by this material. Leave empty to auto-sync from material.shader")]
            public Shader shader;

            [Tooltip("Shader name (used to re-link the shader if the reference is lost)")]
            public string shaderName;
        }

        [SerializeField]
        private List<Entry> _entries = new List<Entry>();

        [NonSerialized]
        private Dictionary<Material, Shader> _map;

        // Tracks the entry count used to build the current map (prevents rebuild loops when some entries can't resolve a shader)
        [NonSerialized]
        private int _builtEntryCount = -1;

        [Header("Editor Options")]
        [Tooltip("When enabled, keeps 'shader' in sync with 'material.shader' if not explicitly set.")]
        [SerializeField] private bool _autoSyncShaderFromMaterial = true;

        public IReadOnlyList<Entry> Entries => _entries;

        private static bool IsHiddenInternalShaderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("Hidden/Internal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHiddenInternal(Shader s)
        {
            return s != null && IsHiddenInternalShaderName(s.name);
        }

        private void OnEnable()
        {
            BuildIndex();
        }

        private void OnValidate()
        {
            // Clean and normalize serialized data in edit-time
            bool changed = false;

            // Remove null materials
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].material == null)
                {
                    _entries.RemoveAt(i);
                    changed = true;
                }
            }

            // Deduplicate by Material reference, favor the last occurrence (latest edit)
            if (_entries.Count > 1)
            {
                var lastIndexByMat = new Dictionary<Material, int>();
                for (int i = 0; i < _entries.Count; i++)
                {
                    var m = _entries[i].material;
                    if (m == null) continue;
                    lastIndexByMat[m] = i;
                }
                // Keep only last occurrences
                var keep = new HashSet<int>(lastIndexByMat.Values);
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (!keep.Contains(i))
                    {
                        _entries.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            // Auto-fill shader from material if requested (do not touch shaderName here)
            if (_autoSyncShaderFromMaterial)
            {
                foreach (var e in _entries)
                {
                    if (e.material != null)
                    {
                        var matShader = e.material.shader;
                        if (e.shader == null || e.shader != matShader)
                        {
                            e.shader = matShader;
                            changed = true;
                        }
                    }
                }
            }

            // Keep shaderName in sync with any available reference, but skip hidden/internal shaders
            foreach (var e in _entries)
            {
                var nameFromRef = e.shader != null ? e.shader.name : (e.material != null ? e.material.shader?.name : null);
                if (!string.IsNullOrEmpty(nameFromRef) && !IsHiddenInternalShaderName(nameFromRef) && e.shaderName != nameFromRef)
                {
                    e.shaderName = nameFromRef;
                    changed = true;
                }
            }

            if (changed)
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            BuildIndex();
        }

        public void BuildIndex()
        {
            if (_map == null) _map = new Dictionary<Material, Shader>();
            else _map.Clear();

            foreach (var e in _entries)
            {
                if (e.material == null) continue;
                var shader = ResolveShaderForEntry(e, cache: false);
                if (shader != null)
                {
                    // Favor last entry (consistent with OnValidate dedupe policy)
                    _map[e.material] = shader;
                }
            }

            _builtEntryCount = _entries.Count;
        }

        public bool TryGetShader(Material material, out Shader shader)
        {
            if (material == null)
            {
                shader = null;
                return false;
            }
            EnsureBuilt();

            if (_map.TryGetValue(material, out shader) && shader != null)
                return true;

            // Attempt to resolve from the entry (handles lost references)
            var e = _entries.FirstOrDefault(x => ReferenceEquals(x.material, material));
            if (e != null)
            {
                shader = ResolveShaderForEntry(e, cache: true);
                if (shader != null)
                {
                    _map[material] = shader;
                    return true;
                }
            }

            // Fallback: use the material's current shader (not cached if we have no entry)
            shader = material.shader;
            return shader != null;
        }

        public Shader GetShader(Material material)
        {
            return TryGetShader(material, out var s) ? s : null;
        }

        public IEnumerable<Material> GetMaterials(Shader shader)
        {
            EnsureBuilt();
            if (shader == null) yield break;

            // Iterate entries to include ones that only have shaderName
            foreach (var e in _entries)
            {
                if (e.material == null) continue;

                var resolved = ResolveShaderForEntry(e, cache: false);
                if (resolved == shader)
                {
                    _map[e.material] = resolved; // keep cache warm
                    yield return e.material;
                }
                else if (resolved == null && !string.IsNullOrEmpty(e.shaderName) && e.shaderName == shader.name)
                {
                    // Match by name when reference cannot be resolved
                    yield return e.material;
                }
            }
        }

        public bool Contains(Material material)
        {
            EnsureBuilt();
            return material != null && (_map.ContainsKey(material) || _entries.Any(e => ReferenceEquals(e.material, material)));
        }

        public void AddOrUpdate(Material material, Shader shader = null)
        {
            if (material == null) return;

            var idx = _entries.FindIndex(e => ReferenceEquals(e.material, material));
            var chosenShader = shader != null ? shader : material.shader;
            var chosenName = chosenShader != null ? chosenShader.name : (material.shader != null ? material.shader.name : null);

            if (idx >= 0)
            {
                _entries[idx].shader = chosenShader;
                // Only set shaderName if it's not a hidden/internal shader
                if (!string.IsNullOrEmpty(chosenName) && !IsHiddenInternalShaderName(chosenName))
                {
                    _entries[idx].shaderName = chosenName;
                }
            }
            else
            {
                _entries.Add(new Entry
                {
                    material = material,
                    shader = chosenShader,
                    shaderName = (!string.IsNullOrEmpty(chosenName) && !IsHiddenInternalShaderName(chosenName)) ? chosenName : string.Empty
                });
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            BuildIndex();
        }

        public bool Remove(Material material)
        {
            if (material == null) return false;
            var removed = _entries.RemoveAll(e => ReferenceEquals(e.material, material)) > 0;
            if (removed)
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
                BuildIndex();
            }
            return removed;
        }

        public void Clear()
        {
            _entries.Clear();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            BuildIndex();
        }

        [ContextMenu("Sync Shader From Material (All)")]
        private void SyncAllFromMaterial()
        {
            foreach (var e in _entries)
            {
                if (e.material != null)
                {
                    e.shader = e.material.shader;
                    if (e.shader != null && !IsHiddenInternal(e.shader))
                        e.shaderName = e.shader.name;
                }
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            BuildIndex();
        }

        [ContextMenu("Resolve Missing Shaders From Names")]
        private void ResolveAllFromNames()
        {
            bool changed = false;
            foreach (var e in _entries)
            {
                if (e.shader == null && !string.IsNullOrEmpty(e.shaderName))
                {
                    var found = Shader.Find(e.shaderName);
                    if (found != null)
                    {
                        e.shader = found;
                        changed = true;
                    }
                }
            }
#if UNITY_EDITOR
            if (changed) UnityEditor.EditorUtility.SetDirty(this);
#endif
            BuildIndex();
        }

        private void EnsureBuilt()
        {
            if (_map == null || _builtEntryCount != _entries.Count)
                BuildIndex();
        }

        private static Shader ResolveByName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return null;
            return Shader.Find(shaderName);
        }

        private Shader ResolveShaderForEntry(Entry e, bool cache)
        {
            if (e == null) return null;

            // If we have a shader ref, keep its name in sync (skip hidden/internal) and return it
            if (e.shader != null)
            {
                var name = e.shader.name;
                if (!string.IsNullOrEmpty(name) && !IsHiddenInternalShaderName(name) && e.shaderName != name)
                {
                    e.shaderName = name;
                }
                return e.shader;
            }

            // Try material reference
            Shader matShader = e.material != null ? e.material.shader : null;
            if (matShader != null)
            {
                var n = matShader.name;
                if (!string.IsNullOrEmpty(n) && !IsHiddenInternalShaderName(n) && (string.IsNullOrEmpty(e.shaderName) || e.shaderName != n))
                    e.shaderName = n;
                if (cache) e.shader = matShader;
                return matShader;
            }

            // Finally, try to resolve by stored name
            var resolved = ResolveByName(e.shaderName);
            if (resolved != null && cache)
            {
                e.shader = resolved;
            }
            return resolved;
        }
    }
}