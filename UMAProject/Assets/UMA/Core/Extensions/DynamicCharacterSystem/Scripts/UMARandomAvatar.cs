using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
	public class UMARandomAvatar : MonoBehaviour
	{
		public List<UMARandomizer> Randomizers;
		public GameObject prefab;
		public GameObject ParentObject;
		public bool ShowPlaceholder;
		public bool GenerateGrid;
		public int GridXSize = 5;
		public int GridZSize = 4;
		public float GridDistance = 1.5f;
		public float RandomOffset = 0.0f;
		public bool RandomRotation;
		public string NameBase = "Pat";
		public UMARandomAvatarEvent RandomAvatarGenerated;

		private DynamicCharacterAvatar RandomAvatar;
		private GameObject character;

		// Use this for initialization
		void Start()
		{
			if (ParentObject == null)
			{
				ParentObject = this.gameObject;
			}

			if (!GenerateGrid)
			{
				if (RandomRotation)
                {
                    GenerateRandomCharacter(transform.position, RandRotation(transform.rotation),NameBase);
                }
                else
                {
                    GenerateRandomCharacter(transform.position, transform.rotation, NameBase);
                }
            } 
			else
			{
				float xstart = 0-((GridXSize * GridDistance) / 2.0f);
				int i = 0;
				for (int x=0;x<GridXSize;x++)
				{
					float zstart = 0-((GridZSize * GridDistance) / 2.0f);
					for (int z=0;z<GridZSize;z++)
					{
						Vector3 pos = new Vector3(transform.position.x + xstart, transform.position.y, transform.position.z + zstart);
						if (RandomOffset != 0.0f)
						{
							pos.x = pos.x + Random.Range(-RandomOffset, RandomOffset);
							pos.z = pos.z + Random.Range(-RandomOffset, RandomOffset);
						}
						if (RandomRotation)
                        {
                            GenerateRandomCharacter(pos, RandRotation(transform.rotation),NameBase + " "+ i);
                        }
                        else
                        {
                            GenerateRandomCharacter(pos, transform.rotation, NameBase + " " + i);
                        }

                        ++i;
						zstart += GridDistance;
					}
					xstart += GridDistance;
				}
			}
		}

		private Quaternion RandRotation(Quaternion src)
		{
			Vector3 Euler = src.eulerAngles;
			return Quaternion.Euler(Euler.x, Random.Range(0.0f, 359.9f), Euler.z);
		}


		public void GenerateRandomCharacter(Vector3 Pos, Quaternion Rot, string Name)
		{
			if (prefab)
			{
				GameObject go = GameObject.Instantiate(prefab, Pos, Rot);
				if (ParentObject != null)
				{
					go.transform.parent = ParentObject.transform;
				}
				RandomAvatar = go.GetComponent<DynamicCharacterAvatar>();
				go.name = Name;
				// Event for possible networking here
				if (RandomAvatarGenerated != null)
				{
					RandomAvatarGenerated.Invoke(gameObject, go);
				}
			}
			Randomize(RandomAvatar);
			RandomAvatar.BuildCharacter(!RandomAvatar.BundleCheck);
		}

		public RandomWardrobeSlot GetRandomWardrobe(List<RandomWardrobeSlot> wardrobeSlots)
		{
			int total = 0;

            for (int i = 0; i < wardrobeSlots.Count; i++)
            {
                RandomWardrobeSlot rws = wardrobeSlots[i];
                total += rws.Chance;
            }

            for (int i = 0; i < wardrobeSlots.Count; i++)
			{
                RandomWardrobeSlot rws = wardrobeSlots[i];
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

		private void AddRandomSlot(DynamicCharacterAvatar Avatar, RandomWardrobeSlot uwr)
		{
			Avatar.SetSlot(uwr.WardrobeSlot);
		    if (uwr.Colors != null)
			{
                for (int i = 0; i < uwr.Colors.Count; i++)
				{
                    RandomColors rc = uwr.Colors[i];
                    if (rc.ColorTable != null)
					{
						OverlayColorData ocd = GetRandomColor(rc);
						Avatar.SetColor(rc.ColorName, ocd, false);
					}
				}
			}
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (ShowPlaceholder)
			{
				Gizmos.DrawCube(transform.position, Vector3.one);
			}
		}
#endif


		public void Randomize(DynamicCharacterAvatar Avatar)
		{
			// Must clear that out!
			Avatar.WardrobeRecipes.Clear();

			UMARandomizer Randomizer = null;
			if (Randomizers != null)
			{
				if (Randomizers.Count == 0)
                {
                    return;
                }

                if (Randomizers.Count == 1)
                {
                    Randomizer = Randomizers[0];
                }
                else
				{
					Randomizer = Randomizers[UnityEngine.Random.Range(0, Randomizers.Count)];
				}
			}
			if (Avatar != null && Randomizer != null)
			{
				RandomAvatar ra = Randomizer.GetRandomAvatar();
				Avatar.ChangeRaceData(ra.RaceName);
				//Avatar.BuildCharacterEnabled = true;
				var RandomDNA = ra.GetRandomDNA();
				Avatar.predefinedDNA = RandomDNA;
				var RandomSlots = ra.GetRandomSlots();

				if (ra.SharedColors != null && ra.SharedColors.Count > 0)
				{
                    for (int i = 0; i < ra.SharedColors.Count; i++)
					{
                        RandomColors rc = ra.SharedColors[i];
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
					if (uwr.WardrobeSlot != null)
					{
						AddRandomSlot(Avatar, uwr);
					}
				}
			}
		}
	}
}