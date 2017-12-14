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
//		public UMAContextBase umaContext;
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

		// Indices of humanoidDNA entries
		static bool indicesSet = false;
		static int height;
		static int headSize;
		static int headWidth;
		static int neckThickness;
		static int armLength;
		static int forearmLength;
		static int armWidth;
		static int forearmWidth;

		static int handsSize;
		static int feetSize;
		static int legSeparation;
		static int upperMuscle;
		static int lowerMuscle;
		static int upperWeight;
		static int lowerWeight;
		static int legsSize;
		static int belly;
		static int waist;
		static int gluteusSize;

		static int earsSize;
		static int earsPosition;
		static int earsRotation;
		static int noseSize;
		static int noseCurve;
		static int noseWidth;
		static int noseInclination;
		static int nosePosition;
		static int nosePronounced;
		static int noseFlatten;

		static int chinSize;
		static int chinPronounced;
		static int chinPosition;

		static int mandibleSize;
		static int jawsSize;
		static int jawsPosition;

		static int cheekSize;
		static int cheekPosition;
		static int lowCheekPronounced;
		static int lowCheekPosition;

		static int foreheadSize;
		static int foreheadPosition;

		static int lipsSize;
		static int mouthSize;
		static int eyeRotation;
		static int eyeSize;

		static int breastSize;

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

		private static void buildIndices(UMADnaBase dna)
		{
			if (indicesSet) return;
			if (dna == null) return;

			height = dna.GetIndex("height");
			headSize = dna.GetIndex("headSize");
			headWidth = dna.GetIndex("headWidth");
			neckThickness = dna.GetIndex("neckThickness");
			armLength = dna.GetIndex("armLength");
			forearmLength = dna.GetIndex("forearmLength");
			armWidth = dna.GetIndex("armWidth");
			forearmWidth = dna.GetIndex("forearmWidth");

			handsSize = dna.GetIndex("handsSize");
			feetSize = dna.GetIndex("feetSize");
			legSeparation = dna.GetIndex("legSeparation");
			upperMuscle = dna.GetIndex("upperMuscle");
			lowerMuscle = dna.GetIndex("lowerMuscle");
			upperWeight = dna.GetIndex("upperWeight");
			lowerWeight = dna.GetIndex("lowerWeight");
			legsSize = dna.GetIndex("legsSize");
			belly = dna.GetIndex("belly");
			waist = dna.GetIndex("waist");
			gluteusSize = dna.GetIndex("gluteusSize");

			earsSize = dna.GetIndex("earsSize");
			earsPosition = dna.GetIndex("earsPosition");
			earsRotation = dna.GetIndex("earsRotation");
			noseSize = dna.GetIndex("noseSize");
			noseCurve = dna.GetIndex("noseCurve");
			noseWidth = dna.GetIndex("noseWidth");
			noseInclination = dna.GetIndex("noseInclination");
			nosePosition = dna.GetIndex("nosePosition");
			nosePronounced = dna.GetIndex("nosePronounced");
			noseFlatten = dna.GetIndex("noseFlatten");

			chinSize = dna.GetIndex("chinSize");
			chinPronounced = dna.GetIndex("chinPronounced");
			chinPosition = dna.GetIndex("chinPosition");

			mandibleSize = dna.GetIndex("mandibleSize");
			jawsSize = dna.GetIndex("jawsSize");
			jawsPosition = dna.GetIndex("jawsPosition");

			cheekSize = dna.GetIndex("cheekSize");
			cheekPosition = dna.GetIndex("cheekPosition");
			lowCheekPronounced = dna.GetIndex("lowCheekPronounced");
			lowCheekPosition = dna.GetIndex("lowCheekPosition");

			foreheadSize = dna.GetIndex("foreheadSize");
			foreheadPosition = dna.GetIndex("foreheadPosition");

			lipsSize = dna.GetIndex("lipsSize");
			mouthSize = dna.GetIndex("mouthSize");
			eyeRotation = dna.GetIndex("eyeRotation");
			eyeSize = dna.GetIndex("eyeSize");

			breastSize = dna.GetIndex("breastSize");

			indicesSet = true;
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
			UMACrowdRandomSet.Apply(umaData, race, skinColor, HairColor, Shine, keywordsLookup);
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

				umaData.umaRecipe.slotDataList[0] = UMAGlobal.Context.InstantiateSlot("MaleEyes");
				umaData.umaRecipe.slotDataList[0].AddOverlay(UMAGlobal.Context.InstantiateOverlay("EyeOverlay"));
				umaData.umaRecipe.slotDataList[0].AddOverlay(UMAGlobal.Context.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1] = UMAGlobal.Context.InstantiateSlot("MaleFace");

					randomResult = Random.Range(0, 2);

					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHead02", skinColor));
					}
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1] = UMAGlobal.Context.InstantiateSlot("MaleHead_Head");

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHead01", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHead02", skinColor));
					}

					umaData.umaRecipe.slotDataList[7] = UMAGlobal.Context.InstantiateSlot("MaleHead_Eyes", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					umaData.umaRecipe.slotDataList[9] = UMAGlobal.Context.InstantiateSlot("MaleHead_Mouth", umaData.umaRecipe.slotDataList[1].GetOverlayList());

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[10] = UMAGlobal.Context.InstantiateSlot("MaleHead_PigNose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
						umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHead_PigNose", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[10] = UMAGlobal.Context.InstantiateSlot("MaleHead_Nose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						umaData.umaRecipe.slotDataList[8] = UMAGlobal.Context.InstantiateSlot("MaleHead_ElvenEars");
						umaData.umaRecipe.slotDataList[8].AddOverlay(UMAGlobal.Context.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						umaData.umaRecipe.slotDataList[8] = UMAGlobal.Context.InstantiateSlot("MaleHead_Ears", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					}
				}


				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHair01", HairColor * 0.25f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleHair02", HairColor * 0.25f));
				}
				else
				{

				}


				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}



				//Extra beard composition
				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
				}
				else if (randomResult == 2)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
				}
				else
				{

				}

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleEyebrow01", HairColor * 0.05f));
				}
				else
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleEyebrow02", HairColor * 0.05f));
				}

				umaData.umaRecipe.slotDataList[2] = UMAGlobal.Context.InstantiateSlot("MaleTorso");

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBody01", skinColor));
				}
				else
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleBody02", skinColor));
				}


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[3] = UMAGlobal.Context.InstantiateSlot("MaleHands", umaData.umaRecipe.slotDataList[2].GetOverlayList());

				umaData.umaRecipe.slotDataList[4] = UMAGlobal.Context.InstantiateSlot("MaleInnerMouth");
				umaData.umaRecipe.slotDataList[4].AddOverlay(UMAGlobal.Context.InstantiateOverlay("InnerMouth"));


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[5] = UMAGlobal.Context.InstantiateSlot("MaleLegs", umaData.umaRecipe.slotDataList[2].GetOverlayList());
					umaData.umaRecipe.slotDataList[2].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{
					umaData.umaRecipe.slotDataList[5] = UMAGlobal.Context.InstantiateSlot("MaleJeans01");
					umaData.umaRecipe.slotDataList[5].AddOverlay(UMAGlobal.Context.InstantiateOverlay("MaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				umaData.umaRecipe.slotDataList[6] = UMAGlobal.Context.InstantiateSlot("MaleFeet", umaData.umaRecipe.slotDataList[2].GetOverlayList());
			}
			else if (umaData.umaRecipe.raceData.raceName == "HumanFemale")
			{
				int randomResult = 0;
				//Female Avatar

				//Example of dynamic list
				List<SlotData> tempSlotList = new List<SlotData>();

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleEyes"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("EyeOverlay"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				int headIndex = 0;

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{

					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleFace"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_Head"));
					headIndex = tempSlotList.Count - 1;
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleHead01", skinColor));
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_Eyes", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_Mouth", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_Nose", tempSlotList[headIndex].GetOverlayList()));


					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_ElvenEars"));
						tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("ElvenEars", skinColor));
					}
					else if (randomResult == 1)
					{
						tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHead_Ears", tempSlotList[headIndex].GetOverlayList()));
					}

					randomResult = Random.Range(0, 2);
					if (randomResult == 0)
					{
						tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
					}
				}

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleEyelash"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleEyelash", Color.black));

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleTorso"));
				int bodyIndex = tempSlotList.Count - 1;
				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleBody01", skinColor));
				} if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleBody02", skinColor));
				}

				tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

				randomResult = Random.Range(0, 4);
				if (randomResult == 0)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 1)
				{
					tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleShirt02", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else if (randomResult == 2)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleTshirt01"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleTshirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}
				else
				{

				}

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleHands", tempSlotList[bodyIndex].GetOverlayList()));

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleInnerMouth"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("InnerMouth"));

				tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleFeet", tempSlotList[bodyIndex].GetOverlayList()));


				randomResult = Random.Range(0, 2);

				if (randomResult == 0)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
					tempSlotList[bodyIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
				}

				randomResult = Random.Range(0, 3);
				if (randomResult == 0)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleShortHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleShortHair01", HairColor));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleLongHair01", HairColor));
				}
				else
				{
					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
					tempSlotList[headIndex].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleLongHair01", HairColor));

					tempSlotList.Add(UMAGlobal.Context.InstantiateSlot("FemaleLongHair01_Module"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(UMAGlobal.Context.InstantiateOverlay("FemaleLongHair01_Module", HairColor));
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
			UMADnaBase humanoidDNA = umaData.umaRecipe.GetDna(UMAUtils.StringToHash("UMADnaHumanoid"));
			buildIndices(humanoidDNA);

			// HACK
			// This is obviosuly terrible! Cache the indices or do a wrapping object!
			humanoidDNA.SetValue(height, Random.Range(0.4f, 0.5f));
			humanoidDNA.SetValue(headSize, Random.Range(0.485f, 0.515f));
			humanoidDNA.SetValue(headWidth, Random.Range(0.4f, 0.6f));

			humanoidDNA.SetValue(neckThickness, Random.Range(0.495f, 0.51f));

			if (umaData.umaRecipe.raceData.raceName == "HumanMale")
			{
				humanoidDNA.SetValue(handsSize, Random.Range(0.485f, 0.515f));
				humanoidDNA.SetValue(feetSize, Random.Range(0.485f, 0.515f));
				humanoidDNA.SetValue(legSeparation, Random.Range(0.4f, 0.6f));
				humanoidDNA.SetValue(waist, 0.5f);
			}
			else
			{
				humanoidDNA.SetValue(handsSize, Random.Range(0.485f, 0.515f));
				humanoidDNA.SetValue(feetSize, Random.Range(0.485f, 0.515f));
				humanoidDNA.SetValue(legSeparation, Random.Range(0.485f, 0.515f));
				humanoidDNA.SetValue(waist, Random.Range(0.3f, 0.8f));
			}

			humanoidDNA.SetValue(armLength, Random.Range(0.485f, 0.515f));
			humanoidDNA.SetValue(forearmLength, Random.Range(0.485f, 0.515f));
			humanoidDNA.SetValue(armWidth, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(forearmWidth, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(upperMuscle, Random.Range(0.0f, 1.0f));
			humanoidDNA.SetValue(upperWeight, Random.Range(-0.2f, 0.2f) + humanoidDNA.GetValue(upperMuscle));
			if (humanoidDNA.GetValue(upperWeight) > 1.0) { humanoidDNA.SetValue(upperWeight, 1.0f); }
			if (humanoidDNA.GetValue(upperWeight) < 0.0) { humanoidDNA.SetValue(upperWeight, 0.0f); }

			humanoidDNA.SetValue(lowerMuscle, Random.Range(-0.2f, 0.2f) + humanoidDNA.GetValue(upperMuscle));
			if (humanoidDNA.GetValue(lowerMuscle) > 1.0) { humanoidDNA.SetValue(lowerMuscle, 1.0f); }
			if (humanoidDNA.GetValue(lowerMuscle) < 0.0) { humanoidDNA.SetValue(lowerMuscle, 0.0f); }

			humanoidDNA.SetValue(lowerWeight, Random.Range(-0.1f, 0.1f) + humanoidDNA.GetValue(upperWeight));
			if (humanoidDNA.GetValue(lowerWeight) > 1.0) { humanoidDNA.SetValue(lowerWeight, 1.0f); }
			if (humanoidDNA.GetValue(lowerWeight) < 0.0) { humanoidDNA.SetValue(lowerWeight, 0.0f); }

			humanoidDNA.SetValue(belly, humanoidDNA.GetValue(upperWeight) * Random.Range(0.0f,1.0f));
			humanoidDNA.SetValue(legsSize, Random.Range(0.45f, 0.6f));
			humanoidDNA.SetValue(gluteusSize, Random.Range(0.4f, 0.6f));

			humanoidDNA.SetValue(earsSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(earsPosition, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(earsRotation, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(noseSize, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(noseCurve, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(noseWidth, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(noseInclination, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(nosePosition, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(nosePronounced, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(noseFlatten, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(chinSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(chinPronounced, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(chinPosition, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(mandibleSize, Random.Range(0.45f, 0.52f));
			humanoidDNA.SetValue(jawsSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(jawsPosition, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(cheekSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(cheekPosition, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(lowCheekPronounced, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(lowCheekPosition, Random.Range(0.3f, 0.8f));

			humanoidDNA.SetValue(foreheadSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(foreheadPosition, Random.Range(0.15f, 0.65f));

			humanoidDNA.SetValue(lipsSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(mouthSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(eyeRotation, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(eyeSize, Random.Range(0.3f, 0.8f));
			humanoidDNA.SetValue(breastSize, Random.Range(0.3f, 0.8f));
		}

		protected virtual void GenerateUMAShapes()
		{
			int dnaType = UMAUtils.StringToHash("UMADnaHumanoid");
			UMADnaBase umaDna = umaData.umaRecipe.GetDna(dnaType);
			if (umaDna ==  null)
			{
				//umaDna = new UMADnaHumanoid();
				umaDna = UMAGlobal.Context.InstantiateDNA(dnaType);
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
				umaRecipe.SetRace(UMAGlobal.Context.GetRace(race.raceID));
			}
			else
			{
				if (sex == 0)
				{
					umaRecipe.SetRace(UMAGlobal.Context.GetRace("HumanMale"));
				}
				else
				{
					umaRecipe.SetRace(UMAGlobal.Context.GetRace("HumanFemale"));
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
			umaData.AddAdditionalRecipes(additionalRecipes, UMAContextBase.FindInstance());
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
					umaRecipe.SetRace(UMAGlobal.Context.GetRace(race.raceID));
				}
				else
				{
					if (Random.value < 0.5f)
					{
						umaRecipe.SetRace(UMAGlobal.Context.GetRace("HumanMale"));
					}
					else
					{
						umaRecipe.SetRace(UMAGlobal.Context.GetRace("HumanFemale"));
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
