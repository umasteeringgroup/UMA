using System;
using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// What Publish asks for before it publishes: the name the slot, overlay and wardrobe recipe take, the wardrobe
    /// region the recipe goes in, the UMA material and the folder. The recipe is made compatible with the race of the
    /// character in the designer.
    /// </summary>
    public sealed class PublishChoice
    {
        public string name;
        public string wardrobeSlot;
        public UMAMaterial material;
        /// <summary>An overlay the clothes use instead of making their own; with it no material is needed.</summary>
        public OverlayDataAsset existingOverlay;
        public string folder;
        /// <summary>The race the recipe is made compatible with: the race of the character in the designer.</summary>
        public RaceData race;
        /// <summary>The name the clothes were last published under, or null when they never were.</summary>
        public string publishedName;
        /// <summary>Set by the window when the artist pressed Publish (or Publish and Open in Overlay Painter).</summary>
        public bool accepted;
        /// <summary>
        /// Set by the window's Publish and Open in Overlay Painter: once published, the Clothes Designer closes and the
        /// Overlay Painter opens on the slot and its overlay, with the pattern's trims as editable path layers.
        /// </summary>
        public bool openInPainter;
        /// <summary>How many trims the pattern has (zippers, stitching, piping): they go to the Overlay Painter as path layers.</summary>
        public int trims;

        /// <summary>
        /// Whether the Overlay Painter is installed: it registers itself with core UMA (<see cref="UMATrimPainting"/>),
        /// so the designer finds it without depending on it.
        /// </summary>
        public static bool PainterInstalled => UMATrimPainting.Painter != null;

        /// <summary>Publishing again under the same name updates the assets made last time.</summary>
        public bool UpdatesEarlierPublish => !string.IsNullOrEmpty(publishedName) && string.Equals(publishedName, (name ?? string.Empty).Trim(), StringComparison.Ordinal);

        /// <summary>The wardrobe regions a race offers, in its own order, without "None" and blanks.</summary>
        public static List<string> Regions(RaceData race)
        {
            var list = new List<string>();
            if (race == null || race.wardrobeSlots == null) return list;
            foreach (string region in race.wardrobeSlots)
            {
                if (string.IsNullOrWhiteSpace(region) || region == "None" || list.Contains(region)) continue;
                list.Add(region);
            }
            return list;
        }

        /// <summary>
        /// The choice the window starts from. The region is filled in only when the clothes were published before: until
        /// then the settings hold a default nobody chose, so the artist is asked. A default name takes the pattern's.
        /// </summary>
        public static PublishChoice Prefill(PublishSettings settings, string publishedName, string patternName, RaceData race)
        {
            bool published = !string.IsNullOrEmpty(publishedName);
            var choice = new PublishChoice
            {
                name = settings.slotName,
                material = settings.material,
                existingOverlay = settings.existingOverlay,
                folder = settings.folder,
                race = race,
                publishedName = published ? publishedName : null,
                wardrobeSlot = published && Regions(race).Contains(settings.wardrobeSlot) ? settings.wardrobeSlot : null
            };
            if (string.IsNullOrWhiteSpace(choice.name) || choice.name == new PublishSettings().slotName) choice.name = NameFrom(patternName);
            return choice;
        }

        /// <summary>A pattern's name as an asset name: its letters, digits, '_' and '-', the words run together ("Polo Shirt" becomes "PoloShirt").</summary>
        public static string NameFrom(string patternName)
        {
            string fallback = new PublishSettings().slotName;
            if (string.IsNullOrWhiteSpace(patternName)) return fallback;
            var text = new System.Text.StringBuilder();
            foreach (char c in patternName) if (char.IsLetterOrDigit(c) || c == '_' || c == '-') text.Append(c);
            return text.Length > 0 ? text.ToString() : fallback;
        }

        /// <summary>What keeps the choice from being published, in the artist's words. Empty when it can go ahead.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) problems.Add("Enter a name for the clothes.");
            else if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.Contains("/")) problems.Add("The name cannot be used as a file name.");
            if (race == null) problems.Add("The character has no race to make the clothes compatible with.");
            if (string.IsNullOrWhiteSpace(wardrobeSlot)) problems.Add("Choose a wardrobe region.");
            else if (race != null && !Regions(race).Contains(wardrobeSlot)) problems.Add("'" + race.raceName + "' has no wardrobe region '" + wardrobeSlot + "'.");
            if (material == null && existingOverlay == null) problems.Add("Choose a UMA material for the clothes.");
            string f = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (f != "Assets" && !f.StartsWith("Assets/", StringComparison.Ordinal)) problems.Add("Choose a folder inside Assets.");
            return problems;
        }
    }

    /// <summary>
    /// The Publish window: asks for the clothes' name, wardrobe region, material and folder, then publishes them (see
    /// <see cref="DesignerSession.Publish"/>). Opened from the Cloth Tools toolbar and the panel's Publish section.
    /// </summary>
    public sealed class PublishClothesWindow : EditorWindow
    {
        public const string Tip = "Publish: save the clothes as a slot, an overlay and a wardrobe recipe, add them to the Global Library and make the recipe compatible with this character's race. Asks for the name and the wardrobe region first; also saves the pattern and fit. With the Overlay Painter installed, the window can also open the published clothes there to texture them, with the pattern's trims as path layers.";

        private PublishChoice choice;
        private List<string> regions;
        private string[] regionLabels;

        /// <summary>
        /// Asks, then publishes the session's clothes. False when the artist cancelled, or publishing failed (then a
        /// dialog says why).
        /// </summary>
        public static bool AskAndPublish(DesignerSession session)
        {
            if (session == null || session.IsDisposed || session.FitData == null) return false;
            if (!session.Fit.HasCloth || !session.Fit.AllSewn)
            {
                EditorUtility.DisplayDialog("Publish Clothes", "Simulate until the seams are sewn and the cloth hangs, then publish.", "OK");
                return false;
            }
            RaceData race = session.Stage != null && session.Stage.CharacterRace != null ? session.Stage.CharacterRace : session.FitData.race;
            string published = session.FitData.output.recipe != null ? session.FitData.publish.slotName : null;
            PublishChoice choice = PublishChoice.Prefill(session.FitData.publish, published, session.Pattern != null ? session.Pattern.name : null, race);
            choice.trims = session.Pattern != null && session.Pattern.trims != null ? session.Pattern.trims.Count : 0;
            var window = CreateInstance<PublishClothesWindow>();
            window.titleContent = new GUIContent("Publish Clothes");
            window.Bind(choice);
            window.minSize = new Vector2(480f, 310f);
            window.maxSize = new Vector2(700f, 310f);
            window.ShowModalUtility();
            if (!choice.accepted) return false;
            if (session.IsDisposed) return false;
            if (session.Publish(choice))
            {
                if (choice.openInPainter && !session.OpenInOverlayPainter())
                {
                    EditorUtility.DisplayDialog("Publish Clothes", "Published, but the Overlay Painter was not opened: " + session.LastBakeError, "OK");
                }
                return true;
            }
            EditorUtility.DisplayDialog("Publish Clothes", "Not published: " + (session.LastBakeError ?? "the bake did not finish."), "OK");
            return false;
        }

        private void Bind(PublishChoice c)
        {
            choice = c;
            regions = PublishChoice.Regions(c.race);
            var labels = new List<string> { "Choose..." };
            labels.AddRange(regions);
            regionLabels = labels.ToArray();
        }

        private void OnGUI()
        {
            if (choice == null) { Close(); return; }
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); GUIUtility.ExitGUI(); }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Publish the clothes as a slot, an overlay and a wardrobe recipe.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4f);
            choice.name = EditorGUILayout.TextField(new GUIContent("Name", "The slot, overlay and wardrobe recipe are named after it."), choice.name);
            int index = string.IsNullOrEmpty(choice.wardrobeSlot) ? 0 : regions.IndexOf(choice.wardrobeSlot) + 1;
            int picked = EditorGUILayout.Popup(new GUIContent("Wardrobe Region", "Where the recipe goes on the character. A character wears one recipe in each region, so this also says what the clothes replace."), Mathf.Max(0, index), regionLabels);
            choice.wardrobeSlot = picked > 0 ? regions[picked - 1] : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("Compatible Race", "The recipe is made compatible with the race of the character in the designer."), choice.race != null ? choice.race.raceName : "(none)");
            }
            if (choice.existingOverlay == null || choice.material != null)
            {
                choice.material = (UMAMaterial)EditorGUILayout.ObjectField(new GUIContent("Material", "The UMA material the clothes are drawn with."), choice.material, typeof(UMAMaterial), false);
            }
            EditorGUILayout.BeginHorizontal();
            choice.folder = EditorGUILayout.TextField(new GUIContent("Folder", "Where the slot, overlay and recipe are saved."), choice.folder);
            if (GUILayout.Button("...", GUILayout.Width(26f)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Folder for the clothes", string.IsNullOrEmpty(choice.folder) ? "Assets" : choice.folder, "");
                string data = Application.dataPath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(chosen) && chosen.Replace('\\', '/').StartsWith(data, StringComparison.Ordinal))
                {
                    choice.folder = "Assets" + chosen.Replace('\\', '/').Substring(data.Length);
                }
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            string note = "Saved in the folder and added to the Global Library. The pattern and fit are saved too (you choose where the first time).";
            if (choice.UpdatesEarlierPublish) note += "\n\n'" + choice.publishedName + "' was published before: the same assets are updated and keep their GUIDs, overlay textures and anything added to the recipe. The slot's mesh is replaced, so edits made to it directly are lost.";
            if (PublishChoice.PainterInstalled) note += "\n\nPublish and Open in Overlay Painter then closes the Clothes Designer and opens the slot in the Overlay Painter to texture it" + (choice.trims > 0 ? ", with the pattern's " + choice.trims + (choice.trims == 1 ? " trim" : " trims") + " as editable path layers." : ".");
            EditorGUILayout.HelpBox(note, MessageType.Info);
            List<string> problems = choice.Problems();
            foreach (string problem in problems) GUILayout.Label("• " + problem, EditorStyles.wordWrappedMiniLabel);

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f))) { Close(); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(problems.Count > 0))
            {
                if (GUILayout.Button("Publish", GUILayout.Width(90f)))
                {
                    choice.accepted = true;
                    Close();
                    GUIUtility.ExitGUI();
                }
                // Only with the Overlay Painter installed.
                if (PublishChoice.PainterInstalled && GUILayout.Button(new GUIContent("Publish and Open in Overlay Painter",
                    "Publish, then close the Clothes Designer and open the slot in the Overlay Painter to texture it, with the pattern's trims (zippers, stitching, piping, hems) as editable path layers."), GUILayout.Width(230f)))
                {
                    choice.accepted = true;
                    choice.openInPainter = true;
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
        }
    }
}
