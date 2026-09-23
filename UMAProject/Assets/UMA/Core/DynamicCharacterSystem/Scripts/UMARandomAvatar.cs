using System.Collections.Generic;
using UMA.CharacterSystem;
using System.Collections;
using UnityEngine;

namespace UMA
{
	public class UMARandomAvatar : MonoBehaviour
	{
		public UMAGenerationDiagnostics GenerationTimings { get; } = new UMAGenerationDiagnostics();
		// Keep this serialized field name for existing scenes and prefabs.
        public List<UMARandomizer> Randomizers = new List<UMARandomizer>();
        public List<UMARandomizer> CharacterRandomizers { get => Randomizers; set => Randomizers = value; }
        [Tooltip("Optional clothing pass, applied after character traits. If empty, wardrobe-only rerolls use Character Randomizers for the current race.")]
        public List<UMARandomizer> WardrobeRandomizers = new List<UMARandomizer>();
        public enum Mode { Generate, UseExisting }
        public Mode mode;
        public bool RandomizeOnStart = true;
        public bool KeepExistingRace;
        [Tooltip("Keep existing items before applying selected slots. Selected slots can still replace items.")]
        public bool KeepExistingWardrobe;
        public List<DynamicCharacterAvatar> ExistingDCAs = new List<DynamicCharacterAvatar>();
        private readonly HashSet<DynamicCharacterAvatar> waitingForAvatars = new HashSet<DynamicCharacterAvatar>();
        private bool started;
        private readonly HashSet<DynamicCharacterAvatar> initializedExistingAvatars = new HashSet<DynamicCharacterAvatar>();

        public void ToggleKeepExistingWardrobe(bool value) => KeepExistingWardrobe = value;
        public void RandomizeButton() => RandomizeAll(true, true);
        public void RandomizeCharacterButton() => RandomizeAll(true, false);
        public void RandomizeWardrobeButton() => RandomizeAll(false, true);

        public void RandomizeAll(bool randChar = true, bool randWardrobe = true)
        {
            if (!randChar && !randWardrobe) return;
            CancelSpawning();
            ClearCharacterSetupPool();
            var targets = new HashSet<DynamicCharacterAvatar>();
            if (mode == Mode.UseExisting)
            {
                if (ExistingDCAs != null)
                    foreach (var avatar in ExistingDCAs) if (avatar != null) targets.Add(avatar);
            }
            else
            {
                RemoveDestroyedCharacterReferences();
                foreach (var go in generatedCharacters)
                    if (go.TryGetComponent<DynamicCharacterAvatar>(out var avatar)) targets.Add(avatar);
            }
            foreach (var avatar in targets)
            {
                avatar.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
                waitingForAvatars.Remove(avatar);
                initializedExistingAvatars.Add(avatar);
                RandomizeAndBuild(avatar, randChar, randWardrobe);
            }
        }

