using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    public class UMAMeshCombinerSwitcherWindow : EditorWindow
    {
        private enum CombinerMode
        {
            Jobified,
            BoneBaking,
            Default
        }

        private UMAGenerator _generator;
        private CombinerMode _selected;
        private Vector2 _scrollPos;

        [MenuItem("UMA/Tools/Mesh Combiner Switcher", priority = 11)]
        public static void ShowWindow()
        {
            var window = GetWindow<UMAMeshCombinerSwitcherWindow>();
            window.titleContent = new GUIContent("Combiner", EditorGUIUtility.IconContent("Settings").image);
            window.minSize = new Vector2(180f, 100f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshGenerator();
        }

        private void OnFocus()
        {
            RefreshGenerator();
        }

        private void RefreshGenerator()
        {
            if (UMAAssetIndexer.Instance != null)
            {
                _generator = UMAAssetIndexer.Instance.generator;
            }
        }

        private void OnGUI()
        {
            if (_generator == null)
            {
                RefreshGenerator();
                if (_generator == null)
                {
                    EditorGUILayout.HelpBox(
                        "No UMA generator found. Open the Global Library or assign a generator in the UMAAssetIndexer.",
                        MessageType.Warning);
                    return;
                }
            }

            EditorGUILayout.Space(4f);

            // ── Current combiner indicator ──────────────────────
            EditorGUILayout.LabelField("Generator:", _generator.name, EditorStyles.miniLabel);
            var current = GetCurrentCombiner();
            EditorGUILayout.LabelField("Current:", current, EditorStyles.boldLabel);

            EditorGUILayout.Space(6f);

            // ── Combiner selection toggle group ─────────────────
            EditorGUILayout.LabelField("Switch To:", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            DrawCombinerToggle("Jobified", CombinerMode.Jobified,
                "Fast, job-based parallel mesh combining. Good for most use cases.",
                current);

            DrawCombinerToggle("Bone Baking", CombinerMode.BoneBaking,
                "Bakes bone weights into the vertex data for reduced skinning cost at runtime. Best for static or semi-static characters.",
                current);

            DrawCombinerToggle("Default", CombinerMode.Default,
                "Single-threaded, straightforward mesh combining. Reliable fallback.",
                current);

            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                ApplyCombiner(_selected);
            }
        }

        private void DrawCombinerToggle(string label, CombinerMode mode, string tooltip, string currentName)
        {
            bool isCurrent = currentName.EndsWith(label + " Combiner");
            GUI.enabled = !isCurrent;
            if (GUILayout.Toggle(_selected == mode && !isCurrent, new GUIContent(label + " Combiner", tooltip), EditorStyles.miniButton))
            {
                _selected = mode;
                GUI.changed = true;
            }
            GUI.enabled = true;
        }

        private string GetCurrentCombiner()
        {
            var combiner = _generator.meshCombiner;
            if (combiner is UMAJobifiedMeshCombiner) return "Jobified Combiner";
            if (combiner is UMABoneBakingMeshCombiner) return "Bone Baking Combiner";
            if (combiner is UMADefaultMeshCombiner) return "Default Combiner";
            return combiner == null ? "None" : combiner.GetType().Name;
        }

        private void ApplyCombiner(CombinerMode mode)
        {
            switch (mode)
            {
                case CombinerMode.Jobified:
                    UseMeshCombiner<UMAJobifiedMeshCombiner>();
                    break;
                case CombinerMode.BoneBaking:
                    UseMeshCombiner<UMABoneBakingMeshCombiner>();
                    break;
                case CombinerMode.Default:
                    UseMeshCombiner<UMADefaultMeshCombiner>();
                    break;
            }
            Repaint();
        }

        private void UseMeshCombiner<T>(UMAGenerator gen = null)
            where T : UMAMeshCombiner
        {
            var generator = gen ?? _generator;
            if (generator == null) return;

            if (generator.meshCombiner is T)
                return;

            var meshCombiner = Object.FindFirstObjectByType<T>();
            if (meshCombiner == null)
            {
                var go = new GameObject(typeof(T).Name);
                go.transform.parent = generator.transform.parent;
                meshCombiner = go.AddComponent<T>();
            }

            Undo.RecordObject(generator, "Switch Mesh Combiner");
            generator.meshCombiner = meshCombiner;
            if (PrefabUtility.IsPartOfAnyPrefab(generator))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
            }

            Debug.Log($"[UMA] Mesh combiner switched to {typeof(T).Name}");
        }
    }
}
