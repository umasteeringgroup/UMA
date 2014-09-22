using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

public class UMACrowd : MonoBehaviour
{
	public UMACrowdRandomSet[] randomPool;
	public UMAGeneratorBase generator;
	public UMAData umaData;
	public SlotLibrary slotLibrary;
	public OverlayLibrary overlayLibrary;
	public RaceLibrary raceLibrary;
	public RuntimeAnimatorController animationController;

	public float atlasResolutionScale;
	public bool generateUMA;
	public bool generateLotsUMA;
	public Vector2 umaCrowdSize;
	public bool randomDna;

	public float space = 1;
	public Transform zeroPoint;

	private Transform tempUMA;
	private int X;
	private int Y;
	private float umaTimer;
	public string[] keywords;

	public UMADataEvent CharacterCreated;
	public UMADataEvent CharacterDestroyed;
	public UMADataEvent CharacterUpdated;

	void Awake()
	{
		if (space == 0) space = 1;
		string tempVersion = Application.unityVersion;
		tempVersion = tempVersion.Substring(0, 3);
	}

	void Update()
	{
		if (generateLotsUMA)
		{
			if (generator.IsIdle())
			{
				GenerateOneUMA();
				umaData.OnCharacterUpdated += new System.Action<UMAData>(umaData_OnUpdated);

				X = X + 1;
				if (X >= umaCrowdSize.x)
				{
					X = 0;
					Y = Y + 1;
				}
				if (Y >= umaCrowdSize.y)
				{
					generateLotsUMA = false;
					X = 0;
					Y = 0;
				}

			}
		}

		if (generateUMA)
		{
			GenerateOneUMA();
			generateUMA = false;
		}
	}

	void umaData_OnUpdated(UMAData obj)
	{
		if (obj.cancelled)
		{
			Object.Destroy(obj.gameObject);
		}
		else
		{
			if (zeroPoint)
			{
				tempUMA.position = new Vector3(X * space + zeroPoint.position.x - umaCrowdSize.x * space * 0.5f + 0.5f, zeroPoint.position.y, Y * space + zeroPoint.position.z - umaCrowdSize.y * space * 0.5f + 0.5f);
			}
			else
			{
				tempUMA.position = new Vector3(X * space - umaCrowdSize.x * space * 0.5f + 0.5f, 0, Y * space - umaCrowdSize.y * space * 0.5f + 0.5f);
			}
		}
	}

