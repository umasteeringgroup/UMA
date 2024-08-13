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
            umaData.staticCharacter = true;
            umaData.SetupSkeleton();
            DNA = UMAPackedRecipeBase.UnPackDNA(PackedDNA);
            UMA.UMAData.UMARecipe umaRecipe = umaData._umaRecipe;

            umaRecipe.ClearDna();

            for (int i = 0; i < DNA.Count; i++)
            {
                UMADnaBase umd = DNA[i];
                umaRecipe.AddDna(umd);
            }
            umaData._umaRecipe.ClearDNAConverters();
        }

        public void LoadDNAFromAvatarDefinition(AvatarDefinition adf)
        {
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
                    DnaDef def = new DnaDef(d.Name, d.Value);
                    Dna.Add(def);
            }
            avatarDefinition.Dna = Dna.ToArray();
            avatarDefinition.RaceName = originalRace.raceName;

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
            if (dna.Keys.Count == 0)
            {
                UMADnaBase[] dnaBase = umaData.GetAllDna();

                if (recipe == null)
                {
                    dnaBase = umaData.GetAllDna();
                }
                else
                {
                    dnaBase = recipe.GetAllDna();
                }

                for (int j = 0; j < dnaBase.Length; j++)
                {
                    UMADnaBase db = dnaBase[j];
                    string Category = db.GetType().ToString();
                    IDNAConverter[] dcb = originalRace.GetConverters(db);

                    if (dcb.Length > 0 && dcb[0] != null && (!string.IsNullOrEmpty(dcb[0].DisplayValue)))
                    {
                        Category = dcb[0].DisplayValue;
                    }

                    for (int i = 0; i < db.Count; i++)
                    {
                        if (dna.ContainsKey(db.Names[i]))
                        {
                            dna[db.Names[i]] = new DnaSetter(db.Names[i], db.Values[i], i, db, Category);
                        }
                        else
                        {
                            dna.Add(db.Names[i], new DnaSetter(db.Names[i], db.Values[i], i, db, Category));
                        }
                    }
                }
            }
            return dna;
        }
    }
}
