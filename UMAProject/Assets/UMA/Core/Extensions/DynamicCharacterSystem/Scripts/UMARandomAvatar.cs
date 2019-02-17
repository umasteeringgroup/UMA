using System;
using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
	public class UMARandomAvatar : MonoBehaviour
	{
		public UMARandomizer Randomizer;
		public GameObject prefab;
		public bool makeChild;

		private DynamicCharacterAvatar Avatar;
		private GameObject character;

		// Use this for initialization
		void Start()
		{
			if (prefab)
			{
				prefab = GameObject.Instantiate(prefab, transform.position, transform.rotation);
				if (makeChild)
				{
					prefab.transform.parent = this.transform;
				}
				Avatar = prefab.GetComponent<DynamicCharacterAvatar>();
			}
			Randomize(Avatar);
		}

		public RandomWardrobeSlot GetRandomWardrobe(List<RandomWardrobeSlot> wardrobeSlots)
		{
			int total = 0;

			foreach (RandomWardrobeSlot rws in wardrobeSlots)
				total += rws.Chance;

			foreach(RandomWardrobeSlot rws in wardrobeSlots)
			{
				if (UnityEngine.Random.Range(0,total) < rws.Chance)
				{
					return rws;
				}
			}
			return wardrobeSlots[wardrobeSlots.Count - 1];
		}

		private OverlayColorData GetRandomColor(RandomColors rc)
		{
			int inx = UnityEngine.Random.Range(0, rc.ColorTable.colors.Length);
			return rc.ColorTable.colors[inx];
		}

		private void AddRandomSlot(RandomWardrobeSlot uwr)
		{
			Avatar.SetSlot(uwr.WardrobeSlot);
		    if (uwr.Colors != null)
			{
				foreach(RandomColors rc in uwr.Colors)
				{
					if (rc.ColorTable != null)
					{
						OverlayColorData ocd = GetRandomColor(rc);
						Avatar.SetColor(rc.ColorName, ocd, false);
					}
				}
			}
		}

		public void Randomize(DynamicCharacterAvatar Avatar)
		{
			if (Avatar != null && Randomizer != null)
			{
				RandomAvatar ra = Randomizer.GetRandomAvatar();
				Avatar.RacePreset = ra.RaceName;
				Avatar.BuildCharacterEnabled = true;
				var RandomSlots = ra.GetRandomSlots();

				if (ra.SharedColors != null && ra.SharedColors.Count > 0)
				{
					foreach(RandomColors rc in ra.SharedColors)
					{
						if (rc.ColorTable != null)
						{
							Avatar.SetColor(rc.ColorName, GetRandomColor(rc), false);
						}
					}
				}
				foreach (string s in RandomSlots.Keys)
				{
					List<RandomWardrobeSlot> RandomWardrobe = RandomSlots[s];
					RandomWardrobeSlot uwr = GetRandomWardrobe(RandomWardrobe);
					AddRandomSlot(uwr);
				}
			}
		}
	}
}