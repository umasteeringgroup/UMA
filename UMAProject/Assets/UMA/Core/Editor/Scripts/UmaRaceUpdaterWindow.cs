using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.Editors
{
	public class UmaRaceUpdaterWindow : EditorWindow 
	{
		private class CheckedSlot
		{
			public SlotData Slot;
			public bool Checked;
			public CheckedSlot(SlotData slotData, bool isChecked)
			{
				Slot = slotData;
				Checked = isChecked;
			}
		}
        
        /* private class CheckedMaterial
		{
			public UMAMaterial Material;
			public bool Checked;
			public CheckedMaterial(UMAMaterial material, bool isChecked)
			{
				Material = material;
				Checked = isChecked;
			}
		}*/

		public RaceData Race;
		public UMAMaterial Material;
		private UMAData.UMARecipe Recipe;
		private UMAContextBase Context;
		private List<CheckedSlot> Slots = new List<CheckedSlot>();

		//private List<CheckedMaterial> Materials = new List<CheckedMaterial>();

		void Refresh()
		{
			Context = UMAContextBase.Instance;
			if( Context == null)
			{
				EditorUtility.DisplayDialog("Error", "There is no UMA Context in the current scene!", "OK");
				return;
			}

			Slots.Clear();

			if (Race == null)
			{
				EditorUtility.DisplayDialog("Error", "No RaceData selected!", "OK");
				return;
			}

			Recipe = new UMAData.UMARecipe();
			Race.baseRaceRecipe.Load(Recipe, Context);

			if (Recipe == null)
            {
                return;
            }

            foreach (SlotData s in Recipe.slotDataList)
			{
				if (s == null)
                {
                    continue;
                }

                Slots.Add(new CheckedSlot(s, true));
			}
		}

		void OnGUI()
		{
			GUILayout.Label("UMA Race Updater");
			GUILayout.Space(10);
			RaceData lastRace = Race;
			Race = EditorGUILayout.ObjectField("Race to Update ", Race, typeof(RaceData), false) as RaceData;
			Material = EditorGUILayout.ObjectField("New Material ",Material,typeof(UMAMaterial),false) as UMAMaterial;

			
			if (Race != lastRace)
			{
				Refresh();
			}

			GUILayout.Space(10);
			if (Slots.Count < 1)
			{
				GUILayout.Label("No slots found");
			}
			else
			{
				GUILayout.Label("Process these slots");
				foreach(CheckedSlot kp in Slots)
				{
					kp.Checked = GUILayout.Toggle(kp.Checked, kp.Slot.slotName+" ("+kp.Slot.asset.material.name+")");
				}

				GUILayout.Space(10);
				GUILayout.BeginHorizontal();
				if(GUILayout.Button("Change Materials"))
				{
					if (Material == null)
					{
						EditorUtility.DisplayDialog("Error", "Must Select an UMA Material", "OK");
					}
					else
					{
						foreach (CheckedSlot checkedSlot in Slots)
						{
							if (checkedSlot.Checked)
							{
								checkedSlot.Slot.asset.material = Material;
								EditorUtility.SetDirty(checkedSlot.Slot.asset);
								
								// also update the overlay slots
								foreach (var overlaySlot in checkedSlot.Slot.GetOverlayList())
								{
									overlaySlot.asset.material = Material;
									EditorUtility.SetDirty(overlaySlot.asset);
								}
							}
						}
						AssetDatabase.SaveAssets();
					}
				}
				if (GUILayout.Button("Refresh List"))
				{
					Refresh();
				}
				GUILayout.EndHorizontal();
			}
		}


		[MenuItem("UMA/Race Updater")]
		public static void OpenUmaTexturePrepareWindow()
		{
			UmaRaceUpdaterWindow window = (UmaRaceUpdaterWindow)EditorWindow.GetWindow(typeof(UmaRaceUpdaterWindow));
			window.titleContent.text = "Race Updater";
		}
	}
}
