using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace UMA
{
	public class UMARandomAvatarV2 : MonoBehaviour
	{
		// Kept so scenes and UnityEvents referencing V2 continue to load after migration.
        [HideInInspector] public UMARandomAvatar UnifiedController;
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

		public void ToggleKeepExistingWardrobe(bool val) { KeepExistingWardrobe = val; if (UnifiedController != null) UnifiedController.ToggleKeepExistingWardrobe(val); }

		public enum Mode { Generate, UseExisting }
		public Mode mode;

		public CharacterGeneration Generation = new CharacterGeneration();
		// --- Character Generation ---
		[System.Serializable]
		public class CharacterGeneration
		{
			public UMAGenerationDiagnostics Timings { get; } = new UMAGenerationDiagnostics();
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

			public int GeneratedCharacterCount =>
				generatedDCAs != null ? generatedDCAs.Count : 0;

			public void Init(GameObject componentGO)
			{
				if (ParentObject == null)
					ParentObject = componentGO;

				transform = componentGO.transform;

				// Retain ownership when GenerateCharacters is called more than once.
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

			public int DestroyGeneratedCharacters()
			{
				if (generatedDCAs == null)
				return 0;

				int destroyedCount = 0;
				for (int i = generatedDCAs.Count - 1; i >= 0; i--)
				{
					DynamicCharacterAvatar avatar = generatedDCAs[i];
					if (avatar == null)
						continue;

					destroyedCount++;
					if (Application.isPlaying)
						GameObject.Destroy(avatar.gameObject);
					else
						GameObject.DestroyImmediate(avatar.gameObject);
				}
				generatedDCAs.Clear();
				return destroyedCount;
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
				long card = (long)GridXSize * GridZSize;
				if (card > 1000)
				{
					Debug.LogWarning($"Random Character Generation Aborted : Too much Characters {card}.\nReduce the Grid Size (X or Z) to reduce the number of characters to generate.");
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

				GameObject newDCA;
				using (Timings.Measure(UMAGenerationDiagnostics.Stage.Instantiate))
					newDCA = GameObject.Instantiate(Prefab, Pos, Rot);
				using var setupTiming = Timings.Measure(UMAGenerationDiagnostics.Stage.SpawnCallbacksAndSetup);

				// Parent Newly Instantiated GO
				if (ParentObject != null) newDCA.transform.parent = ParentObject.transform;

				// Keep Generated DCA in memory
				DynamicCharacterAvatar RandomAvatar = newDCA.GetComponent<DynamicCharacterAvatar>();
				newDCA.name = Name;
				if (RandomAvatar == null)
                {
                    Debug.LogError("The character prefab must contain a DynamicCharacterAvatar.", Prefab);
                    if (Application.isPlaying) GameObject.Destroy(newDCA);
                    else GameObject.DestroyImmediate(newDCA);
                    return null;
                }
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

		private Random.State initialGenerationRandomState;
		private bool hasInitialGenerationRandomState;


		// Use this for initialization
		void Start()
		{
            if (UnifiedController != null) return;
			switch (mode)
			{
				case Mode.Generate:
					initialGenerationRandomState = Random.state;
					hasInitialGenerationRandomState = true;
					GenerateCharacters(false);
					break;
				case Mode.UseExisting:
					foreach (DynamicCharacterAvatar DCA in new HashSet<DynamicCharacterAvatar>(ExistingDCAs ?? new List<DynamicCharacterAvatar>()))
						if (DCA != null)
                        {
                            DCA.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
                            if (DCA.umaData != null && DCA.umaData.GetRenderers() != null && DCA.umaData.GetRenderers().Length > 0)
                                Randomize(DCA);
                            else DCA.CharacterCreated.AddListener(RandomizeWhenLoaded);
                        }
					break;

				default:
					Debug.LogError($"Mode {mode} not recognized");
					break;
			}
		}

		/// <summary>
		/// Generates the configured character set. Reusing the initial random
		/// state makes a restarted performance run use the same workload.
		/// </summary>
		public void GenerateCharacters(bool repeatInitialRandomSequence)
		{
            if (UnifiedController != null) { UnifiedController.GenerateCharacters(repeatInitialRandomSequence); return; }
			using var batchTiming = Generation.Timings.Measure(UMAGenerationDiagnostics.Stage.SpawnBatch);
			if (mode != Mode.Generate)
				return;

			Random.State previousState = Random.state;
			if (repeatInitialRandomSequence &&
				hasInitialGenerationRandomState)
			{
				Random.state = initialGenerationRandomState;
			}
			try
			{
				Generation.Init(gameObject);
				Generation.Start(Randomize);
			}
			finally
			{
				if (repeatInitialRandomSequence &&
					hasInitialGenerationRandomState)
				{
					Random.state = previousState;
				}
			}
		}

		public int DestroyGeneratedCharacters()
		{
            if (UnifiedController != null) return UnifiedController.DestroyGeneratedCharacters();
			return mode == Mode.Generate
				? Generation.DestroyGeneratedCharacters()
				: 0;
		}

		private void OnDestroy()
		{
			foreach (DynamicCharacterAvatar DCA in new HashSet<DynamicCharacterAvatar>(ExistingDCAs ?? new List<DynamicCharacterAvatar>()))
				if (DCA != null) DCA.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
		}

		private void RandomizeWhenLoaded(UMAData uMAData)
		{
			DynamicCharacterAvatar dynamicCharacterAvatar = uMAData.GetComponent<DynamicCharacterAvatar>();
			if (dynamicCharacterAvatar == null) return;

			dynamicCharacterAvatar.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
            Randomize(dynamicCharacterAvatar);
		}

		public void RandomizeAll(bool randChar = true, bool randWardrobe = true)
		{
            if (UnifiedController != null) { UnifiedController.RandomizeAll(randChar, randWardrobe); return; }
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

			foreach (DynamicCharacterAvatar DCA in new HashSet<DynamicCharacterAvatar>(ExistingDCAs ?? new List<DynamicCharacterAvatar>()))
				Randomize(DCA, randChar, randWardrobe);
		}



		public void Randomize(DynamicCharacterAvatar Avatar, bool randChar = true, bool randWardrobe = true)
		{
            if (UnifiedController != null) { UnifiedController.RandomizeAndBuild(Avatar, randChar, randWardrobe); return; }
			if (Avatar == null) return;

            bool changed;
            using (Generation.Timings.Measure(UMAGenerationDiagnostics.Stage.Randomization))
                changed = UMARandomAvatar.RandomizeAvatarSetup(Avatar, CharacterRandomizers, WardrobeRandomizers,
                    KeepExistingRace, KeepExistingWardrobe, randChar, randWardrobe);
            if (changed)
            {
                Avatar.SetAnimatorController(true);
                using (Generation.Timings.Measure(UMAGenerationDiagnostics.Stage.RecipeAndEnqueue))
                    Avatar.BuildCharacter(!Avatar.BundleCheck);
            }
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


	}
}
