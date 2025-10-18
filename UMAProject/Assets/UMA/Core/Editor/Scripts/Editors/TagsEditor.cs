#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public static class TagsEditor
    {
        public static Dictionary<string, bool> _foldout = new Dictionary<string, bool>();

        public static string[] RaceNames = null;


        public static void DoRaceGUI(ref bool Changed, SlotData slotData)
        {
            if (slotData.Races == null)
            {
                slotData.Races = new string[0];
            }
            if (true)
            {
                GUILayout.Space(10);
                GUILayout.Label("Only add for these Races:");
                // do the race matches here.
                if (RaceNames == null)
                {
                    List<string> theRaceNames = new List<string>();
                    RaceData[] races = UMAAssetIndexer.Instance.GetAllRaces();
                    foreach (RaceData race in races)
                    {
                        if (race != null)
                        {
                            theRaceNames.Add(race.raceName);
                        }
                    }
                    RaceNames = theRaceNames.ToArray();
                }
                GUILayout.BeginHorizontal();
                if (!SlotEditor.SelectedRace.ContainsKey(slotData.slotName))
                {
                    SlotEditor.SelectedRace.Add(slotData.slotName, 0);
                }

                SlotEditor.SelectedRace[slotData.slotName] = EditorGUILayout.Popup(SlotEditor.SelectedRace[slotData.slotName], RaceNames, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Add Race"))
                {
                    // Add the selected race name if it's not already there.
                    string theRace = RaceNames[SlotEditor.SelectedRace[slotData.slotName]];
                    List<string> Races = new List<string>(slotData.Races);
                    if (!Races.Contains(theRace))
                    {
                        Races.Add(theRace);
                        slotData.Races = Races.ToArray();
                        Changed = true;
                    }
                }
                GUILayout.EndHorizontal();

                DoTagsDisplay(ref slotData.Races, ref Changed);

                EditorGUI.BeginChangeCheck();
                slotData.isSwapSlot = EditorGUILayout.Toggle("This is a swap slot", slotData.isSwapSlot);
                if (slotData.isSwapSlot)
                {
                    EditorGUILayout.HelpBox("A Swap slot will only be added if there is a slot with the below tag already in the recipe. If there is no slot with the tag then this slot will not be added.", MessageType.Info);
                    string newSwapTag = CharacterBaseEditor.DoTagSelector(slotData.swapTag);
                    if (!string.IsNullOrEmpty(newSwapTag))
                    {
                        slotData.swapTag = newSwapTag;
                        Changed = true;
                    }
                    slotData.swapTag = EditorGUILayout.DelayedTextField("Swap slot(s) with this tag", slotData.swapTag);
                }
                else
                {
                    slotData.swapTag = "";
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Changed = true;
                }
            }
        }

        const string focusctrl = "TheButtonThatNeedsToFocusSoTheTextInTheTextBoxDisappears";
        public static string DoTagsGUI(ref bool Changed, string TempTag, SlotData slotData)
        {
            string slotName = slotData.slotName;

            if (!_foldout.ContainsKey(slotName))
            {
                _foldout.Add(slotName, false);
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            _foldout[slotName] = EditorGUILayout.Foldout(_foldout[slotName], "Matching Criteria");
            GUILayout.EndHorizontal();
            if (_foldout[slotName])
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                if (slotData.asset.isWildCardSlot)
                {
                    GUILayout.Label("Match Tags:");
                }
                else
                {
                    GUILayout.Label("Edit tags for this slot:");
                }
                
                string newTag = CharacterBaseEditor.DoTagSelector(slotData.asset.tags);
                if (!string.IsNullOrEmpty(newTag))
                {
                    Changed |= AddSlotTag(newTag, slotData);
                }
                if (slotData.tags == null)
                {
                    slotData.tags = new string[0];
                }
                if (slotData.Races == null)
                {
                    slotData.Races = new string[0];
                }
                
                GUILayout.BeginHorizontal();
                TempTag = EditorGUILayout.TextField(TempTag, GUILayout.ExpandWidth(true));
                GUI.SetNextControlName(focusctrl);
                if (GUILayout.Button("x", GUILayout.Width(18)))
                {
                    TempTag = "";
                    GUI.FocusControl(focusctrl);
                }
                if (GUILayout.Button("Add Tag"))
                {
                    if (!string.IsNullOrWhiteSpace(TempTag))
                    {
                        Changed |= AddSlotTag(TempTag, slotData);
                    }
                }
                if (GUILayout.Button("Clear"))
                {
                    slotData.tags = new string[0];
                    Changed = true;
                }
                if (GUILayout.Button("Load"))
                {
                    string fname = EditorUtility.OpenFilePanel("Load", "", "txt");
                    {
                        if (!string.IsNullOrEmpty(fname))
                        {
                            slotData.tags = File.ReadAllLines(fname);
                            Changed = true;
                        }
                    }
                }
                if (GUILayout.Button("Save"))
                {
                    string fname = EditorUtility.SaveFilePanel("Save", "", "Tags", "txt");
                    {
                        if (!string.IsNullOrEmpty(fname))
                        {
                            File.WriteAllLines(fname, slotData.tags);
                        }
                    }
                }

                GUILayout.EndHorizontal();

                DoTagsDisplay(ref slotData.tags, ref Changed);
                
                GUILayout.Space(10);
                GUILayout.Label("Only add for these Races:");
                if (RaceNames == null)
                {
                    List<string> theRaceNames = new List<string>();
                    RaceData[] races = UMAAssetIndexer.Instance.GetAllRaces();
                    foreach (RaceData race in races)
                    {
                        if (race != null)
                        {
                            theRaceNames.Add(race.raceName);
                        }
                    }
                    RaceNames = theRaceNames.ToArray();
                }
                GUILayout.BeginHorizontal();
                if (!SlotEditor.SelectedRace.ContainsKey(slotData.slotName))
                {
                    SlotEditor.SelectedRace.Add(slotData.slotName, 0);
                }

                SlotEditor.SelectedRace[slotData.slotName] = EditorGUILayout.Popup(SlotEditor.SelectedRace[slotData.slotName], RaceNames, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Add Race"))
                {
                    string theRace = RaceNames[SlotEditor.SelectedRace[slotData.slotName]];
                    List<string> Races = new List<string>(slotData.Races);
                    if (!Races.Contains(theRace))
                    {
                        Races.Add(theRace);
                        slotData.Races = Races.ToArray();
                        Changed = true;
                    }
                }
                GUILayout.EndHorizontal();

                DoTagsDisplay(ref slotData.Races, ref Changed);

                EditorGUI.BeginChangeCheck();
                slotData.isSwapSlot = EditorGUILayout.Toggle("This is a swap slot", slotData.isSwapSlot);
                if (slotData.isSwapSlot)
                {
                    EditorGUILayout.HelpBox("A Swap slot will only be added if there is a slot with the below tag already in the recipe. If there is no slot with the tag then this slot will not be added.", MessageType.Info);
                    string newSwapTag = CharacterBaseEditor.DoTagSelector(slotData.swapTag);
                    if (!string.IsNullOrEmpty(newSwapTag))
                    {
                        slotData.swapTag = newSwapTag;
                        Changed = true;
                    }
                    slotData.swapTag = EditorGUILayout.DelayedTextField("Swap slot(s) with this tag", slotData.swapTag);
                }
                else
                {
                    slotData.swapTag = "";
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Changed = true;
                }
                GUIHelper.EndVerticalPadded(10);
            }
            return TempTag;
        }

        private static bool AddSlotTag(string TempTag, SlotData slotData)
        {
            bool Changed = false;
            var tagList = new List<string>(slotData.tags);
            if (!tagList.Contains(TempTag))
            {
                tagList.Add(TempTag);
                slotData.tags = tagList.ToArray();
                Changed = true;
            }

            return Changed;
        }

        public static int DoTagsDisplay(ref string[] tags, ref bool changed)
        {
            int deleted = -1;

            for (int i = 0; i < tags.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(tags[i], EditorStyles.textField, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("X", GUILayout.Width(16)))
                {
                    deleted = i;
                }
                GUILayout.EndHorizontal();
            }
            if (deleted > -1)
            {
                var tagList = new List<string>(tags);
                tagList.RemoveAt(deleted);
                tags = tagList.ToArray();
                changed = true;
            }
            return -1;
        }
    }
}
#endif