	private void DefineSlots(UMACrowdRandomSet.CrowdRaceData race)
	{
		float skinTone = Random.Range(0.1f, 0.6f);
		Color skinColor = new Color(skinTone + Random.Range(0.35f, 0.4f), skinTone + Random.Range(0.25f, 0.4f), skinTone + Random.Range(0.35f, 0.4f), 1);
		Color HairColor = new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.5f));
		var keywordsLookup = new HashSet<string>(keywords);
		UMACrowdRandomSet.Apply(umaData, race, skinColor, HairColor, keywordsLookup, slotLibrary, overlayLibrary);
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

			umaData.umaRecipe.slotDataList[0] = slotLibrary.InstantiateSlot("MaleEyes");
			umaData.umaRecipe.slotDataList[0].AddOverlay(overlayLibrary.InstantiateOverlay("EyeOverlay"));
			umaData.umaRecipe.slotDataList[0].AddOverlay(overlayLibrary.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[1] = slotLibrary.InstantiateSlot("MaleFace");

				randomResult = Random.Range(0, 2);

				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHead01", skinColor));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHead02", skinColor));
				}
			}
			else if (randomResult == 1)
			{
				umaData.umaRecipe.slotDataList[1] = slotLibrary.InstantiateSlot("MaleHead_Head");

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHead01", skinColor));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHead02", skinColor));
				}

				umaData.umaRecipe.slotDataList[7] = slotLibrary.InstantiateSlot("MaleHead_Eyes", umaData.umaRecipe.slotDataList[1].GetOverlayList());
				umaData.umaRecipe.slotDataList[9] = slotLibrary.InstantiateSlot("MaleHead_Mouth", umaData.umaRecipe.slotDataList[1].GetOverlayList());

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[10] = slotLibrary.InstantiateSlot("MaleHead_PigNose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
					umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHead_PigNose", skinColor));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[10] = slotLibrary.InstantiateSlot("MaleHead_Nose", umaData.umaRecipe.slotDataList[1].GetOverlayList());
				}

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					umaData.umaRecipe.slotDataList[8] = slotLibrary.InstantiateSlot("MaleHead_ElvenEars");
					umaData.umaRecipe.slotDataList[8].AddOverlay(overlayLibrary.InstantiateOverlay("ElvenEars", skinColor));
				}
				else if (randomResult == 1)
				{
					umaData.umaRecipe.slotDataList[8] = slotLibrary.InstantiateSlot("MaleHead_Ears", umaData.umaRecipe.slotDataList[1].GetOverlayList());
				}
			}


			randomResult = Random.Range(0, 3);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHair01", HairColor * 0.25f));
			}
			else if (randomResult == 1)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleHair02", HairColor * 0.25f));
			}
			else
			{

			}


			randomResult = Random.Range(0, 4);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
			}
			else if (randomResult == 1)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
			}
			else if (randomResult == 2)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
			}
			else
			{

			}



			//Extra beard composition
			randomResult = Random.Range(0, 4);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard01", HairColor * 0.15f));
			}
			else if (randomResult == 1)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard02", HairColor * 0.15f));
			}
			else if (randomResult == 2)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBeard03", HairColor * 0.15f));
			}
			else
			{

			}

			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleEyebrow01", HairColor * 0.05f));
			}
			else
			{
				umaData.umaRecipe.slotDataList[1].AddOverlay(overlayLibrary.InstantiateOverlay("MaleEyebrow02", HairColor * 0.05f));
			}

			umaData.umaRecipe.slotDataList[2] = slotLibrary.InstantiateSlot("MaleTorso");

			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[2].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBody01", skinColor));
			}
			else
			{
				umaData.umaRecipe.slotDataList[2].AddOverlay(overlayLibrary.InstantiateOverlay("MaleBody02", skinColor));
			}


			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[2].AddOverlay(overlayLibrary.InstantiateOverlay("MaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}

			umaData.umaRecipe.slotDataList[3] = slotLibrary.InstantiateSlot("MaleHands", umaData.umaRecipe.slotDataList[2].GetOverlayList());

			umaData.umaRecipe.slotDataList[4] = slotLibrary.InstantiateSlot("MaleInnerMouth");
			umaData.umaRecipe.slotDataList[4].AddOverlay(overlayLibrary.InstantiateOverlay("InnerMouth"));


			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaData.umaRecipe.slotDataList[5] = slotLibrary.InstantiateSlot("MaleLegs", umaData.umaRecipe.slotDataList[2].GetOverlayList());
				umaData.umaRecipe.slotDataList[2].AddOverlay(overlayLibrary.InstantiateOverlay("MaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}
			else
			{
				umaData.umaRecipe.slotDataList[5] = slotLibrary.InstantiateSlot("MaleJeans01");
				umaData.umaRecipe.slotDataList[5].AddOverlay(overlayLibrary.InstantiateOverlay("MaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}

			umaData.umaRecipe.slotDataList[6] = slotLibrary.InstantiateSlot("MaleFeet", umaData.umaRecipe.slotDataList[2].GetOverlayList());
		}
		else if (umaData.umaRecipe.raceData.raceName == "HumanFemale")
		{
			int randomResult = 0;
			//Female Avatar

			//Example of dynamic list
			List<SlotData> tempSlotList = new List<SlotData>();

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleEyes"));
			tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("EyeOverlay"));
			tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("EyeOverlayAdjust", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

			int headIndex = 0;

			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{

				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleFace"));
				headIndex = tempSlotList.Count - 1;
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleHead01", skinColor));
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
				}
			}
			else if (randomResult == 1)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_Head"));
				headIndex = tempSlotList.Count - 1;
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleHead01", skinColor));
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleEyebrow01", new Color(0.125f, 0.065f, 0.065f, 1.0f)));

				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_Eyes", tempSlotList[headIndex].GetOverlayList()));
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_Mouth", tempSlotList[headIndex].GetOverlayList()));
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_Nose", tempSlotList[headIndex].GetOverlayList()));


				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_ElvenEars"));
					tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("ElvenEars", skinColor));
				}
				else if (randomResult == 1)
				{
					tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHead_Ears", tempSlotList[headIndex].GetOverlayList()));
				}

				randomResult = Random.Range(0, 2);
				if (randomResult == 0)
				{
					tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleLipstick01", new Color(skinColor.r + Random.Range(0.0f, 0.3f), skinColor.g, skinColor.b + Random.Range(0.0f, 0.2f), 1)));
				}
			}

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleEyelash"));
			tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleEyelash", Color.black));

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleTorso"));
			int bodyIndex = tempSlotList.Count - 1;
			randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleBody01", skinColor));
			} if (randomResult == 1)
			{
				tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleBody02", skinColor));
			}

			tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleUnderwear01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));

			randomResult = Random.Range(0, 4);
			if (randomResult == 0)
			{
				tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleShirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}
			else if (randomResult == 1)
			{
				tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleShirt02", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}
			else if (randomResult == 2)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleTshirt01"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleTshirt01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}
			else
			{

			}

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleHands", tempSlotList[bodyIndex].GetOverlayList()));

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleInnerMouth"));
			tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("InnerMouth"));

			tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleFeet", tempSlotList[bodyIndex].GetOverlayList()));


			randomResult = Random.Range(0, 2);

			if (randomResult == 0)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
			}
			else if (randomResult == 1)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleLegs", tempSlotList[bodyIndex].GetOverlayList()));
				tempSlotList[bodyIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleJeans01", new Color(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f), 1)));
			}

			randomResult = Random.Range(0, 3);
			if (randomResult == 0)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleShortHair01", tempSlotList[headIndex].GetOverlayList()));
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleShortHair01", HairColor));
			}
			else if (randomResult == 1)
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleLongHair01", HairColor));
			}
			else
			{
				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleLongHair01", tempSlotList[headIndex].GetOverlayList()));
				tempSlotList[headIndex].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleLongHair01", HairColor));

				tempSlotList.Add(slotLibrary.InstantiateSlot("FemaleLongHair01_Module"));
				tempSlotList[tempSlotList.Count - 1].AddOverlay(overlayLibrary.InstantiateOverlay("FemaleLongHair01_Module", HairColor));
			}

			umaData.SetSlots(tempSlotList.ToArray());
		}
	}

	protected virtual void SetUMAData()
	{
		umaData.atlasResolutionScale = atlasResolutionScale;
		umaData.OnCharacterUpdated += myColliderUpdateMethod;
	}

	void myColliderUpdateMethod(UMAData umaData)
	{
		CapsuleCollider tempCollider = umaData.umaRoot.gameObject.GetComponent("CapsuleCollider") as CapsuleCollider;
		if (tempCollider)
		{
			UMADnaHumanoid umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			tempCollider.height = (umaDna.height + 0.5f) * 2 + 0.1f;
			tempCollider.center = new Vector3(0, tempCollider.height * 0.5f - 0.04f, 0);
		}
	}

	protected virtual void GenerateUMAShapes()
	{
		UMADnaHumanoid umaDna = new UMADnaHumanoid();
		umaData.umaRecipe.AddDna(umaDna);

		if (randomDna)
		{

			umaDna.height = Random.Range(0.3f, 0.5f);
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

			umaDna.belly = umaDna.upperWeight;
			umaDna.legsSize = Random.Range(0.4f, 0.6f);
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
	}

	void GenerateOneUMA()
	{
		var newGO = new GameObject("Generated Character");
		newGO.transform.parent = transform;
		var umaDynamicAvatar = newGO.AddComponent<UMADynamicAvatar>();
		umaDynamicAvatar.Initialize();
		umaData = umaDynamicAvatar.umaData;
		umaData.CharacterCreated = CharacterCreated;
		umaData.CharacterDestroyed = CharacterDestroyed;
		umaData.CharacterUpdated = CharacterUpdated;
		umaDynamicAvatar.umaGenerator = generator;
		umaData.umaGenerator = generator;
		var umaRecipe = umaDynamicAvatar.umaData.umaRecipe;
		UMACrowdRandomSet.CrowdRaceData race = null;

		if (randomPool != null && randomPool.Length > 0)
		{
			int randomResult = Random.Range(0, randomPool.Length);
			race = randomPool[randomResult].data;
			umaRecipe.SetRace(raceLibrary.GetRace(race.raceID));
		}
		else
		{
			int randomResult = Random.Range(0, 2);
			if (randomResult == 0)
			{
				umaRecipe.SetRace(raceLibrary.GetRace("HumanMale"));
			}
			else
			{
				umaRecipe.SetRace(raceLibrary.GetRace("HumanFemale"));
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

		GenerateUMAShapes();

		if (animationController != null)
		{
			umaDynamicAvatar.animationController = animationController;
		}
		umaDynamicAvatar.UpdateNewRace();
		umaDynamicAvatar.umaData.myRenderer.enabled = false;
		tempUMA = newGO.transform;

		if (zeroPoint)
		{
			tempUMA.position = new Vector3(zeroPoint.position.x, zeroPoint.position.y, zeroPoint.position.z);
		}
		else
		{
			tempUMA.position = new Vector3(0, 0, 0);
		}
	}
}
