using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    public class UMAMeshCombinerSwitcherWindow : EditorWindow
    {
        private enum CombinerMode
        {
            Jobified,
            Incremental,
            DefaultBoneBaking,
            BoneBakingCompatibility,
            Default
        }

        private UMAGenerator _generator;
        private UMAGeneratorOverride _generatorParms;
        private CombinerMode _selected;
        private Vector2 _scrollPos;

        [MenuItem("UMA/Tools/Mesh Tools/Mesh Combiner Switcher", priority = 111)]
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
            _generator = UMAToolbarActions.GetGenerator();
            _generatorParms = _generator == null
                ? UMAToolbarActions.GetGeneratorParms()
                : null;
        }

        private void OnGUI()
        {
            if (_generator == null && _generatorParms == null)
            {
                RefreshGenerator();
                if (_generator == null && _generatorParms == null)
                {
                    EditorGUILayout.HelpBox(
                        "No scene UMAGenerator or GeneratorParms object was found.",
                        MessageType.Warning);
                    return;
                }
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "Switching mesh combiners requires a full character rebuild to take effect. " +
                "Existing generated characters will not reflect the change until they are rebuilt.",
                MessageType.Warning);

            EditorGUILayout.Space(6f);

            // ── Current combiner indicator ──────────────────────
            EditorGUILayout.LabelField(
                _generator != null ? "Generator:" : "Generator Parameters:",
                _generator != null ? _generator.name : _generatorParms.name,
                EditorStyles.miniLabel);
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

            DrawCombinerToggle("Incremental", CombinerMode.Incremental,
                "Amortizes mesh generation and blendshape loading over multiple frames using the generator's Max Multi-Step Work budget.",
                current);

            DrawCombinerToggle("Default Bone Baking", CombinerMode.DefaultBoneBaking,
                "Recommended bone-baking combiner. Reuses the Default combiner's renderer, material, and multi-renderer pipeline while baking unused bones.",
                current);

            DrawCombinerToggle("Bone Baking (Compatibility)", CombinerMode.BoneBakingCompatibility,
                "Compatibility component for existing scenes that reference UMABoneBakingMeshCombiner. It uses the same default-derived baking implementation.",
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
            UMAMeshCombiner combiner = _generator != null
                ? _generator.meshCombiner
                : _generatorParms != null ? _generatorParms.meshCombiner : null;
            if (combiner is UMAJobifiedMeshCombiner) return "Jobified Combiner";
            if (combiner is UMAIncrementalMeshCombiner) return "Incremental Combiner";
            if (combiner != null && combiner.GetType() == typeof(UMADefaultBoneBakingMeshCombiner)) return "Default Bone Baking Combiner";
            if (combiner != null && combiner.GetType() == typeof(UMABoneBakingMeshCombiner)) return "Bone Baking (Compatibility) Combiner";
            if (combiner is UMADefaultBoneBakingMeshCombiner) return "Default Bone Baking Combiner";
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
                case CombinerMode.Incremental:
                    UseMeshCombiner<UMAIncrementalMeshCombiner>();
                    break;
                case CombinerMode.DefaultBoneBaking:
                    UseMeshCombiner<UMADefaultBoneBakingMeshCombiner>();
                    break;
                case CombinerMode.BoneBakingCompatibility:
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
            UMAGenerator targetGenerator = gen ?? _generator;
            UMAToolbarActions.UseMeshCombinerForTargets<T>(
                targetGenerator,
                targetGenerator == null ? _generatorParms : null);
        }
    }
}
