using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMA.Examples
{
	public class UMACrowd : MonoBehaviour
	{
		public UMACrowdRandomSet[] randomPool;
		public UMAGeneratorBase generator;
		public UMAData umaData;
		public UMAContextBase UMAContextBase;
		public RuntimeAnimatorController animationController;
		public float atlasResolutionScale = 1;
		public bool generateUMA;
		public bool generateLotsUMA;
		public bool hideWhileGeneratingLots;
		public bool stressTest;
		public Vector2 umaCrowdSize;
		public bool randomDna;
		public bool allAtOnce;
		public UMARecipeBase[] additionalRecipes;

		public float space = 1;
		public Transform zeroPoint;
		private int spawnX;
		private int spawnY;

		public SharedColorTable SkinColors;
		public SharedColorTable HairColors;
		// public SharedColorTable EyeColors; TODO: Add support for eye colors
		// public SharedColorTable ClothesColors; TODO: Add support for clothes colors

		public string[] keywords;

		public UMADataEvent CharacterCreated;
		public UMADataEvent CharacterDestroyed;
		public UMADataEvent CharacterUpdated;

		void Awake()
		{
			if (space <= 0) space = 1;
			if (UMAContextBase == null)
            {
				UMAContextBase = UMAContext.Instance;
            }
			if (generator == null)
            {
				GameObject go = GameObject.Find("UMAGenerator");
				if (go != null)
                {
					generator = go.GetComponent<UMAGenerator>();
                }
				else
                {
					generator = UMAContextBase.GetComponent<UMAGenerator>();
                }
            }
		}

		void Update()
		{
			if (generateUMA)
			{
				umaCrowdSize = Vector2.one;
				int randomResult = Random.Range(0, 2);
				GenerateOneUMA(randomResult);

				generateUMA = false;
				generateLotsUMA = false;
			}

			if (generateLotsUMA)
			{
				if (generator.IsIdle())
				{
					if (allAtOnce)
                    {
						for (int i = 0; i < umaCrowdSize.x * umaCrowdSize.y; i++)
						{
							int randomResult = Random.Range(0, 2);
							GenerateOneUMA(randomResult);
							generateUMA = false;
							generateLotsUMA = false;
						}
					}
					else
                    {
						int randomResult = Random.Range(0, 2);
						GenerateOneUMA(randomResult);
					}
				}
			}

			if (stressTest && generator.IsIdle() && !generateLotsUMA && !generateUMA)
			{
				RandomizeAll();
			}
		}

		private void DefineSlots(UMACrowdRandomSet.CrowdRaceData race)
		{
			Color skinColor;
			Color HairColor;
			Color Shine = Color.black;

			if (SkinColors != null)
			{
				OverlayColorData ocd = SkinColors.colors[Random.Range(0, SkinColors.colors.Length)];

				skinColor = ocd.color;
				Shine = ocd.channelAdditiveMask[2];
			}
			else
			{
				float skinTone = Random.Range(0.1f, 0.6f);
				skinColor = new Color(skinTone + Random.Range(0.35f, 0.4f), skinTone + Random.Range(0.25f, 0.4f), skinTone + Random.Range(0.35f, 0.4f), 1);
			}
			if (HairColors != null)
			{
				HairColor = HairColors.colors[Random.Range(0, HairColors.colors.Length)].color;
			}
			else
			{ 
				HairColor = new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1.0f);
			}

			var keywordsLookup = new HashSet<string>(keywords);
			UMACrowdRandomSet.Apply(umaData, race, skinColor, HairColor, Shine, keywordsLookup, UMAContextBase);
		}

		void DefineSlots()
		{
			Color skinColor = new Color(1, 1, 1, 1);
			float skinTone;

			skinTone = Random.Range(0.1f, 0.6f);
			skinColor = new Color(skinTone + Random.Range(0.35f, 0.4f), skinTone + Random.Range(0.25f, 0.4f), skinTone + Random.Range(0.35f, 0.4f), 1);

			Color HairColor = new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1);

			if (umaData.umaRecipe.raceData.raceName == "HumanMale" || umaData.umaRecipe.raceData.raceName == "HumanMaleDCS")
			{
				int randomResult = 0;
				//Male Avatar

				umaData.umaRecipe.slotDataList = new SlotData[15];

				umaData.umaRecipe.slotDataList[0] = UMAContextBase.InstantiateSlot("MaleEyes");
				umaData.umaRecipe.slotDataList[0].AddOverlay(UMAContextBase.InstantiateOverlay("EyeOverlay"));
				umaData.umaRecipe.slotDataList[0].AddOverlay(UMAContextBase.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1] = UMAContextBase.InstantiateSlot("MaleFace");

					randomResult = Random.Range(0, 2);

					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHead02", skinColor));
					}
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1] = UMAContextBase.InstantiateSlot("MaleHead_Head");

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHead02", skinColor));
					}

					umaData.umaRecipe.slotDataList[7] = UMAContextBase.InstantiateSlot("MaleHead_Eyes", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					umaData.umaRecipe.slotDataList[9] = UMAContextBase.InstantiateSlot("MaleHead_Mouth", umaData.umaRecipe.slotDataList[1].GetOverlayList());

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[10] = UMAContextBase.InstantiateSlot("MaleHead_PigNose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHead_PigNose", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[10] = UMAContextBase.InstantiateSlot("MaleHead_Nose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[8] = UMAContextBase.InstantiateSlot("MaleHead_ElvenEars");
						umaData.umaRecipe.slotDataList[8].AddOverlay(UMAContextBase.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[8] = UMAContextBase.InstantiateSlot("MaleHead_Ears", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}
				}


				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHair01", HairColor * 0.25f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleHair02", HairColor * 0.25f));
				}
				else
				{

				}


				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}



				//Extra beard composition
				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleEyebrow01", HairColor * 0.05f));
				}
				else
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAContextBase.InstantiateOverlay("MaleEyebrow02", HairColor * 0.05f));
				}

				umaData.umaRecipe.slotDataList[2] = UMAContextBase.InstantiateSlot("MaleTorso");

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBody01", skinColor));
				}
				else
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAContextBase.InstantiateOverlay("MaleBody02", skinColor));
				}


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAContextBase.InstantiateOverlay("MaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[3] = UMAContextBase.InstantiateSlot("MaleHands", umaData.umaRecipe.slotDataList[2].GetOverlayList());

				umaData.umaRecipe.slotDataList[4] = UMAContextBase.InstantiateSlot("MaleInnerMouth");
				umaData.umaRecipe.slotDataList[4].AddOverlay(UMAContextBase.InstantiateOverlay("InnerMouth"));


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[5] = UMAContextBase.InstantiateSlot("MaleLegs", umaData.umaRecipe.slotDataList[2].GetOverlayList());
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAContextBase.InstantiateOverlay("MaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{
					umaData.umaRecipe.slotDataList[5] = UMAContextBase.InstantiateSlot("MaleJeans01");
					umaData.umaRecipe.slotDataList[5].AddOverlay(UMAContextBase.InstantiateOverlay("MaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[6] = UMAContextBase.InstantiateSlot("MaleFeet", umaData.umaRecipe.slotDataList[2].GetOverlayList());
			}
			else if (umaData.umaRecipe.raceData.raceName == "HumanFemale" || umaData.umaRecipe.raceData.raceName == "HumanFemaleDCS")
			{
				int randomResult = 0;
				//Female Avatar

				//Example of dynamic list
				List<SlotData> tempSlotList = new List<SlotData>();

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleEyes"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("EyeOverlay"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				int headIndex = 0;

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{

					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleFace"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_Head"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_Eyes", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_Mouth", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_Nose", tempSlotList[headIndex].GetOverlayList()));


					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_ElvenEars"));
						tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHead_Ears", tempSlotList[headIndex].GetOverlayList()));
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleEyelash"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleEyelash", Color.black));

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleTorso"));
				int bodyIndex = tempSlotList.Count - 1;
				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleBody01", skinColor));
				} if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleBody02", skinColor));
				}

				tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleShirt02", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 2)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleTshirt01"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleTshirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{

				}

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleHands", tempSlotList[bodyIndex].GetOverlayList()));

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleInnerMouth"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("InnerMouth"));

				tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleFeet", tempSlotList[bodyIndex].GetOverlayList()));


				randomResult = Random.Range(0, 2);

				if (randomResult == 0)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
					tempSlotList[bodyIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleShortHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleShortHair01", HairColor));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleLongHair01", HairColor));
				}
				else
				{
					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleLongHair01", HairColor));

					tempSlotList.Add(UMAContextBase.InstantiateSlot("FemaleLongHair01_Module"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAContextBase.InstantiateOverlay("FemaleLongHair01_Module", HairColor));
				}

				umaData.SetSlots(tempSlotList.ToArray());
			}
		}

		protected virtual void SetUMAData()
		{
			umaData.atlasResolutionScale = atlasResolutionScale;
			umaData.OnCharacterCreated += CharacterCreatedCallback;
		}

		void CharacterCreatedCallback(UMAData umaData)
		{
			if (generateLotsUMA && hideWhileGeneratingLots)
			{
				if (umaData.animator != null)
					umaData.animator.enabled = false;
				
				Renderer[] renderers = umaData.GetRenderers();
				for (int i = 0; i < renderers.Length; i++)
				{
					renderers[i].enabled = false;
				}
			}
		}
		//Dont use LegacyDNA here 
		public static void RandomizeShape(UMAData umaData)
		{
			var allDNA = umaData.umaRecipe.GetAllDna();
			UMADnaBase mainDNA = null;
			//find the main dna to use
			foreach(UMADnaBase dna in allDNA)
			{
				if(System.Array.IndexOf(dna.Names, "height") > -1)
				{
					mainDNA = dna;
					break;
				}
			}

			if (mainDNA != null && mainDNA.GetType() == typeof(DynamicUMADna))
			{
				DynamicUMADna umaDna = mainDNA as DynamicUMADna;
				umaDna.SetValue("height", Random.Range(0.4f, 0.5f));
				umaDna.SetValue("headSize", Random.Range(0.485f, 0.515f));
				umaDna.SetValue("headWidth", Random.Range(0.4f, 0.6f));

				umaDna.SetValue("neckThickness", Random.Range(0.495f, 0.51f));

				if (umaData.umaRecipe.raceData.raceName.IndexOf("HumanMale") > -1)
				{
					umaDna.SetValue("handsSize", Random.Range(0.485f, 0.515f));
					umaDna.SetValue("feetSize", Random.Range(0.485f, 0.515f));
					umaDna.SetValue("legSeparation", Random.Range(0.4f, 0.6f));
					umaDna.SetValue("waist", 0.5f);
				}
				else
				{
					umaDna.SetValue("handsSize", Random.Range(0.485f, 0.515f));
					umaDna.SetValue("feetSize", Random.Range(0.485f, 0.515f));
					umaDna.SetValue("legSeparation", Random.Range(0.485f, 0.515f));
					umaDna.SetValue("waist", Random.Range(0.3f, 0.8f));
				}

				umaDna.SetValue("armLength", Random.Range(0.485f, 0.515f));
				umaDna.SetValue("forearmLength", Random.Range(0.485f, 0.515f));
				umaDna.SetValue("armWidth", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("forearmWidth", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("upperMuscle", Random.Range(0.0f, 1.0f));
				umaDna.SetValue("upperWeight", Random.Range(-0.2f, 0.2f) + umaDna.GetValue("upperMuscle"));
				if (umaDna.GetValue("upperWeight") > 1.0) { umaDna.SetValue("upperWeight", 1.0f); }
				if (umaDna.GetValue("upperWeight") < 0.0) { umaDna.SetValue("upperWeight", 0.0f); }

				umaDna.SetValue("lowerMuscle", Random.Range(-0.2f, 0.2f) + umaDna.GetValue("upperMuscle"));
				if (umaDna.GetValue("lowerMuscle") > 1.0) { umaDna.SetValue("lowerMuscle", 1.0f); }
				if (umaDna.GetValue("lowerMuscle") < 0.0) { umaDna.SetValue("lowerMuscle", 0.0f); }

				umaDna.SetValue("lowerWeight", Random.Range(-0.1f, 0.1f) + umaDna.GetValue("upperWeight"));
				if (umaDna.GetValue("lowerWeight") > 1.0) { umaDna.SetValue("lowerWeight", 1.0f); }
				if (umaDna.GetValue("lowerWeight") < 0.0) { umaDna.SetValue("lowerWeight", 0.0f); }

				umaDna.SetValue("belly", umaDna.GetValue("upperWeight") * Random.Range(0.0f, 1.0f));
				umaDna.SetValue("legsSize", Random.Range(0.45f, 0.6f));
				umaDna.SetValue("gluteusSize", Random.Range(0.4f, 0.6f));

				umaDna.SetValue("earsSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("earsPosition", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("earsRotation", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("noseSize", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("noseCurve", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("noseWidth", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("noseInclination", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("nosePosition", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("nosePronounced", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("noseFlatten", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("chinSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("chinPronounced", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("chinPosition", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("mandibleSize", Random.Range(0.45f, 0.52f));
				umaDna.SetValue("jawsSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("jawsPosition", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("cheekSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("cheekPosition", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("lowCheekPronounced", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("lowCheekPosition", Random.Range(0.3f, 0.8f));

				umaDna.SetValue("foreheadSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("foreheadPosition", Random.Range(0.15f, 0.65f));

				umaDna.SetValue("lipsSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("mouthSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("eyeRotation", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("eyeSize", Random.Range(0.3f, 0.8f));
				umaDna.SetValue("breastSize", Random.Range(0.3f, 0.8f));
			}
			else if (mainDNA != null)
			{
				RandomizeShapeLegacy(umaData);

			}
		}

		private static void RandomizeShapeLegacy(UMAData umaData)
		{
			UMADnaHumanoid umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			umaDna.height = Random.Range(0.4f, 0.5f);
			umaDna.headSize = Random.Range(0.485f, 0.515f);
			umaDna.headWidth = Random.Range(0.4f, 0.6f);

			umaDna.neckThickness = Random.Range(0.495f, 0.51f);

			if (umaData.umaRecipe.raceData.raceName == "HumanMale")
			{
				umaDna.handsSize = Random.Range(0.485f, 0.515f);
				umaDna.feetSize = Random.Range(0.485f, 0.515f);
				umaDna.legSeparation = Random.Range(0.4f, 0.6f);
				umaDna.waist = 0.5f;
			}
			else
			{
				umaDna.handsSize = Random.Range(0.485f, 0.515f);
				umaDna.feetSize = Random.Range(0.485f, 0.515f);
				umaDna.legSeparation = Random.Range(0.485f, 0.515f);
				umaDna.waist = Random.Range(0.3f, 0.8f);
			}

			umaDna.armLength = Random.Range(0.485f, 0.515f);
			umaDna.forearmLength = Random.Range(0.485f, 0.515f);
			umaDna.armWidth = Random.Range(0.3f, 0.8f);
			umaDna.forearmWidth = Random.Range(0.3f, 0.8f);

			umaDna.upperMuscle = Random.Range(0.0f, 1.0f);
			umaDna.upperWeight = Random.Range(-0.2f, 0.2f) + umaDna.upperMuscle;
			if (umaDna.upperWeight > 1.0) { umaDna.upperWeight = 1.0f; }
			if (umaDna.upperWeight < 0.0) { umaDna.upperWeight = 0.0f; }

			umaDna.lowerMuscle = Random.Range(-0.2f, 0.2f) + umaDna.upperMuscle;
			if (umaDna.lowerMuscle > 1.0) { umaDna.lowerMuscle = 1.0f; }
			if (umaDna.lowerMuscle < 0.0) { umaDna.lowerMuscle = 0.0f; }

			umaDna.lowerWeight = Random.Range(-0.1f, 0.1f) + umaDna.upperWeight;
			if (umaDna.lowerWeight > 1.0) { umaDna.lowerWeight = 1.0f; }
			if (umaDna.lowerWeight < 0.0) { umaDna.lowerWeight = 0.0f; }

			umaDna.belly = umaDna.upperWeight * Random.Range(0.0f, 1.0f);
			umaDna.legsSize = Random.Range(0.45f, 0.6f);
			umaDna.gluteusSize = Random.Range(0.4f, 0.6f);

			umaDna.earsSize = Random.Range(0.3f, 0.8f);
			umaDna.earsPosition = Random.Range(0.3f, 0.8f);
			umaDna.earsRotation = Random.Range(0.3f, 0.8f);

			umaDna.noseSize = Random.Range(0.3f, 0.8f);

			umaDna.noseCurve = Random.Range(0.3f, 0.8f);
			umaDna.noseWidth = Random.Range(0.3f, 0.8f);
			umaDna.noseInclination = Random.Range(0.3f, 0.8f);
			umaDna.nosePosition = Random.Range(0.3f, 0.8f);
			umaDna.nosePronounced = Random.Range(0.3f, 0.8f);
			umaDna.noseFlatten = Random.Range(0.3f, 0.8f);

			umaDna.chinSize = Random.Range(0.3f, 0.8f);
			umaDna.chinPronounced = Random.Range(0.3f, 0.8f);
			umaDna.chinPosition = Random.Range(0.3f, 0.8f);

			umaDna.mandibleSize = Random.Range(0.45f, 0.52f);
			umaDna.jawsSize = Random.Range(0.3f, 0.8f);
			umaDna.jawsPosition = Random.Range(0.3f, 0.8f);

			umaDna.cheekSize = Random.Range(0.3f, 0.8f);
			umaDna.cheekPosition = Random.Range(0.3f, 0.8f);
			umaDna.lowCheekPronounced = Random.Range(0.3f, 0.8f);
			umaDna.lowCheekPosition = Random.Range(0.3f, 0.8f);

			umaDna.foreheadSize = Random.Range(0.3f, 0.8f);
			umaDna.foreheadPosition = Random.Range(0.15f, 0.65f);

			umaDna.lipsSize = Random.Range(0.3f, 0.8f);
			umaDna.mouthSize = Random.Range(0.3f, 0.8f);
			umaDna.eyeRotation = Random.Range(0.3f, 0.8f);
			umaDna.eyeSize = Random.Range(0.3f, 0.8f);
			umaDna.breastSize = Random.Range(0.3f, 0.8f);
		}

		protected virtual void GenerateUMAShapes()
		{
			/*UMADnaHumanoid umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			if (umaDna ==  null)
			{
				umaDna = new UMADnaHumanoid();
				umaData.umaRecipe.AddDna(umaDna);
			}*/

			if (randomDna)
			{
				RandomizeShape(umaData);
			}
		}

		public void ResetSpawnPos(){
			spawnX = 0;
			spawnY = 0;
		}

		public GameObject GenerateUMA(int sex, Vector3 position)
		{
			GameObject newGO = new GameObject("Generated Character");
			newGO.transform.parent = transform;
			newGO.transform.position = position;
			newGO.transform.rotation = zeroPoint != null ? zeroPoint.rotation : transform.rotation;

			UMADynamicAvatar umaDynamicAvatar = newGO.AddComponent<UMADynamicAvatar>();
			umaDynamicAvatar.Initialize();
			umaData = umaDynamicAvatar.umaData;
			umaData.CharacterCreated = new UMADataEvent(CharacterCreated);
			umaData.CharacterDestroyed = new UMADataEvent(CharacterDestroyed);
			umaData.CharacterUpdated = new UMADataEvent(CharacterUpdated);
			umaDynamicAvatar.umaGenerator = generator;
			umaData.umaGenerator = generator;
			var umaRecipe = umaDynamicAvatar.umaData.umaRecipe;
			UMACrowdRandomSet.CrowdRaceData race = null;

			if (randomPool != null && randomPool.Length > 0)
			{
				int randomResult = Random.Range(0, randomPool.Length);
				race = randomPool[randomResult].data;
				string theRaceId = race.raceID;
				umaRecipe.SetRace(UMAContextBase.GetRace(race.raceID));
			}
			else
			{
				if (sex == 0)
				{
					umaRecipe.SetRace(UMAContextBase.GetRace("HumanMaleDCS"));
				}
				else
				{
					umaRecipe.SetRace(UMAContextBase.GetRace("HumanFemaleDCS"));
				}
			}

			SetUMAData();

			if (race != null && race.slotElements.Length > 0)
			{
				DefineSlots(race);
			}
			else
			{
				DefineSlots();
			}

			AddAdditionalSlots();

			GenerateUMAShapes();

			if (animationController != null)
			{
				umaDynamicAvatar.animationController = animationController;
			}
			umaDynamicAvatar.Show();

			return newGO;
		}

		public GameObject GenerateOneUMA(int sex)
		{
			Vector3 zeroPos = Vector3.zero;
			if (zeroPoint != null)
				zeroPos = zeroPoint.position;
			Vector3 newPos = zeroPos + new Vector3((spawnX - umaCrowdSize.x / 2f + 0.5f) * space, 0f, (spawnY - umaCrowdSize.y / 2f + 0.5f) * space);

			if (spawnY < umaCrowdSize.y)
			{
				spawnX++;
				if (spawnX >= umaCrowdSize.x)
				{
					spawnX = 0;
					spawnY++;
				}
			}
			else
			{
				if (hideWhileGeneratingLots)
				{
					UMAData[] generatedCrowd = GetComponentsInChildren<UMAData>();
					foreach (UMAData generatedData in generatedCrowd)
					{
						if (generatedData.animator != null)
							generatedData.animator.enabled = true;
						
						Renderer[] renderers = generatedData.GetRenderers();
						for (int i = 0; i < renderers.Length; i++)
						{
							renderers[i].enabled = true;
						}
					}
				}
				generateLotsUMA = false;
				spawnX = 0;
				spawnY = 0;
				return null;
			}
			return GenerateUMA(sex, newPos);
		}

		private void AddAdditionalSlots()
		{
			umaData.AddAdditionalRecipes(additionalRecipes, UMAContextBase.Instance);
		}

		public void ReplaceAll()
		{
			if (generateUMA || generateLotsUMA)
			{
				Debug.LogWarning("Can't replace while spawning.");
				return;
			}

			int childCount = gameObject.transform.childCount;
			while(--childCount >= 0)
			{
				Transform child = gameObject.transform.GetChild(childCount);
				UMAUtils.DestroySceneObject(child.gameObject);
			}

			if (umaCrowdSize.x <= 1 && umaCrowdSize.y <= 1)
				generateUMA = true;
			else
				generateLotsUMA = true;
		}

		public void RandomizeAllDna()
		{
			for (int i = 0; i < transform.childCount; i++)
			{
				var umaData = transform.GetChild(i).GetComponent<UMAData>();
				UMACrowd.RandomizeShape(umaData);
				umaData.Dirty(true, false, false);
			}
		}

		public void RandomizeAll()
		{
			if (generateUMA || generateLotsUMA)
			{
				Debug.LogWarning("Can't randomize while spawning.");
				return;
			}
			
			int childCount = gameObject.transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				Transform child = gameObject.transform.GetChild(i);
				UMADynamicAvatar umaDynamicAvatar = child.gameObject.GetComponent<UMADynamicAvatar>();
				if (umaDynamicAvatar == null)
				{
					Debug.Log("Couldn't find dynamic avatar on child: " + child.gameObject.name);
					continue;
				}
				umaData = umaDynamicAvatar.umaData;
				var umaRecipe = umaDynamicAvatar.umaData.umaRecipe;
				UMACrowdRandomSet.CrowdRaceData race = null;
				
				if (randomPool != null && randomPool.Length > 0)
				{
					int randomResult = Random.Range(0, randomPool.Length);
					race = randomPool[randomResult].data;
					umaRecipe.SetRace(UMAContextBase.GetRace(race.raceID));
				}
				else
				{
					if (Random.value < 0.5f)
					{
						umaRecipe.SetRace(UMAContextBase.GetRace("HumanMaleDCS"));
					}
					else
					{
						umaRecipe.SetRace(UMAContextBase.GetRace("HumanFemaleDCS"));
					}
				}
				
	//			SetUMAData();
				
				if (race != null && race.slotElements.Length > 0)
				{
					DefineSlots(race);
				}
				else
				{
					DefineSlots();
				}
				
				AddAdditionalSlots();

				GenerateUMAShapes();
				
				if (animationController != null)
				{
					umaDynamicAvatar.animationController = animationController;
				}
				umaDynamicAvatar.Show();
			}
		}

	}
}
