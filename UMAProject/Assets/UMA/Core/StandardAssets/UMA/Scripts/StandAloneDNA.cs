using System.Collections.Generic;
using UnityEngine;
using static UMA.UMAPackedRecipeBase;

namespace UMA
{
    public class StandAloneDNA : MonoBehaviour
    {
        private List<UMADnaBase> DNA = new List<UMADnaBase>();
        public List<UMAPackedDna> PackedDNA = new List<UMAPackedDna>();
        // The original AvatarDefinition;
        public AvatarDefinition avatarDefinition; 
        public UMAData umaData;
        public RaceData originalRace;
        private Dictionary<string, DnaSetter> dna = new Dictionary<string, DnaSetter>();

        // Start is called before the first frame update
        void Start()
        {
            UMAData.UMARecipe umaRecipe = GetRecipe();
            if (umaData == null || umaRecipe == null)
            {
                return;
            }

            umaData.staticCharacter = true;

            RaceData raceData = GetOriginalRace();
            if (raceData != null)
            {
                umaRecipe.raceData = raceData;
                umaData.SetupSkeleton();
            }

            if (raceData != null && raceData.useNewDNA)
            {
                SetupNewDNA(umaRecipe, raceData);
                return;
            }

            SetupLegacyDNA(umaRecipe, raceData);
        }

        private UMAData.UMARecipe GetRecipe()
        {
            if (umaData == null)
            {
                umaData = GetComponent<UMAData>();
            }

            if (umaData == null)
            {
                return null;
            }

            if (umaData._umaRecipe == null)
            {
                umaData._umaRecipe = new UMAData.UMARecipe();
            }

            return umaData._umaRecipe;
        }

        private RaceData GetOriginalRace()
        {
            if (originalRace != null)
            {
                return originalRace;
            }

            UMAData.UMARecipe umaRecipe = umaData != null ? umaData.umaRecipe : null;
            if (umaRecipe != null && umaRecipe.raceData != null)
            {
                originalRace = umaRecipe.raceData;
                return originalRace;
            }

            if (!string.IsNullOrEmpty(avatarDefinition.RaceName) && UMAAssetIndexer.Instance != null)
            {
                originalRace = UMAAssetIndexer.Instance.GetRace(avatarDefinition.RaceName);
            }

            return originalRace;
        }

        private void SetupLegacyDNA(UMAData.UMARecipe umaRecipe, RaceData raceData)
        {
            if (PackedDNA == null)
            {
                PackedDNA = new List<UMAPackedDna>();
            }

            DNA = raceData != null ? UMAPackedRecipeBase.UnPackDNA(PackedDNA, raceData) : UMAPackedRecipeBase.UnPackDNA(PackedDNA);

            umaRecipe.ClearDna();

            for (int i = 0; i < DNA.Count; i++)
            {
                UMADnaBase umd = DNA[i];
                if (umd == null || umd is UMADnaInstance)
                {
                    continue;
                }

                umaRecipe.AddDna(umd);
            }

            umaRecipe.ClearDNAConverters();
            dna.Clear();
        }

        private void SetupNewDNA(UMAData.UMARecipe umaRecipe, RaceData raceData)
        {
            EnsureNewDNACollection(umaRecipe, raceData);
            ApplyPackedDNAToNewCollection(umaRecipe, raceData);

            dna.Clear();
            if (avatarDefinition.Dna != null && avatarDefinition.Dna.Length > 0)
            {
                LoadDNAFromAvatarDefinition(avatarDefinition);
            }

            umaRecipe.GetDefinedDna();
            umaRecipe.ClearDNAConverters();
            dna.Clear();
        }

        private static void EnsureNewDNACollection(UMAData.UMARecipe umaRecipe, RaceData raceData)
        {
            if (umaRecipe == null || raceData == null || !raceData.useNewDNA)
            {
                return;
            }

            umaRecipe.raceData = raceData;
            if (umaRecipe.dnaInstanceCollection == null || umaRecipe.dnaInstanceCollection.dnaInstances == null || umaRecipe.dnaInstanceCollection.dnaInstances.Count == 0)
            {
                umaRecipe.InitializeDNA();
            }
            else
            {
                umaRecipe.AddMissingDNAForRace();
            }
        }

        private void ApplyPackedDNAToNewCollection(UMAData.UMARecipe umaRecipe, RaceData raceData)
        {
            if (PackedDNA == null || PackedDNA.Count == 0 || umaRecipe.dnaInstanceCollection == null)
            {
                return;
            }

            List<UMADnaBase> packedDna = UMAPackedRecipeBase.UnPackDNA(PackedDNA, raceData);
            for (int i = 0; i < packedDna.Count; i++)
            {
                UMADnaInstance packedInstance = packedDna[i] as UMADnaInstance;
                if (packedInstance == null || packedInstance.DNAInstances == null)
                {
                    continue;
                }

                if (raceData != null && raceData.DNACollection != null)
                {
                    packedInstance.DNAInstances.Initialize(raceData.DNACollection);
                }

                ApplyDNAInstances(umaRecipe.dnaInstanceCollection, packedInstance.DNAInstances);
            }
        }

