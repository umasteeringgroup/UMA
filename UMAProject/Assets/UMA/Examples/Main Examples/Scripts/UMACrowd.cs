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
		public UMAContext umaContext;
		public RuntimeAnimatorController animationController;
		public float atlasResolutionScale = 1;
		public bool generateUMA;
		public bool generateLotsUMA;
		public bool hideWhileGeneratingLots;
		public bool stressTest;
		public Vector2 umaCrowdSize;
		public bool randomDna;
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
					int randomResult = Random.Range(0, 2);
					GenerateOneUMA(randomResult);
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
			UMACrowdRandomSet.Apply(umaData, race, skinColor, HairColor, Shine, keywordsLookup, umaContext);
		}

		void DefineSlots()
		{
			Color skinColor = new Color(1, 1, 1, 1);
			float skinTone;

			skinTone = Random.Range(0.1f, 0.6f);
			skinColor = new Color(skinTone + Random.Range(0.35f, 0.4f), skinTone + Random.Range(0.25f, 0.4f), skinTone + Random.Range(0.35f, 0.4f), 1);

			Color HairColor = new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1);

			if (umaData.umaRecipe.raceData.raceName == "HumanMale")
			{
				int randomResult = 0;
				//Male Avatar

				umaData.umaRecipe.slotDataList = new SlotData[15];

				umaData.umaRecipe.slotDataList[0] = umaContext.InstantiateSlot("MaleEyes");
				umaData.umaRecipe.slotDataList[0].AddOverlay(umaContext.InstantiateOverlay("EyeOverlay"));
				umaData.umaRecipe.slotDataList[0].AddOverlay(umaContext.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1] = umaContext.InstantiateSlot("MaleFace");

					randomResult = Random.Range(0, 2);

					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHead02", skinColor));
					}
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1] = umaContext.InstantiateSlot("MaleHead_Head");

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHead02", skinColor));
					}

					umaData.umaRecipe.slotDataList[7] = umaContext.InstantiateSlot("MaleHead_Eyes", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					umaData.umaRecipe.slotDataList[9] = umaContext.InstantiateSlot("MaleHead_Mouth", umaData.umaRecipe.slotDataList[1].GetOverlayList());

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[10] = umaContext.InstantiateSlot("MaleHead_PigNose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
						umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHead_PigNose", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[10] = umaContext.InstantiateSlot("MaleHead_Nose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[8] = umaContext.InstantiateSlot("MaleHead_ElvenEars");
						umaData.umaRecipe.slotDataList[8].AddOverlay(umaContext.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[8] = umaContext.InstantiateSlot("MaleHead_Ears", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}
				}


				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHair01", HairColor * 0.25f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleHair02", HairColor * 0.25f));
				}
				else
				{

				}


				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}



				//Extra beard composition
				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleEyebrow01", HairColor * 0.05f));
				}
				else
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(umaContext.InstantiateOverlay("MaleEyebrow02", HairColor * 0.05f));
				}

				umaData.umaRecipe.slotDataList[2] = umaContext.InstantiateSlot("MaleTorso");

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(umaContext.InstantiateOverlay("MaleBody01", skinColor));
				}
				else
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(umaContext.InstantiateOverlay("MaleBody02", skinColor));
				}


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(umaContext.InstantiateOverlay("MaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[3] = umaContext.InstantiateSlot("MaleHands", umaData.umaRecipe.slotDataList[2].GetOverlayList());

				umaData.umaRecipe.slotDataList[4] = umaContext.InstantiateSlot("MaleInnerMouth");
				umaData.umaRecipe.slotDataList[4].AddOverlay(umaContext.InstantiateOverlay("InnerMouth"));


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[5] = umaContext.InstantiateSlot("MaleLegs", umaData.umaRecipe.slotDataList[2].GetOverlayList());
					umaData.umaRecipe.slotDataList[2].AddOverlay(umaContext.InstantiateOverlay("MaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{
					umaData.umaRecipe.slotDataList[5] = umaContext.InstantiateSlot("MaleJeans01");
					umaData.umaRecipe.slotDataList[5].AddOverlay(umaContext.InstantiateOverlay("MaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[6] = umaContext.InstantiateSlot("MaleFeet", umaData.umaRecipe.slotDataList[2].GetOverlayList());
			}
			else if (umaData.umaRecipe.raceData.raceName == "HumanFemale")
			{
				int randomResult = 0;
				//Female Avatar

				//Example of dynamic list
				List<SlotData> tempSlotList = new List<SlotData>();

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleEyes"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("EyeOverlay"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				int headIndex = 0;

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{

					tempSlotList.Add(umaContext.InstantiateSlot("FemaleFace"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_Head"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_Eyes", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_Mouth", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_Nose", tempSlotList[headIndex].GetOverlayList()));


					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_ElvenEars"));
						tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						tempSlotList.Add(umaContext.InstantiateSlot("FemaleHead_Ears", tempSlotList[headIndex].GetOverlayList()));
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleEyelash"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("FemaleEyelash", Color.black));

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleTorso"));
				int bodyIndex = tempSlotList.Count - 1;
				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleBody01", skinColor));
				} if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleBody02", skinColor));
				}

				tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleShirt02", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 2)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleTshirt01"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("FemaleTshirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{

				}

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleHands", tempSlotList[bodyIndex].GetOverlayList()));

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleInnerMouth"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("InnerMouth"));

				tempSlotList.Add(umaContext.InstantiateSlot("FemaleFeet", tempSlotList[bodyIndex].GetOverlayList()));


				randomResult = Random.Range(0, 2);

				if (randomResult == 0)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
					tempSlotList[bodyIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleShortHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleShortHair01", HairColor));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleLongHair01", HairColor));
				}
				else
				{
					tempSlotList.Add(umaContext.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(umaContext.InstantiateOverlay("FemaleLongHair01", HairColor));

					tempSlotList.Add(umaContext.InstantiateSlot("FemaleLongHair01_Module"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(umaContext.InstantiateOverlay("FemaleLongHair01_Module", HairColor));
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

		public static void RandomizeShape(UMAData umaData)
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

			umaDna.belly = umaDna.upperWeight * Random.Range(0.0f,1.0f);
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
			UMADnaHumanoid umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			if (umaDna ==  null)
			{
				umaDna = new UMADnaHumanoid();
				umaData.umaRecipe.AddDna(umaDna);
			}

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
				umaRecipe.SetRace(umaContext.GetRace(race.raceID));
			}
			else
			{
				if (sex == 0)
				{
					umaRecipe.SetRace(umaContext.GetRace("HumanMale"));
				}
				else
				{
					umaRecipe.SetRace(umaContext.GetRace("HumanFemale"));
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
			umaData.AddAdditionalRecipes(additionalRecipes, UMAContext.FindInstance());
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
					umaRecipe.SetRace(umaContext.GetRace(race.raceID));
				}
				else
				{
					if (Random.value < 0.5f)
					{
						umaRecipe.SetRace(umaContext.GetRace("HumanMale"));
					}
					else
					{
						umaRecipe.SetRace(umaContext.GetRace("HumanFemale"));
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
