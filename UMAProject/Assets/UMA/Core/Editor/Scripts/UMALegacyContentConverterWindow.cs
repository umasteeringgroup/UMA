using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Dialog for <see cref="UMALegacyContentConverter"/>: brings a folder of UMA 2 wardrobe recipes to
    /// the UMA 3 races (mesh hides for the legacy body slots, new races added, retired races removed).
    /// </summary>
    public sealed class UMALegacyContentConverterWindow : EditorWindow
    {
        [SerializeField] private DefaultAsset contentFolder;
        [SerializeField] private UMALegacyContentConversionSettings settings = new UMALegacyContentConversionSettings();
        [SerializeField] private List<bool> slotFoldouts = new List<bool>();
        [SerializeField] private string lastReport = string.Empty;
        [SerializeField] private bool initialised;
        private Vector2 scroll;
        private Vector2 reportScroll;

        [MenuItem("UMA/Tools/Slot Tools/Convert Legacy Content")]
        public static UMALegacyContentConverterWindow Open()
        {
            var window = GetWindow<UMALegacyContentConverterWindow>("Legacy Content");
            window.minSize = new Vector2(420f, 360f);
            return window;
        }

        [MenuItem("Assets/UMA/Convert Legacy Content In Folder", false, 2000)]
        private static void OpenForSelectedFolder()
        {
            UMALegacyContentConverterWindow window = Open();
            window.contentFolder = SelectedFolder();
        }

        [MenuItem("Assets/UMA/Convert Legacy Content In Folder", true)]
        private static bool CanOpenForSelectedFolder()
        {
            return SelectedFolder() != null;
        }

        private static DefaultAsset SelectedFolder()
        {
            var folder = Selection.activeObject as DefaultAsset;
            return folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)) ? folder : null;
        }

        private void OnEnable()
        {
            if (!initialised)
            {
                settings.mappings = UMALegacyContentConverter.DefaultMappings();
                initialised = true;
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Convert legacy (UMA 2) content to the UMA 3 races", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "For each wardrobe recipe in the folder that is compatible with a legacy race:\n" +
                "• the legacy body slots it hides (MaleTorso, FemaleFeet…) become Mesh Hide Assets on the new race's body,\n" +
                "• the new race is added to its compatible races,\n" +
                "• the retired races (HumanMaleDCS…) are removed.\n" +
                "The hides are saved in a MeshHides folder inside the content folder, so they travel with the recipes. Running it again updates them in place.",
                MessageType.Info);

            contentFolder = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Content folder", "Searched with its subfolders."), contentFolder, typeof(DefaultAsset), false);
            string folderPath = contentFolder != null ? AssetDatabase.GetAssetPath(contentFolder) : null;
            bool folderValid = !string.IsNullOrEmpty(folderPath) && AssetDatabase.IsValidFolder(folderPath);
            if (contentFolder != null && !folderValid)
            {
                EditorGUILayout.HelpBox("That is not a folder.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            settings.createMeshHides = EditorGUILayout.ToggleLeft(new GUIContent("Create mesh hides for the legacy body slots", Tooltip(nameof(settings.createMeshHides))), settings.createMeshHides);
            using (new EditorGUI.DisabledScope(!settings.createMeshHides))
            {
                EditorGUI.indentLevel++;
                settings.smoothingPasses = EditorGUILayout.IntSlider(new GUIContent("Seam smoothing", Tooltip(nameof(settings.smoothingPasses))), settings.smoothingPasses, 0, 10);
                EditorGUI.indentLevel--;
            }
            settings.addNewRace = EditorGUILayout.ToggleLeft(new GUIContent("Add the new race to the recipes", Tooltip(nameof(settings.addNewRace))), settings.addNewRace);
            settings.removeRaces = EditorGUILayout.ToggleLeft(new GUIContent("Remove the retired races from the recipes", Tooltip(nameof(settings.removeRaces))), settings.removeRaces);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Races", EditorStyles.boldLabel);
            if (GUILayout.Button("Add", GUILayout.Width(60f)))
            {
                settings.mappings.Add(new UMALegacyRaceMapping());
            }
            if (GUILayout.Button(new GUIContent("Defaults", "HumanMale → Human Male 3.0 and HumanFemale → Human Female 3.0."), GUILayout.Width(70f)))
            {
                settings.mappings = UMALegacyContentConverter.DefaultMappings();
                slotFoldouts.Clear();
            }
            EditorGUILayout.EndHorizontal();

            for (int m = 0; m < settings.mappings.Count; m++)
            {
                if (DrawMapping(m))
                {
                    break;
                }
            }

            EditorGUILayout.Space();
            bool ready = folderValid && settings.mappings.Count > 0 && settings.mappings.All(x => x != null && x.legacyRace != null && x.newRace != null);
            using (new EditorGUI.DisabledScope(!ready))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Preview", "Lists what would change. Nothing is written."), GUILayout.Height(28f)))
                {
                    Run(folderPath, true);
                }
                if (GUILayout.Button("Convert", GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog("Convert legacy content",
                        $"Change the wardrobe recipes in '{folderPath}' and write Mesh Hide Assets to '{folderPath}/{UMALegacyContentConverter.HideFolderName}'?\n\n" +
                        "Recipe edits can be undone with Undo; created hide assets are kept.", "Convert", "Cancel"))
                    {
                        Run(folderPath, false);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last run", EditorStyles.boldLabel);
                reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(400f));
                EditorGUILayout.TextArea(lastReport, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Draws one mapping. Returns true when it was removed (stop drawing this frame).</summary>
        private bool DrawMapping(int index)
        {
            UMALegacyRaceMapping mapping = settings.mappings[index];
            while (slotFoldouts.Count <= index)
            {
                slotFoldouts.Add(false);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            RaceData legacy = (RaceData)EditorGUILayout.ObjectField(mapping.legacyRace, typeof(RaceData), false);
            GUILayout.Label("→", GUILayout.Width(16f));
            RaceData replacement = (RaceData)EditorGUILayout.ObjectField(mapping.newRace, typeof(RaceData), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (legacy != mapping.legacyRace)
                {
                    mapping.legacyRace = legacy;
                    mapping.legacySlots.Clear();
                    mapping.racesToRemove.Clear();
                }
                if (replacement != mapping.newRace)
                {
                    mapping.newRace = replacement;
                    mapping.targetSlots.Clear();
                }
                UMALegacyContentConverter.ApplyDefaults(mapping);
            }
            if (GUILayout.Button("-", GUILayout.Width(22f)))
            {
                settings.mappings.RemoveAt(index);
                slotFoldouts.RemoveAt(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return true;
            }
            EditorGUILayout.EndHorizontal();

            string remove = EditorGUILayout.DelayedTextField(new GUIContent("Races to remove", "Comma separated."), string.Join(", ", mapping.racesToRemove));
            mapping.racesToRemove = remove.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            if (settings.createMeshHides && mapping.legacyRace != null && mapping.newRace != null)
            {
                slotFoldouts[index] = EditorGUILayout.Foldout(slotFoldouts[index],
                    $"Body slots: {mapping.legacySlots.Count} legacy → {mapping.targetSlots.Count} new", true);
                if (slotFoldouts[index])
                {
                    EditorGUI.indentLevel++;
                    DrawSlotChoice("Legacy slots that become hides", mapping.legacyRace, mapping.legacySlots);
                    DrawSlotChoice("New body slots the hides apply to", mapping.newRace, mapping.targetSlots);
                    if (GUILayout.Button("Reset slots to defaults"))
                    {
                        mapping.legacySlots.Clear();
                        mapping.targetSlots.Clear();
                        UMALegacyContentConverter.ApplyDefaults(mapping);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            if (mapping.newRace != null && mapping.legacyRace != null &&
                (mapping.newRace.legacyRig == null || mapping.newRace.legacyRig.sourceRaceName != mapping.legacyRace.raceName))
            {
                EditorGUILayout.HelpBox($"{mapping.newRace.raceName} has no Legacy Rig baked from {mapping.legacyRace.raceName}. " +
                    "The hides are still made, but legacy clothing is not converted automatically on that race until it has one.", MessageType.None);
            }
            EditorGUILayout.EndVertical();
            return false;
        }

        private static void DrawSlotChoice(string label, RaceData race, List<string> chosen)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            foreach (string slot in UMALegacyContentConverter.GetBaseSlotNames(race, false))
            {
                bool on = chosen.Contains(slot);
                bool now = EditorGUILayout.ToggleLeft(slot, on);
                if (now && !on) chosen.Add(slot);
                if (!now && on) chosen.Remove(slot);
            }
        }

        private void Run(string folderPath, bool preview)
        {
            settings.contentFolder = folderPath;
            UMALegacyContentConversionResult result;
            try
            {
                result = UMALegacyContentConverter.Convert(settings, preview);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                lastReport = "Failed: " + ex.GetType().Name + ": " + ex.Message;
                return;
            }
            lastReport = result.Summary();
            Debug.Log("[UMA] Legacy content conversion\n" + lastReport);
            if (!preview && result.CreatedHides.Count + result.UpdatedHides.Count > 0)
            {
                EditorGUIUtility.PingObject(result.CreatedHides.Count > 0 ? result.CreatedHides[0] : result.UpdatedHides[0]);
            }
        }

        private static string Tooltip(string field)
        {
            var info = typeof(UMALegacyContentConversionSettings).GetField(field);
            var attribute = info != null ? (TooltipAttribute)Attribute.GetCustomAttribute(info, typeof(TooltipAttribute)) : null;
            return attribute != null ? attribute.tooltip : string.Empty;
        }
    }
}