        public void RandomizeAndBuild(DynamicCharacterAvatar avatar, bool randChar = true, bool randWardrobe = true)
        {
            if (avatar == null || (!randChar && !randWardrobe)) return;
            selectedNPCSetup = null;
            bool changed;
            using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.Randomization))
            {
                if (mode == Mode.Generate && randChar && randWardrobe && !KeepExistingRace && !KeepExistingWardrobe)
                {
                    Randomize(avatar);
                    changed = true;
                }
                else changed = RandomizeAvatarSetup(avatar, Randomizers, WardrobeRandomizers, KeepExistingRace,
                    KeepExistingWardrobe, randChar, randWardrobe);
            }
            if (!changed) return;
            using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.AnimatorSetup))
                avatar.SetAnimatorController(true);
            BuildRandomizedAvatar(avatar, !avatar.BundleCheck);
        }

        private void InitializeExistingAvatars()
        {
            if (ExistingDCAs == null) return;
            foreach (var avatar in ExistingDCAs)
            {
                if (avatar == null || initializedExistingAvatars.Contains(avatar) || !waitingForAvatars.Add(avatar)) continue;
                if (avatar.umaData != null && avatar.umaData.GetRenderers() != null && avatar.umaData.GetRenderers().Length > 0)
                    RandomizeWhenLoaded(avatar.umaData);
                else avatar.CharacterCreated.AddListener(RandomizeWhenLoaded);
            }
        }

        private void RandomizeWhenLoaded(UMAData data)
        {
            var avatar = data != null ? data.GetComponent<DynamicCharacterAvatar>() : null;
            if (avatar == null || !waitingForAvatars.Remove(avatar)) return;
            initializedExistingAvatars.Add(avatar);
            // Unsubscribe BEFORE rebuilding: CharacterCreated can fire again.
            avatar.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
            RandomizeAndBuild(avatar);
        }

        private void StopWaitingForAvatars()
        {
            foreach (var avatar in waitingForAvatars)
                if (avatar != null) avatar.CharacterCreated.RemoveListener(RandomizeWhenLoaded);
            waitingForAvatars.Clear();
        }

		public GameObject prefab;
		public GameObject ParentObject;
		public bool ShowPlaceholder;
		public bool GenerateGrid;
		[Min(0)]
		[Tooltip("Maximum randomly generated character setups. After this many, new characters randomly reuse a setup (race, DNA, wardrobe and colors). 0 randomizes every character independently. Enable Cache and Reuse on the character to share generated resources.")]
		public int MaximumUniqueCharacters = 0;
        [Tooltip("Build each pooled appearance once, then instantiate its completed rig and shared outputs. Requires Maximum Unique Characters above zero. Unsupported custom builds fall back to BuildCharacter.")]
        public bool UseNPCBuilds;
        [Min(0)]
        [Tooltip("CPU budget for instantiating and enqueueing characters each frame. 0 preserves synchronous generation. A positive value spreads spawning across frames; one character is always allowed. This is separate from the UMA generator build budget.")]
        public float SpawnBudgetMilliseconds;
        public bool IsGenerating => spawnSequence != null;
        private IEnumerator spawnSequence;
        private Random.State spawnRandomState;
		public int GridXSize = 5;
		public int GridZSize = 4;
		public float GridDistance = 1.5f;
		public float RandomOffset = 0.0f;
		public bool RandomRotation;
		public string NameBase = "Pat";
		public UMARandomAvatarEvent RandomAvatarGenerated;

		private DynamicCharacterAvatar RandomAvatar;
		private readonly List<GameObject> generatedCharacters = new List<GameObject>();
		private readonly List<CharacterSetup> characterSetups = new List<CharacterSetup>();
        private CharacterSetup selectedNPCSetup;
		private bool initialRandomStateCaptured;
		private Random.State initialRandomState;

		/// <summary>Number of saved random setups, not live avatars or cached meshes.</summary>
		public int UniqueCharacterSetupCount => characterSetups.Count;

		/// <summary>Forget the setup pool without changing characters already generated.</summary>
		[ContextMenu("Clear Character Setup Pool")]
        public void ClearCharacterSetupPool()
        {
            foreach (var setup in characterSetups) setup.NPC?.Dispose();
            characterSetups.Clear(); selectedNPCSetup = null;
        }
        private void OnDestroy() { StopWaitingForAvatars(); CancelSpawning(); ClearCharacterSetupPool(); }

		public int GeneratedCharacterCount
		{
			get
			{
				RemoveDestroyedCharacterReferences();
				return generatedCharacters.Count;
			}
		}

		// Use this for initialization
		void Start()
        {
            started = true;
            if (!RandomizeOnStart) return;
            if (mode == Mode.UseExisting) InitializeExistingAvatars();
            else GenerateCharacters(false);
        }

        private void OnEnable()
        {
            if (started && RandomizeOnStart && mode == Mode.UseExisting) InitializeExistingAvatars();
        }

		/// <summary>
		/// Generates the configured avatar or avatar grid. Passing true restores
		/// the random state captured before the first run so profiling restarts
		/// generate the same crowd.
		/// </summary>
		public void GenerateCharacters(bool repeatInitialRandomSequence)
		{
            if (mode != Mode.Generate) return;
            CancelSpawning();
            var callerRandomState = Random.state;
			// Recreate both the pool and its random selections for OFF/ON comparisons.
			if (repeatInitialRandomSequence) ClearCharacterSetupPool();
			if (ParentObject == null)
			{
				ParentObject = this.gameObject;
			}

			if (!initialRandomStateCaptured)
			{
				initialRandomState = Random.state;
				initialRandomStateCaptured = true;
			}
			else if (repeatInitialRandomSequence)
			{
				Random.state = initialRandomState;
			}

            spawnRandomState = Random.state;
            spawnSequence = GenerateSequence();
            if (Application.isPlaying && isActiveAndEnabled && SpawnBudgetMilliseconds > 0)
            {
                Random.state = callerRandomState;
                AdvanceSpawning(true);
            }
            else AdvanceSpawning(false);
        }

        private void Update()
        {
            if (IsGenerating) AdvanceSpawning(true);
        }

        private void OnDisable() { StopWaitingForAvatars(); CancelSpawning(); }

        public void CancelSpawning()
        {
            (spawnSequence as System.IDisposable)?.Dispose();
            spawnSequence = null;
        }

        private void AdvanceSpawning(bool sliced)
        {
            var callerState = Random.state;
            if (sliced) Random.state = spawnRandomState;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            using var batchTiming = GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.SpawnBatch);
            var sequence = spawnSequence;
            try
            {
                while (sequence != null && ReferenceEquals(sequence, spawnSequence))
                {
                    if (!sequence.MoveNext()) { CancelSpawning(); break; }
                    if (sliced && SpawnBudgetMilliseconds > 0 &&
                        UMATime.StopwatchTicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - start) >= SpawnBudgetMilliseconds)
                        break;
                }
            }
            catch { CancelSpawning(); throw; }
            finally
            {
                if (sliced) { spawnRandomState = Random.state; Random.state = callerState; }
            }
        }

        private IEnumerator GenerateSequence()
        {
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
                        if (x < GridXSize - 1 || z < GridZSize - 1) yield return null;
						zstart += GridDistance;
					}
					xstart += GridDistance;
				}
			}
		}

		/// <summary>
		/// Destroys avatars created by this crowd controller and returns the
		/// number scheduled for destruction.
		/// </summary>
		public int DestroyGeneratedCharacters()
		{
            CancelSpawning();
			RemoveDestroyedCharacterReferences();
			int destroyed = generatedCharacters.Count;
			for (int i = 0; i < generatedCharacters.Count; i++)
			{
				GameObject generatedCharacter = generatedCharacters[i];
				if (generatedCharacter != null)
				{
					if (Application.isPlaying) Destroy(generatedCharacter);
                    else DestroyImmediate(generatedCharacter);
				}
			}
			generatedCharacters.Clear();
			ClearCharacterSetupPool();
			RandomAvatar = null;
			return destroyed;
		}

		private void RemoveDestroyedCharacterReferences()
		{
			for (int i = generatedCharacters.Count - 1; i >= 0; i--)
			{
				if (generatedCharacters[i] == null)
				{
					generatedCharacters.RemoveAt(i);
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
			if (prefab == null)
			{
				Debug.LogError("UMARandomAvatar requires a character prefab before it can generate avatars.", this);
				return;
			}

			GameObject go;
			using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.Instantiate))
				go = GameObject.Instantiate(prefab, Pos, Rot);
			using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.SpawnCallbacksAndSetup))
			{
				RandomAvatar = go.GetComponent<DynamicCharacterAvatar>();
				if (RandomAvatar == null)
				{
					Debug.LogError("UMARandomAvatar prefab '" + prefab.name +
						"' does not contain a DynamicCharacterAvatar component.", prefab);
					Destroy(go);
					return;
				}

				if (ParentObject != null)
				{
					go.transform.parent = ParentObject.transform;
				}
				generatedCharacters.Add(go);
				go.name = Name;
				// Event for possible networking here
				if (RandomAvatarGenerated != null)
				{
					RandomAvatarGenerated.Invoke(gameObject, go);
				}
			}
			// This generator owns the initial build, so batch all randomization
			// changes and perform the same animator setup that the normal DCA
			// startup path performs. Relying on LoadCharacter to do this only
			// works for some restore-DNA and addressables paths, leaving newly
			// generated avatars without an Animator/controller in the others.
			RandomAvatar.BuildCharacterEnabled = false;
			using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.Randomization))
				Randomize(RandomAvatar);
			using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.AnimatorSetup))
				RandomAvatar.SetAnimatorController(true);
            BuildRandomizedAvatar(RandomAvatar, true);
        }

        private void BuildRandomizedAvatar(DynamicCharacterAvatar avatar, bool restoreDNA)
        {
            using (GenerationTimings.Measure(UMAGenerationDiagnostics.Stage.RecipeAndEnqueue))
            {
                if ((UseNPCBuilds || avatar.useNPCBuilds) && selectedNPCSetup != null)
                {
                    if (selectedNPCSetup.NPC != null && selectedNPCSetup.NPC.IsInvalidated)
                    {
                        selectedNPCSetup.NPC.Dispose(); selectedNPCSetup.NPC = null;
                    }
                    var handle = avatar.BuildNPC(selectedNPCSetup.NPC);
                    if (selectedNPCSetup.NPC == null && handle != null) selectedNPCSetup.NPC = handle.Retain();
                }
                else avatar.BuildCharacter(restoreDNA);
            }
        }

        public RandomWardrobeSlot GetRandomWardrobe(List<RandomWardrobeSlot> wardrobeSlots) => SelectWardrobe(wardrobeSlots);

        internal static RandomWardrobeSlot SelectWardrobe(List<RandomWardrobeSlot> slots)
        {
            if (slots == null) return null;
            double total = 0;
            foreach (var slot in slots) if (slot != null) total += Mathf.Max(0, slot.Chance);
            if (total <= 0) return null;
            double roll = Random.value * total;
            RandomWardrobeSlot last = null;
            foreach (var slot in slots)
            {
                if (slot == null || slot.Chance <= 0) continue;
                last = slot;
                roll -= slot.Chance;
                if (roll < 0) return slot;
            }
            return last;
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


        /// <summary>Change selected appearance data without building or using the complete-setup pool.</summary>
        public void Randomize(DynamicCharacterAvatar avatar, bool randChar, bool randWardrobe) =>
            RandomizeAvatarSetup(avatar, Randomizers, WardrobeRandomizers, KeepExistingRace, KeepExistingWardrobe, randChar, randWardrobe);

		public void Randomize(DynamicCharacterAvatar Avatar)
		{
            selectedNPCSetup = null;
			if (Avatar == null)
			{
				Debug.LogError("UMARandomAvatar cannot randomize a null DynamicCharacterAvatar.", this);
				return;
			}

			// Preserved avatar-specific state must never be overwritten by another avatar's pooled setup.
            int limit = mode == Mode.UseExisting || KeepExistingRace || KeepExistingWardrobe ? 0 : Mathf.Max(0, MaximumUniqueCharacters);
			if (characterSetups.Count > limit)
            {
                for (int i = limit; i < characterSetups.Count; i++) characterSetups[i].NPC?.Dispose();
                characterSetups.RemoveRange(limit, characterSetups.Count - limit);
            }
			if (limit > 0 && characterSetups.Count == limit)
			{
                selectedNPCSetup = characterSetups[Random.Range(0, characterSetups.Count)];
                selectedNPCSetup.Apply(Avatar);
				return;
			}
			if (RandomizeNewSetup(Avatar) && limit > 0)
            {
                selectedNPCSetup = new CharacterSetup(Avatar);
                characterSetups.Add(selectedNPCSetup);
            }
		}

        /// <summary>Randomize independent appearance categories without building or using pooled setups.</summary>
        public bool RandomizeSelective(DynamicCharacterAvatar avatar, bool race, bool dna, bool clothing, bool colors)
        {
            if (avatar == null || (!race && !dna && !clothing && !colors)) return false;
            string previousRace = avatar.activeRace?.name ?? string.Empty;
            var character = SelectDefinition(Randomizers, race ? null : previousRace, out var characterSource);
            string targetRace = character != null && race ? character.RaceName : previousRace;
            RandomAvatar wardrobe = null;
            UMARandomizer wardrobeSource = null;
            if (clothing || colors) wardrobe = SelectDefinition(WardrobeRandomizers, targetRace, out wardrobeSource);
            if (character == null && wardrobe == null) return false;
            if (character == null && !clothing && !colors) return false;

            Dictionary<string, float> preservedDNA = null;
            if (race && !dna && avatar.activeRace.data != null)
            {
                preservedDNA = new Dictionary<string, float>();
                foreach (var value in avatar.GetDNA()) preservedDNA[value.Key] = value.Value.Value;
            }
            bool buildEnabled = avatar.BuildCharacterEnabled;
            avatar.BuildCharacterEnabled = false;
            try
            {
                if (race && character != null) avatar.ChangeRaceData(character.RaceName);
                var randomizedDNA = dna && character != null ? character.GetRandomDNA() : null;
                bool changedRace = previousRace != avatar.activeRace.name;
                if (avatar.activeRace.data != null && avatar.activeRace.data.useNewDNA && (dna || changedRace))
                {
                    avatar.umaRecipe.raceData = avatar.activeRace.data;
                    if (changedRace || avatar.dnaInstanceCollection == null || avatar.dnaInstanceCollection.dnaInstances == null || avatar.dnaInstanceCollection.dnaInstances.Count == 0)
                        avatar.umaRecipe.InitializeDNA();
                    else avatar.umaRecipe.AddMissingDNAForRace();
                    var setters = avatar.GetDNA();
                    if (dna && character != null)
                    {
                        foreach (var value in randomizedDNA.PreloadValues)
                            if (setters.TryGetValue(value.Name, out var setter)) setter.Set(value.Value);
                    }
                    else if (preservedDNA != null)
                        foreach (var value in preservedDNA)
                            if (setters.TryGetValue(value.Key, out var setter)) setter.Set(value.Value);
                    // UMA 3 stores these values in the live collection, never in legacy preload DNA.
                    avatar.predefinedDNA?.Clear();
                }
                else if (randomizedDNA != null)
                {
                    avatar.predefinedDNA = randomizedDNA;
                }
                else if (changedRace && !dna && preservedDNA != null)
                {
                    avatar.predefinedDNA ??= new UMAPredefinedDNA();
                    foreach (var value in preservedDNA) avatar.predefinedDNA.AddDNA(value.Key, value.Value);
                }
                if (clothing && !KeepExistingWardrobe)
                {
                    avatar.ClearSlots();
                    avatar.WardrobeCollections.Clear();
                }
                if (colors && character != null) ApplyRandomColors(avatar, characterSource, character);
                if (clothing && character != null) ApplyRandomSlots(avatar, character, colors);
                if (colors && wardrobe != null) ApplyRandomColors(avatar, wardrobeSource, wardrobe);
                if (clothing && wardrobe != null) ApplyRandomSlots(avatar, wardrobe, colors);
                if (colors && !clothing)
                {
                    ApplyEquippedSlotColors(avatar, character);
                    ApplyEquippedSlotColors(avatar, wardrobe);
                }
                return true;
            }
            finally { avatar.BuildCharacterEnabled = buildEnabled; }
        }

        private static void ApplyEquippedSlotColors(DynamicCharacterAvatar avatar, RandomAvatar definition)
        {
            if (definition?.RandomWardrobeSlots == null) return;
            foreach (var slot in definition.RandomWardrobeSlots)
            {
                if (slot?.WardrobeSlot == null) continue;
                bool equipped = avatar.WardrobeRecipes.TryGetValue(slot.SlotName, out var recipe) && recipe == slot.WardrobeSlot;
                if (!equipped && avatar.AdditiveRecipes.TryGetValue(slot.SlotName, out var appended))
                    equipped = appended != null && appended.Contains(slot.WardrobeSlot);
                if (equipped) ApplyRandomColors(avatar, slot.Colors);
            }
        }

        private bool RandomizeNewSetup(DynamicCharacterAvatar avatar) =>
            RandomizeAvatarSetup(avatar, Randomizers, WardrobeRandomizers, KeepExistingRace, KeepExistingWardrobe, true, true);

        // Shared with the legacy V2 component so old scenes use the same fixes.
        internal static bool RandomizeAvatarSetup(DynamicCharacterAvatar avatar,
            List<UMARandomizer> characters, List<UMARandomizer> wardrobe,
            bool keepRace, bool keepWardrobe, bool randChar, bool randWardrobe)
        {
            if (avatar == null || (!randChar && !randWardrobe)) return false;
            string race = avatar.activeRace?.name ?? string.Empty;
            UMARandomizer characterSource = null, wardrobeSource = null;
            RandomAvatar character = null, clothing = null;
            if (randChar)
                character = SelectDefinition(characters, keepRace ? race : null, out characterSource);
            if (character != null) race = character.RaceName;
            if (randWardrobe)
            {
                if (HasRandomizer(wardrobe)) clothing = SelectDefinition(wardrobe, race, out wardrobeSource);
                else if (!randChar) clothing = SelectDefinition(characters, race, out wardrobeSource);
            }
            if (character == null && clothing == null) return false;
            bool buildEnabled = avatar.BuildCharacterEnabled;
            avatar.BuildCharacterEnabled = false;
            try
            {
                // Partial wardrobe rerolls replace only selected regions, preserving hair/body slots.
                if (randChar && randWardrobe && character != null && !keepWardrobe)
                {
                    avatar.ClearSlots();
                    avatar.WardrobeCollections.Clear();
                }
                if (character != null)
                {
                    if (!keepRace) avatar.ChangeRaceData(character.RaceName);
                    avatar.predefinedDNA = character.GetRandomDNA();
                    ApplyRandomColors(avatar, characterSource, character);
                    ApplyRandomSlots(avatar, character);
                }
                if (clothing != null)
                {
                    ApplyRandomColors(avatar, wardrobeSource, clothing);
                    ApplyRandomSlots(avatar, clothing);
                }
                return true;
            }
            finally { avatar.BuildCharacterEnabled = buildEnabled; }
        }

        private static bool HasRandomizer(List<UMARandomizer> sources) =>
            sources != null && sources.Exists(source => source != null);

        private static RandomAvatar SelectDefinition(List<UMARandomizer> sources, string race, out UMARandomizer source)
        {
            source = null;
            if (sources == null) return null;
            var compatible = new List<UMARandomizer>();
            foreach (var candidate in sources)
                if (candidate != null && candidate.RandomAvatars != null &&
                    candidate.RandomAvatars.Exists(entry => entry != null && entry.Chance > 0 &&
                        (race == null || entry.RaceName == race))) compatible.Add(candidate);
            if (compatible.Count == 0) return null;
            source = compatible.Count == 1 ? compatible[0] : compatible[Random.Range(0, compatible.Count)];
            return race == null ? source.GetRandomAvatar() : source.GetRandomAvatar(race);
        }

        private static void ApplyRandomSlots(DynamicCharacterAvatar avatar, RandomAvatar definition, bool applyColors = true)
        {
            foreach (var pair in definition.GetRandomSlots())
            {
                var slot = SelectWardrobe(pair.Value);
                if (slot == null) continue;
                // Replace additive items in selected regions too, rather than accumulating duplicates.
                avatar.ClearSlot(pair.Key);
                if (slot.WardrobeSlot == null) continue;
                avatar.SetSlot(slot.WardrobeSlot);
                if (applyColors) ApplyRandomColors(avatar, slot.Colors);
            }
        }

        private static void ApplyRandomColors(DynamicCharacterAvatar avatar, UMARandomizer source, RandomAvatar definition)
        {
            if (source.useGlobalColors) ApplyRandomColors(avatar, source.Global.SharedColors);
            ApplyRandomColors(avatar, definition.SharedColors);
        }

        private static void ApplyRandomColors(DynamicCharacterAvatar avatar, List<RandomColors> colors)
        {
            if (colors == null) return;
            foreach (var entry in colors)
            {
                if (entry == null || entry.ColorTable == null || entry.ColorTable.colors == null) continue;
                var choices = new List<OverlayColorData>();
                foreach (var color in entry.ColorTable.colors) if (color != null) choices.Add(color);
                if (choices.Count > 0) avatar.SetRawColor(entry.ColorName, choices[Random.Range(0, choices.Count)], false);
            }
        }

		// Immutable setup snapshots, not live avatars or generated resources. In particular,
		// BuildCharacter consumes predefinedDNA, so never retain its mutable list directly.
		private sealed class CharacterSetup
		{
            internal UMANPCBuildHandle NPC;
			private readonly string race;
			private readonly UMAPredefinedDNA dna;
			private readonly Dictionary<string, UMATextRecipe> wardrobe;
			private readonly Dictionary<string, List<UMATextRecipe>> additive;
			private readonly Dictionary<string, UMAWardrobeCollection> collections;
			private readonly DynamicCharacterAvatar.ColorValueList colors;

			internal CharacterSetup(DynamicCharacterAvatar avatar)
			{
				race = avatar.RacePreset;
				dna = avatar.predefinedDNA?.Clone();
				wardrobe = new Dictionary<string, UMATextRecipe>(avatar.WardrobeRecipes);
				additive = CopyAdditive(avatar.AdditiveRecipes);
				collections = new Dictionary<string, UMAWardrobeCollection>(avatar.WardrobeCollections);
				colors = CopyColors(avatar.characterColors);
			}

			internal void Apply(DynamicCharacterAvatar avatar)
			{
				avatar.ChangeRaceData(race);
				avatar.predefinedDNA = dna?.Clone();
				avatar.WardrobeRecipes.Clear();
				foreach (var pair in wardrobe) avatar.WardrobeRecipes.Add(pair.Key, pair.Value);
				avatar.AdditiveRecipes.Clear();
				foreach (var pair in additive)
					avatar.AdditiveRecipes.Add(pair.Key, pair.Value == null ? null : new List<UMATextRecipe>(pair.Value));
				avatar.WardrobeCollections.Clear();
				foreach (var pair in collections) avatar.WardrobeCollections.Add(pair.Key, pair.Value);
				avatar.characterColors = CopyColors(colors);
			}

			private static Dictionary<string, List<UMATextRecipe>> CopyAdditive(Dictionary<string, List<UMATextRecipe>> source)
			{
				var result = new Dictionary<string, List<UMATextRecipe>>();
				foreach (var pair in source)
					result.Add(pair.Key, pair.Value == null ? null : new List<UMATextRecipe>(pair.Value));
				return result;
			}

			private static DynamicCharacterAvatar.ColorValueList CopyColors(DynamicCharacterAvatar.ColorValueList source)
			{
				var result = new DynamicCharacterAvatar.ColorValueList();
				if (source?.Colors != null)
					foreach (var color in source.Colors)
						result.Colors.Add(color == null ? null : new DynamicCharacterAvatar.ColorValue(color));
				return result;
			}
		}
	}
}
