using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace UMA
{
	public class UMARandomAvatarV2 : MonoBehaviour
	{
		// ------------- API ---------------
		public void RandomizeButton() { RandomizeAll(randChar: true, randWardrobe: true); }
		public void RandomizeCharacterButton() { RandomizeAll(randChar: true, randWardrobe: false); }
		public void RandomizeWardrobeButton() { RandomizeAll(randChar: false, randWardrobe: true); }
		// ---------------------------------


		[FormerlySerializedAs("Randomizers")]
		public List<UMARandomizer> CharacterRandomizers;
		public List<UMARandomizer> WardrobeRandomizers;

		public bool KeepExistingRace = false;
		public bool KeepExistingWardrobe = false;

		public void ToggleKeepExistingWardrobe(bool val) { KeepExistingWardrobe = val; }

		public enum Mode { Generate, UseExisting }
		public Mode mode;

		public CharacterGeneration Generation = new CharacterGeneration();
		// --- Character Generation ---
		[System.Serializable]
		public class CharacterGeneration
		{
			public GameObject Prefab;
			public GameObject ParentObject;

			public bool ShowPlaceholder, GenerateGrid;
			public int GridXSize = 5, GridZSize = 4;
			public float GridDistance = 1.5f, GridRandomOffset = 0.0f;
			public bool RandomRotation;
			public string NameBase = "Pat";
			public bool Sequential;

			public UMARandomAvatarEvent RandomAvatarGenerated;
			public Quaternion GetRotation => RandomRotation ? RandRotation(transform.rotation) : transform.rotation;

			private Transform transform;
			private List<DynamicCharacterAvatar> generatedDCAs = new List<DynamicCharacterAvatar>();

			public void Init(GameObject componentGO)
			{
				if (ParentObject == null)
					ParentObject = componentGO;

				transform = componentGO.transform;

				generatedDCAs.Clear();
			}

			public void Start(System.Action<DynamicCharacterAvatar, bool, bool> Randomization)
			{
				if (!GenerateGrid)
				{
					DynamicCharacterAvatar newDCA = GenerateCharacter(transform.position, GetRotation, NameBase);
					if (newDCA != null && Sequential) Randomization(newDCA, true, true);
				}
				else
				{
					List<Vector3> grid = GenerateGridPositions();

					if (grid == null || grid.Count == 0) return;

					for (int i = 0; i < grid.Count; i++)
					{
						DynamicCharacterAvatar newDCA = GenerateCharacter(grid[i], GetRotation, NameBase + " " + i);
						if (newDCA != null && Sequential) Randomization(newDCA, true, true);
					}
				}

				if (!Sequential) RandomizeAll(Randomization);
			}

			public void RandomizeAll(System.Action<DynamicCharacterAvatar, bool, bool> Randomization, bool randChar = true, bool randWardrobe = true)
			{
				if (generatedDCAs == null || generatedDCAs.Count == 0) return;

				foreach (DynamicCharacterAvatar DCA in generatedDCAs)
					Randomization(DCA, randChar, randWardrobe);
			}

			private List<Vector3> GenerateGridPositions()
			{
				List<Vector3> GridPositions = new List<Vector3>();
				// Hard limit
				float card = GridXSize * GridZSize / GridDistance;
				if (card > 1000)
				{
					Debug.LogWarning($"Random Character Generation Aborted : Too much Characters {card}.\nReduce the Grid Size (X or Z) or increase the Grid Distance to recude the number of Characters to generate.");
					return GridPositions;
				}

				float xstart = 0 - ((GridXSize * GridDistance) / 2.0f);

				for (int x = 0; x < GridXSize; x++)
				{
					float zstart = 0 - ((GridZSize * GridDistance) / 2.0f);
					for (int z = 0; z < GridZSize; z++)
					{
						Vector3 pos = new Vector3(transform.position.x + xstart, transform.position.y, transform.position.z + zstart);
						if (GridRandomOffset != 0.0f)
						{
							pos.x = pos.x + Random.Range(-GridRandomOffset, GridRandomOffset);
							pos.z = pos.z + Random.Range(-GridRandomOffset, GridRandomOffset);
						}

						GridPositions.Add(pos);
						zstart += GridDistance;
					}
					xstart += GridDistance;
				}

				return GridPositions;
			}

			private DynamicCharacterAvatar GenerateCharacter(Vector3 Pos, Quaternion Rot, string Name)
			{
				if (Prefab == null) return null;

				GameObject newDCA = GameObject.Instantiate(Prefab, Pos, Rot);

				// Parent Newly Instantiated GO
				if (ParentObject != null) newDCA.transform.parent = ParentObject.transform;

				// Keep Generated DCA in memory
				DynamicCharacterAvatar RandomAvatar = newDCA.GetComponent<DynamicCharacterAvatar>();
				newDCA.name = Name;
				generatedDCAs.Add(RandomAvatar);

				// Event for possible networking here
				RandomAvatarGenerated?.Invoke(transform.gameObject, newDCA);

				return RandomAvatar;
			}

			private Quaternion RandRotation(Quaternion src)
			{
				Vector3 Euler = src.eulerAngles;
				return Quaternion.Euler(Euler.x, Random.Range(0.0f, 359.9f), Euler.z);
			}

		}

		// --- Existing Character Randomization ---
		public List<DynamicCharacterAvatar> ExistingDCAs = new List<DynamicCharacterAvatar>();



		// Use this for initialization
		void Start()
		{
			switch (mode)
			{
				case Mode.Generate:
					Generation.Init(this.gameObject);
					Generation.Start(Randomize);
					break;
				case Mode.UseExisting:
					foreach (DynamicCharacterAvatar DCA in ExistingDCAs)
						DCA?.CharacterCreated.AddListener(RandomizeWhenLoaded);
					break;

				default:
					Debug.LogError($"Mode {mode} not recognized");
					break;
			}
		}

		private void OnDestroy()
		{
			foreach (DynamicCharacterAvatar DCA in ExistingDCAs)
				DCA?.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
		}

		private void RandomizeWhenLoaded(UMAData uMAData)
		{
			DynamicCharacterAvatar dynamicCharacterAvatar = uMAData.GetComponent<DynamicCharacterAvatar>();
			if (dynamicCharacterAvatar == null) return;

			Randomize(dynamicCharacterAvatar);
		}

		public void RandomizeAll(bool randChar = true, bool randWardrobe = true)
		{
			switch (mode)
			{
				case Mode.Generate:
					Generation.RandomizeAll(Randomize, randChar, randWardrobe);
					break;
				case Mode.UseExisting:
					RandomizeAllExisting(randChar, randWardrobe);
					break;
			}
		}

		private void RandomizeAllExisting(bool randChar = true, bool randWardrobe = true)
		{
			if (ExistingDCAs == null || ExistingDCAs.Count == 0) return;

			foreach (DynamicCharacterAvatar DCA in ExistingDCAs)
				Randomize(DCA, randChar, randWardrobe);
		}



		public void Randomize(DynamicCharacterAvatar Avatar, bool randChar = true, bool randWardrobe = true)
		{
			if (Avatar == null) return;

			if (!KeepExistingWardrobe) Avatar.WardrobeRecipes.Clear();

			if (randChar) RandomizeCharacter(Avatar);

			if (randWardrobe) RandomizeWardrobe(Avatar);

			Avatar.BuildCharacter(!Avatar.BundleCheck);
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (Generation.ShowPlaceholder)
			{
				Gizmos.DrawCube(transform.position, Vector3.one);
			}
		}
#endif


		private void RandomizeCharacter(DynamicCharacterAvatar Avatar)
		{
			UMARandomizer Randomizer = GetRandomizer(CharacterRandomizers);
			if (Randomizer == null) return;

			RandomAvatar ra = default;

			// Handle Race Selection
			if (KeepExistingRace)
			{
				ra = Randomizer.GetRandomAvatar(Avatar.activeRace.name);
				if (RaiseNoRandomizerForRace(Avatar, Randomizer, ra)) return;
			}
			else
			{
				ra = Randomizer.GetRandomAvatar();
				Avatar.ChangeRaceData(ra.RaceName);
			}

			var RandomDNA = ra.GetRandomDNA();
			Avatar.predefinedDNA = RandomDNA;

			// Global Colors
			if (Randomizer.useGlobalColors)
				RandomizeSharedColor(Avatar, Randomizer.Global.SharedColors);
			// Colors defined per Random Avatars
			RandomizeSharedColor(Avatar, ra.SharedColors);

			// Randomize Slots for Hair, Faces, etc..
			var RandomSlots = ra.GetRandomSlots();
			AssigRandomSlots(Avatar, RandomSlots);
		}

		private void RandomizeWardrobe(DynamicCharacterAvatar Avatar)
		{
			UMARandomizer Randomizer = GetRandomizer(WardrobeRandomizers);

			if (Randomizer == null) return;

			RandomAvatar ra = Randomizer.GetRandomAvatar(Avatar.activeRace.name);

			if (RaiseNoRandomizerForRace(Avatar, Randomizer, ra)) return;

			var RandomSlots = ra.GetRandomSlots();

			// Global Colors
			RandomizeSharedColor(Avatar, Randomizer.Global.SharedColors);
			// Colors defined per Random Avatars
			RandomizeSharedColor(Avatar, ra.SharedColors);

			AssigRandomSlots(Avatar, RandomSlots);
		}

		private RandomWardrobeSlot GetRandomWardrobe(List<RandomWardrobeSlot> wardrobeSlots)
		{
			int total = 0;

			foreach (RandomWardrobeSlot rws in wardrobeSlots)
				total += rws.Chance;

			foreach (RandomWardrobeSlot rws in wardrobeSlots)
			{
				if (UnityEngine.Random.Range(0, total) < rws.Chance)
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

		private void AssigRandomSlots(DynamicCharacterAvatar Avatar, Dictionary<string, List<RandomWardrobeSlot>> RandomSlots)
		{
			foreach (string s in RandomSlots.Keys)
			{
				List<RandomWardrobeSlot> RandomWardrobe = RandomSlots[s];
				RandomWardrobeSlot uwr = GetRandomWardrobe(RandomWardrobe);
				if (uwr.WardrobeSlot != null)
				{
					Avatar.SetSlot(uwr.WardrobeSlot);
					RandomizeSharedColorFromSlot(Avatar, uwr);
				}
				else
				{
					Avatar.ClearSlot(uwr.SlotName);
				}
			}
		}

		private void RandomizeSharedColor(DynamicCharacterAvatar Avatar, List<RandomColors> randomColors)
		{
			if (randomColors != null && randomColors.Count > 0)
			{
				foreach (RandomColors rc in randomColors)
				{
					if (rc.ColorTable != null)
					{
						Avatar.SetColor(rc.ColorName, GetRandomColor(rc), false);
					}
				}
			}
		}

		private void RandomizeSharedColorFromSlot(DynamicCharacterAvatar Avatar, RandomWardrobeSlot uwr)
		{
			if (uwr.Colors != null)
			{
				foreach (RandomColors rc in uwr.Colors)
				{
					if (rc.ColorTable != null)
					{
						OverlayColorData ocd = GetRandomColor(rc);
						Avatar.SetColor(rc.ColorName, ocd, false);
					}
				}
			}
		}

		private UMARandomizer GetRandomizer(List<UMARandomizer> Randomizers)
		{
			if (Randomizers == null) return null;

			if (Randomizers.Count == 0)
				return null;

			if (Randomizers.Count == 1)
				return Randomizers[0];
			else
			{
				return Randomizers[UnityEngine.Random.Range(0, WardrobeRandomizers.Count)];
			}

		}

		private static bool RaiseNoRandomizerForRace(DynamicCharacterAvatar Avatar, UMARandomizer Randomizer, RandomAvatar ra)
		{
			if (ra == null)
			{
				Debug.LogWarning($"No randomization settings for {Avatar.activeRace.name} in {Randomizer.name}. Add settings for selected Race or UnCheck \"Keep Race\"");
				return true;
			}
			return false;
		}

	}
}