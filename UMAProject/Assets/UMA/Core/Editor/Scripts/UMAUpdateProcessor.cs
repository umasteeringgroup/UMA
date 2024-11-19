using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{
    public static class UMAUpdateProcessor
    {
        public static List<DynamicCharacterAvatar> GetSceneEditTimeAvatars()
        {
            List<DynamicCharacterAvatar> EditTimeAvatars = new List<DynamicCharacterAvatar>();

            Scene scene = SceneManager.GetActiveScene();

            GameObject[] sceneObjs = scene.GetRootGameObjects();
            foreach (GameObject go in sceneObjs)
            {
                DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
                if (dcas.Length > 0)
                {
                    foreach (DynamicCharacterAvatar dca in dcas)
                    {
                        if (dca.editorTimeGeneration == false)
                        {
                            continue;
                        }

                        EditTimeAvatars.Add(dca);
                    }
                }
            }
            return EditTimeAvatars;
        }

        public static void UpdateRecipe(UMATextRecipe recipe)
        {
            if (recipe == null)
            {
                return;
            }

            UMAAssetIndexer.Instance.ReleaseReference(recipe);

            List<DynamicCharacterAvatar> Avatars = GetSceneEditTimeAvatars();

            if (recipe is UMAWardrobeRecipe)
            {
                foreach (DynamicCharacterAvatar dca in Avatars)
                {
                    var items = dca.preloadWardrobeRecipes.recipes;
                    foreach (var wi in items)
                    {
                        if (wi == null)
                        {
                            continue;
                        }

                        if (wi._recipe == null)
                        {
                            continue;
                        }

                        var rcp = wi._recipe;
                        if (rcp.name == recipe.name)
                        {
                            dca.GenerateSingleUMA();
                            break;
                        }
                    }
                }
                UMAAssetIndexer.Instance.ReleaseReference(recipe);
                return;
            }

            if (recipe is UMATextRecipe)
            {
                foreach (DynamicCharacterAvatar dca in Avatars)
                {
                    if (dca.activeRace.data != null)
                    {
                        RaceData rc = dca.activeRace.data;
                        if (recipe == rc.baseRaceRecipe)
                        {
                            dca.GenerateSingleUMA();
                        }
                    }
                }
            }
        }

        public static void UpdateSlot(SlotDataAsset slot, bool doItAll = true)
        {
            if (slot == null)
            {
                Debug.Log("UpdateSlot: Slot is null");
                return;
            }
            // look at the slot list of any generated UMA
            UMAAssetIndexer.Instance.ReleaseReference(slot);
            List<DynamicCharacterAvatar> Avatars = GetSceneEditTimeAvatars();

            Debug.Log($"Rebuilding {Avatars.Count} Avatars");
            foreach (DynamicCharacterAvatar dca in Avatars)
            {
                UMAData ud = dca.gameObject.GetComponent<UMAData>();
                if (ud != null)
                {
                    if (ud.umaRecipe != null)
                    {
                        SlotData[] slots = ud.umaRecipe.GetAllSlots();
                        if (slots != null)
                        {
                            foreach (SlotData sd in slots)
                            {
                                if (sd.slotName == slot.slotName)
                                {
                                    if (doItAll)
                                    {
                                        dca.GenerateSingleUMA();
                                    }
                                    else
                                    {
                                        dca.GenerateSingleUMA(true);
                                    }
                                    dca.ForceUpdate(false,false,true);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void UpdateOverlay(OverlayDataAsset overlay)
        {
            if (overlay == null)
            {
                return;
            }

            UMAAssetIndexer.Instance.ReleaseReference(overlay);
            List<DynamicCharacterAvatar> Avatars = GetSceneEditTimeAvatars();

            foreach (DynamicCharacterAvatar dca in Avatars)
            {
                bool hasMatchingOverlay = false;

                UMAData ud = dca.gameObject.GetComponent<UMAData>();
                if (ud != null)
                {
                    if (ud.umaRecipe != null)
                    {
                        SlotData[] slots = ud.umaRecipe.GetAllSlots();
                        if (slots != null)
                        {
                            foreach (SlotData sd in slots)
                            {
                                List<OverlayData> odl = sd.GetOverlayList();
                                foreach (OverlayData od in odl)
                                {
                                    if (od.asset == overlay)
                                    {
                                        hasMatchingOverlay = true;
                                        break;
                                    }
                                }
                                if (hasMatchingOverlay)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
                if (hasMatchingOverlay)
                {
                    dca.GenerateSingleUMA();
                }
            }
        }

        public static void UpdateRace(RaceData race)
        {
            if (race == null)
            {
                return;
            }

            UMAAssetIndexer.Instance.ReleaseReference(race);
            List<DynamicCharacterAvatar> Avatars = GetSceneEditTimeAvatars();

            foreach (DynamicCharacterAvatar dca in Avatars)
            {
                if (dca.activeRace.data != null)
                {
                    RaceData rc = dca.activeRace.data;
                    if (rc == race)
                    {
                        dca.GenerateSingleUMA();
                    }
                }
            }
        }
    }
}