        private static void ApplyDNAInstances(DNAInstanceCollection targetCollection, DNAInstanceCollection sourceCollection)
        {
            if (targetCollection == null || sourceCollection == null || sourceCollection.dnaInstances == null)
            {
                return;
            }

            if (targetCollection.dnaInstances == null)
            {
                targetCollection.dnaInstances = new List<DNAInstance>();
            }

            Dictionary<string, DNAInstance> targetByName = targetCollection.ToDictionary();
            for (int i = 0; i < sourceCollection.dnaInstances.Count; i++)
            {
                DNAInstance sourceInstance = sourceCollection.dnaInstances[i];
                if (sourceInstance == null || string.IsNullOrEmpty(sourceInstance.Name))
                {
                    continue;
                }

                if (targetByName.TryGetValue(sourceInstance.Name, out DNAInstance targetInstance))
                {
                    targetInstance.Value = sourceInstance.Value;
                    targetInstance.enabled = sourceInstance.enabled;
                }
                else
                {
                    targetCollection.AddDNAInstance(sourceInstance.Clone());
                }
            }
        }

        public void LoadDNAFromAvatarDefinition(AvatarDefinition adf)
        {
            if (adf.Dna == null)
            {
                return;
            }

            var DNA = GetDNA();
            for (int i = 0; i < adf.Dna.Length; i++)
            {
                DnaDef d = adf.Dna[i];
                if (DNA.ContainsKey(d.Name))
                {
                    DNA[d.Name].Set(d.Value);
                }
            }
        }

        public AvatarDefinition SaveDNAToAvatarDefinition()
        {
            var CurrentDNA = GetDNA().Values;

            List<DnaDef> Dna = new List<DnaDef>();
            foreach (DnaSetter d in CurrentDNA)
            {
                DnaDef def = new DnaDef(d.Name, d.Get());
                Dna.Add(def);
            }
            avatarDefinition.Dna = Dna.ToArray();

            RaceData raceData = GetOriginalRace();
            if (raceData != null)
            {
                avatarDefinition.RaceName = raceData.raceName;
            }

            if (avatarDefinition.Wardrobe == null)
            {
                avatarDefinition.Wardrobe = new string[0];
            }

            if (avatarDefinition.Colors == null)
            {
                avatarDefinition.Colors = new SharedColorDef[0];
            }

            return avatarDefinition;
        }

        /// <summary>
        /// Get the DNA. Unlike DynamicCharacterAvatar, this is cached because the character cannot be rebuilt.
        /// </summary>
        /// <param name="recipe"></param>
        /// <returns></returns>
        public Dictionary<string, DnaSetter> GetDNA(UMAData.UMARecipe recipe = null)
        {
            if (recipe != null)
            {
                Dictionary<string, DnaSetter> recipeDNA = new Dictionary<string, DnaSetter>();
                AddDNASetters(recipeDNA, recipe);
                return recipeDNA;
            }

            if (dna.Keys.Count == 0)
            {
                AddDNASetters(dna, GetRecipe());
            }
            return dna;
        }

        private void AddDNASetters(Dictionary<string, DnaSetter> target, UMAData.UMARecipe recipe)
        {
            RaceData raceData = recipe != null && recipe.raceData != null ? recipe.raceData : GetOriginalRace();
            if (raceData != null && raceData.useNewDNA)
            {
                EnsureNewDNACollection(recipe, raceData);
                AddNewDNASetters(target, recipe != null ? recipe.dnaInstanceCollection : null);
                return;
            }

            AddLegacyDNASetters(target, recipe, raceData);
        }

        private void AddNewDNASetters(Dictionary<string, DnaSetter> target, DNAInstanceCollection dnaInstanceCollection)
        {
            if (dnaInstanceCollection == null)
            {
                return;
            }

            Dictionary<DNAGroup, List<DNAInstance>> dnaByGroup = dnaInstanceCollection.GetDNAByGroup();
            foreach (KeyValuePair<DNAGroup, List<DNAInstance>> groupPair in dnaByGroup)
            {
                DNAGroup group = groupPair.Key;
                List<DNAInstance> instances = groupPair.Value;
                if (group == null || instances == null)
                {
                    continue;
                }

                for (int i = 0; i < instances.Count; i++)
                {
                    DNAInstance dnaInstance = instances[i];
                    if (dnaInstance == null || string.IsNullOrEmpty(dnaInstance.Name) || target.ContainsKey(dnaInstance.Name))
                    {
                        continue;
                    }

                    target.Add(dnaInstance.Name, new DnaSetter(dnaInstance, group, umaData));
                }
            }
        }

        private void AddLegacyDNASetters(Dictionary<string, DnaSetter> target, UMAData.UMARecipe recipe, RaceData raceData)
        {
            UMADnaBase[] dnaBase = recipe != null ? recipe.GetAllDna() : new UMADnaBase[0];

            for (int j = 0; j < dnaBase.Length; j++)
            {
                UMADnaBase db = dnaBase[j];
                if (db == null || db is UMADnaInstance)
                {
                    continue;
                }

                string Category = db.GetType().ToString();
                if (raceData != null)
                {
                    IDNAConverter[] dcb = raceData.GetConverters(db);
                    if (dcb.Length > 0 && dcb[0] != null && !string.IsNullOrEmpty(dcb[0].DisplayValue))
                    {
                        Category = dcb[0].DisplayValue;
                    }
                }

                for (int i = 0; i < db.Count; i++)
                {
                    if (db.Names == null || db.Values == null || i >= db.Names.Length || i >= db.Values.Length || string.IsNullOrEmpty(db.Names[i]))
                    {
                        continue;
                    }

                    if (target.ContainsKey(db.Names[i]))
                    {
                        target[db.Names[i]] = new DnaSetter(db.Names[i], db.Values[i], i, db, Category);
                    }
                    else
                    {
                        target.Add(db.Names[i], new DnaSetter(db.Names[i], db.Values[i], i, db, Category));
                    }
                }
            }
        }
    }
}
