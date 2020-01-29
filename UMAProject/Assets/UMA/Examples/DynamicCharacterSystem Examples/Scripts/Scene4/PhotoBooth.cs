//This is UnityEditor only for the time being...
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.CharacterSystem;

namespace UMA.CharacterSystem.Examples
{
	//UPDATED For CharacterSystem.
	//Takes photos of the character based on the Wardrobe slots.
	//HUGE MemoryLeak or infinite loop in this somewhere...
	public class PhotoBooth : MonoBehaviour
	{

		public RenderTexture bodyRenderTexture;
		public RenderTexture outfitRenderTexture;
		public RenderTexture headRenderTexture;
		public RenderTexture chestRenderTexture;
		public RenderTexture handsRenderTexture;
		public RenderTexture legsRenderTexture;
		public RenderTexture feetRenderTexture;
		public DynamicCharacterAvatar avatarToPhoto;

		public enum renderTextureOpts { BodyRenderTexture, OutfitRenderTexture, HeadRenderTexture, ChestRenderTexture, HandsRenderTexture, LegsRenderTexture, FeetRenderTexture };
		public string photoName;

		public bool freezeAnimation;
		public float animationFreezeFrame = 1.8f;
		public string destinationFolder;//non-serialized?
		[Tooltip("If true will automatically take all possible wardrobe photos for the current character. Otherwise photographs character in its current state.")]
		public bool autoPhotosEnabled = true;
		[Tooltip("In mnual mode use this to select the RenderTexture you wish to Photo")]
		public renderTextureOpts textureToPhoto = renderTextureOpts.BodyRenderTexture;
		[Tooltip("If true will dim everything but the target wardrobe recipe (AutoPhotosEnabled only)")]
		public bool dimAllButTarget = false;
		public Color dimToColor = new Color(0, 0, 0, 1);
		public Color dimToMetallic = new Color(0, 0, 0, 1);
		[Tooltip("If true will set the colors for the target wardrobe recipe to the neuttal color (AutoPhotosEnabled only)")]
		public bool neutralizeTargetColors = false;
		public Color neutralizeToColor = new Color(1, 1, 1, 1);
		public Color neutralizeToMetallic = new Color(0, 0, 0, 1);
		[Tooltip("If true will attempt to find an underwear wardrobe slot to apply when taking the base race photo (AutoPhotosEnabled only)")]
		public bool addUnderwearToBasePhoto = true;
		public bool overwriteExistingPhotos = true;
		public bool doingTakePhoto = false;
		bool canTakePhoto = true;

		RenderTexture renderTextureToUse;
		List<UMATextRecipe> wardrobeRecipeToPhoto = new List<UMATextRecipe>();
		Dictionary<int, Dictionary<int, Color>> originalColors = new Dictionary<int, Dictionary<int, Color>>();

		bool basePhotoTaken;

		void Start()
		{
			destinationFolder = "";
		}

		public void TakePhotos()
		{
			if (doingTakePhoto == false)
			{
				doingTakePhoto = true;
				if (avatarToPhoto == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Avatar to photo in the inspector!");
					doingTakePhoto = false;
					return;
				}
				if (destinationFolder == "")
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set a DestinationFolder in the inspector!");
					doingTakePhoto = false;
					return;
				}
				if (!autoPhotosEnabled)
				{
					bool canPhoto = SetBestRenderTexture();
					if (canPhoto)
					{
						photoName = photoName == "" ? avatarToPhoto.activeRace.name + Path.GetRandomFileName().Replace(".", "") : photoName;
						StartCoroutine(TakePhotoCoroutine());
					}
					else
					{
						if (Debug.isDebugBuild)
							Debug.Log("Unknown RenderTexture Error...");
						doingTakePhoto = false;
						return;
					}
				}
				else
				{
					Dictionary<string, List<UMATextRecipe>> recipesToPhoto = UMAContext.Instance.GetRecipes(avatarToPhoto.activeRace.name);
					basePhotoTaken = false;
					StartCoroutine(TakePhotosCoroutine(recipesToPhoto));
				}
			}
		}

		IEnumerator TakePhotosCoroutine(Dictionary<string, List<UMATextRecipe>> recipesToPhoto)
		{
			yield return null;
			wardrobeRecipeToPhoto.Clear();
			if (!basePhotoTaken)
			{
				if (Debug.isDebugBuild)
					Debug.Log("Gonna take base Photo...");
				avatarToPhoto.ClearSlots();
				if (addUnderwearToBasePhoto)
				{
					foreach (KeyValuePair<string, List<UMATextRecipe>> kp in recipesToPhoto)
					{
						if (kp.Key.IndexOf("Underwear") > -1 || kp.Key.IndexOf("underwear") > -1)
						{
							avatarToPhoto.SetSlot(kp.Value[0]);
						}
					}
				}
				bool renderTextureFound = SetBestRenderTexture("Body");
				if (!renderTextureFound)
				{
					if (Debug.isDebugBuild)
						Debug.Log("No suitable RenderTexture found for Base Photo..");
					doingTakePhoto = false;
					yield break;
				}
				photoName = avatarToPhoto.activeRace.name;
				canTakePhoto = false;
				avatarToPhoto.CharacterUpdated.AddListener(SetCharacterReady);
				avatarToPhoto.BuildCharacter(true);
				while (!canTakePhoto)
				{
					yield return new WaitForSeconds(1f);
				}
				yield return StartCoroutine("TakePhotoCoroutine");
				if (Debug.isDebugBuild)
					Debug.Log("Took base Photo...");
				StopCoroutine("TakePhotoCoroutine");
				//really we need photos for each area of the body (i.e. from each renderTexture/Cam) with no clothes on so we can have a 'None' image
				//so we'll need head, chest, hands, legs, feet, full outfit...
				if (Debug.isDebugBuild)
					Debug.Log("Now taking the Base Photos for body parts...");
				List<string> emptySlotsToPhoto = new List<string> { "Head", "Chest", "Hands", "Legs", "Feet", "Outfit" };
				//if dimming and neutralizing is on do it
				if (dimAllButTarget && neutralizeTargetColors)
				{
					canTakePhoto = false;
					avatarToPhoto.CharacterUpdated.AddListener(SetCharacterReadyAfterColorChange);
					DoDimmingAndNeutralizing();
					while (!canTakePhoto)
					{
						yield return new WaitForSeconds(1f);
					}
				}
				foreach (string emptySlotToPhoto in emptySlotsToPhoto)
				{
					photoName = avatarToPhoto.activeRace.name + emptySlotToPhoto + "None";
					renderTextureFound = SetBestRenderTexture(emptySlotToPhoto);
					if (!renderTextureFound)
					{
						if (Debug.isDebugBuild)
							Debug.Log("No suitable RenderTexture found for " + emptySlotToPhoto + " Photo..");
						continue;
					}
					yield return StartCoroutine("TakePhotoCoroutine");
					if (Debug.isDebugBuild)
						Debug.Log("Took base " + emptySlotToPhoto + " Photo...");
					StopCoroutine("TakePhotoCoroutine");
				}
				basePhotoTaken = true;
				if (Debug.isDebugBuild)
					Debug.Log("Now taking the rest...");
				StartCoroutine(TakePhotosCoroutine(recipesToPhoto));
				yield break;
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.Log("Gonna take other wardrobe photos...");
				if (originalColors.Count > 0)
				{
					avatarToPhoto.CharacterUpdated.RemoveListener(SetCharacterReadyAfterColorChange);
					UndoDimmingAnNeutralizing();
				}
				var numKeys = recipesToPhoto.Count;
				int slotsDone = 0;
				foreach (string wardrobeSlot in recipesToPhoto.Keys)
				{
					if (Debug.isDebugBuild)
						Debug.Log("Gonna take photos for " + wardrobeSlot);
					bool renderTextureFound = SetBestRenderTexture(wardrobeSlot);
					if (!renderTextureFound)
					{
						if (Debug.isDebugBuild)
							Debug.Log("No suitable RenderTexture found for " + wardrobeSlot + " Photo..");
						doingTakePhoto = false;
						yield break;
					}
					foreach (UMATextRecipe wardrobeRecipe in recipesToPhoto[wardrobeSlot])
					{
						if (Debug.isDebugBuild)
							Debug.Log("Gonna take photos for " + wardrobeRecipe.name + " in " + wardrobeSlot);
						photoName = wardrobeRecipe.name;
						var path = destinationFolder + "/" + photoName + ".png";
						if (!overwriteExistingPhotos && File.Exists(Application.dataPath + "/" + path))
						{
							if (Debug.isDebugBuild)
								Debug.Log("Photo already existed for " + photoName + ". Turn on overwrite photos to replace existig ones");
							continue;
						}
						wardrobeRecipeToPhoto.Clear();
						wardrobeRecipeToPhoto.Add(wardrobeRecipe);
						avatarToPhoto.ClearSlots();
						if (addUnderwearToBasePhoto)
						{
							foreach (KeyValuePair<string, List<UMATextRecipe>> kp in recipesToPhoto)
							{
								if (kp.Key.IndexOf("Underwear") > -1 || kp.Key.IndexOf("underwear") > -1)
								{
									avatarToPhoto.SetSlot(kp.Value[0]);
									break;
								}
							}
						}
						avatarToPhoto.SetSlot(wardrobeRecipe);
						canTakePhoto = false;
						avatarToPhoto.CharacterUpdated.AddListener(SetCharacterReady);
						avatarToPhoto.BuildCharacter(true);
						while (!canTakePhoto)
						{
							if (Debug.isDebugBuild)
								Debug.Log("Waiting to take photo...");
							yield return new WaitForSeconds(1f);
						}
						yield return StartCoroutine("TakePhotoCoroutine");
						StopCoroutine("TakePhotoCoroutine");
					}
					slotsDone++;
					if (slotsDone == numKeys)
					{
						ResetCharacter();
						doingTakePhoto = false;
						StopAllCoroutines();
						yield break;
					}
				}
			}
		}

		private void ResetCharacter()
		{
			if (Debug.isDebugBuild)
				Debug.Log("Doing Final Reset");
			if (originalColors.Count > 0)
			{
				avatarToPhoto.CharacterUpdated.RemoveListener(SetCharacterReadyAfterColorChange);
				UndoDimmingAnNeutralizing();
			}
			if (freezeAnimation)
			{
				avatarToPhoto.umaData.animator.speed = 1f;
				avatarToPhoto.umaData.gameObject.GetComponent<UMA.PoseTools.UMAExpressionPlayer>().enableBlinking = true;
				avatarToPhoto.umaData.gameObject.GetComponent<UMA.PoseTools.UMAExpressionPlayer>().enableSaccades = true;
			}
			avatarToPhoto.LoadDefaultWardrobe();
			avatarToPhoto.BuildCharacter(true);
		}

		public void SetCharacterReady(UMAData umaData)
		{
			avatarToPhoto.CharacterUpdated.RemoveListener(SetCharacterReady);
			if ((dimAllButTarget || neutralizeTargetColors) /*&& wardrobeRecipeToPhoto != null*/)//should we be making it possible to dim the base slot photos too?
			{
				if (originalColors.Count > 0)
				{
					avatarToPhoto.CharacterUpdated.RemoveListener(SetCharacterReadyAfterColorChange);
					UndoDimmingAnNeutralizing();//I think this is causing character updates maybe?
				}
				avatarToPhoto.CharacterUpdated.AddListener(SetCharacterReadyAfterColorChange);
				DoDimmingAndNeutralizing();
			}
			else
			{
				if (freezeAnimation)
				{
					SetAnimationFrame();
				}
				canTakePhoto = true;
			}
		}
		public void SetCharacterReadyAfterColorChange(UMAData umaData)
		{
			avatarToPhoto.CharacterUpdated.RemoveListener(SetCharacterReadyAfterColorChange);
			if (freezeAnimation)
			{
				SetAnimationFrame();
			}
			canTakePhoto = true;
		}

		private void SetAnimationFrame()
		{
			var thisAnimatonClip = avatarToPhoto.umaData.animationController.animationClips[0];
			avatarToPhoto.umaData.animator.Play(thisAnimatonClip.name, 0, animationFreezeFrame);
			avatarToPhoto.umaData.animator.speed = 0f;
			avatarToPhoto.umaData.gameObject.GetComponent<UMA.PoseTools.UMAExpressionPlayer>().enableBlinking = false;
			avatarToPhoto.umaData.gameObject.GetComponent<UMA.PoseTools.UMAExpressionPlayer>().enableSaccades = false;
		}

		IEnumerator TakePhotoCoroutine()
		{
			canTakePhoto = false;
			if (Debug.isDebugBuild)
				Debug.Log("Taking Photo...");
			if (!autoPhotosEnabled)
			{
				if (dimAllButTarget && neutralizeTargetColors)
				{
					canTakePhoto = false;
					avatarToPhoto.CharacterUpdated.AddListener(SetCharacterReadyAfterColorChange);
					wardrobeRecipeToPhoto.Clear();
					foreach (KeyValuePair<string, UMATextRecipe> kp in avatarToPhoto.WardrobeRecipes)
					{
						wardrobeRecipeToPhoto.Add(kp.Value);
					}
					DoDimmingAndNeutralizing();
					while (!canTakePhoto)
					{
						yield return new WaitForSeconds(1f);
					}
				}
			}
			var path = destinationFolder + "/" + photoName + ".png";
			if (!overwriteExistingPhotos && File.Exists(path))
			{
				if (Debug.isDebugBuild)
					Debug.Log("could not overwrite existing Photo. Turn on Overwrite Existing Photos if you want to allow this");
				canTakePhoto = true;
				doingTakePhoto = false;
				if (!autoPhotosEnabled)
				{
					ResetCharacter();
				}
				yield return true;
			}
			else
			{
				Texture2D texToSave = new Texture2D(renderTextureToUse.width, renderTextureToUse.height);
				RenderTexture prev = RenderTexture.active;
				RenderTexture.active = renderTextureToUse;
				texToSave.ReadPixels(new Rect(0, 0, renderTextureToUse.width, renderTextureToUse.height), 0, 0, true);
				texToSave.Apply();
				byte[] texToSavePNG = texToSave.EncodeToPNG();
				//path must be inside assets
				File.WriteAllBytes(path, texToSavePNG);
				RenderTexture.active = prev;
				var relativePath = path;
				if (path.StartsWith(Application.dataPath))
				{
					relativePath = "Assets" + path.Substring(Application.dataPath.Length);
				}
				AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUncompressedImport);
				TextureImporter textureImporter = AssetImporter.GetAtPath(relativePath) as TextureImporter;
				textureImporter.textureType = TextureImporterType.Sprite;
				textureImporter.mipmapEnabled = false;
				textureImporter.maxTextureSize = 256;
				AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
				canTakePhoto = true;
				doingTakePhoto = false;
				if (!autoPhotosEnabled)
				{
					ResetCharacter();
				}
				yield return true;
			}
		}

		private void UndoDimmingAnNeutralizing()
		{
			int numSlots = avatarToPhoto.umaData.GetSlotArraySize();
			for (int i = 0; i < numSlots; i++)
			{
				if (avatarToPhoto.umaData.GetSlot(i))
				{
					var thisSlot = avatarToPhoto.umaData.GetSlot(i);
					var thisSlotOverlays = thisSlot.GetOverlayList();
					for (int ii = 0; ii < thisSlotOverlays.Count; ii++)
					{
						if (originalColors.ContainsKey(i))
						{
							if (originalColors[i].ContainsKey(ii))
							{
								thisSlotOverlays[ii].SetColor(0, originalColors[i][ii]);
							}
						}
					}
				}
			}
		}

		private void DoDimmingAndNeutralizing()
		{
			if (wardrobeRecipeToPhoto.Count > 0)
			{
				if (Debug.isDebugBuild)
					Debug.Log("Doing Dimming And Neutralizing for " + wardrobeRecipeToPhoto[0].name);
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.Log("Doing Dimming And Neutralizing for Body shots");
			}
			int numAvatarSlots = avatarToPhoto.umaData.GetSlotArraySize();
			originalColors.Clear();
			List<string> slotsInRecipe = new List<string>();
			List<string> overlaysInRecipe = new List<string>();
			foreach (UMATextRecipe utr in wardrobeRecipeToPhoto)
			{
				UMAData.UMARecipe tempLoadedRecipe = new UMAData.UMARecipe();
				utr.Load(tempLoadedRecipe, avatarToPhoto.context);
				foreach (SlotData slot in tempLoadedRecipe.slotDataList)
				{
					if (slot != null)
					{
						slotsInRecipe.Add(slot.asset.name);
						foreach (OverlayData wOverlay in slot.GetOverlayList())
						{
							if (!overlaysInRecipe.Contains(wOverlay.asset.name))
								overlaysInRecipe.Add(wOverlay.asset.name);
						}
					}
				}
			}
			//Deal with skin color first if we are dimming
			if (dimAllButTarget)
			{
				OverlayColorData[] sharedColors = avatarToPhoto.umaData.umaRecipe.sharedColors;
				for (int i = 0; i < sharedColors.Length; i++)
				{
					if (sharedColors[i].name == "Skin" || sharedColors[i].name == "skin")
					{
						sharedColors[i].color = dimToColor;
						if (sharedColors[i].channelAdditiveMask.Length >= 3)
						{
							sharedColors[i].channelAdditiveMask[2] = dimToMetallic;
						}
					}
				}
			}
			for (int i = 0; i < numAvatarSlots; i++)
			{
				if (avatarToPhoto.umaData.GetSlot(i) != null)
				{
					var overlaysInAvatarSlot = avatarToPhoto.umaData.GetSlot(i).GetOverlayList();
					if (slotsInRecipe.Contains(avatarToPhoto.umaData.GetSlot(i).asset.name))
					{
						if (neutralizeTargetColors || dimAllButTarget)
						{
							for (int ii = 0; ii < overlaysInAvatarSlot.Count; ii++)
							{
								//there is a problem here where if the recipe also contains replacement body slots (like my toon CapriPants_LEGS) these also get set to white
								//so we need to check if the overlay contains any body part names I think
								bool overlayIsBody = false;
								var thisOverlayName = overlaysInAvatarSlot[ii].asset.name;
								if (thisOverlayName.IndexOf("Face", StringComparison.OrdinalIgnoreCase) > -1
									// || thisOverlayName.IndexOf("Torso", StringComparison.OrdinalIgnoreCase) > -1
									|| thisOverlayName.IndexOf("Arms", StringComparison.OrdinalIgnoreCase) > -1
									|| thisOverlayName.IndexOf("Hands", StringComparison.OrdinalIgnoreCase) > -1
									|| thisOverlayName.IndexOf("Legs", StringComparison.OrdinalIgnoreCase) > -1
									|| thisOverlayName.IndexOf("Feet", StringComparison.OrdinalIgnoreCase) > -1
									|| thisOverlayName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) > -1)
								{
									overlayIsBody = true;
								}
								if (overlaysInRecipe.Contains(overlaysInAvatarSlot[ii].asset.name) && overlayIsBody == false)
								{
									if (!originalColors.ContainsKey(i))
									{
										originalColors.Add(i, new Dictionary<int, Color>());
									}
									if (!originalColors[i].ContainsKey(ii))
										originalColors[i].Add(ii, overlaysInAvatarSlot[ii].colorData.color);
									overlaysInAvatarSlot[ii].colorData.color = neutralizeToColor;
									if (overlaysInAvatarSlot[ii].colorData.channelAdditiveMask.Length >= 3)
									{
										overlaysInAvatarSlot[ii].colorData.channelAdditiveMask[2] = neutralizeToMetallic;
									}
								}
								else
								{
									if (dimAllButTarget)
									{
										if (!originalColors.ContainsKey(i))
										{
											originalColors.Add(i, new Dictionary<int, Color>());
										}
										if (!originalColors[i].ContainsKey(ii))
											originalColors[i].Add(ii, overlaysInAvatarSlot[ii].colorData.color);
										overlaysInAvatarSlot[ii].colorData.color = dimToColor;
										if (overlaysInAvatarSlot[ii].colorData.channelAdditiveMask.Length >= 3)
										{
											overlaysInAvatarSlot[ii].colorData.channelAdditiveMask[2] = dimToMetallic;
										}
									}
								}
							}
						}
					}
					else
					{
						if (dimAllButTarget)
						{
							for (int ii = 0; ii < overlaysInAvatarSlot.Count; ii++)
							{
								if (!overlaysInRecipe.Contains(overlaysInAvatarSlot[ii].asset.name))
								{
									if (!originalColors.ContainsKey(i))
									{
										originalColors.Add(i, new Dictionary<int, Color>());
									}
									if (!originalColors[i].ContainsKey(ii))
										originalColors[i].Add(ii, overlaysInAvatarSlot[ii].colorData.color);
									overlaysInAvatarSlot[ii].colorData.color = dimToColor;
									if (overlaysInAvatarSlot[ii].colorData.channelAdditiveMask.Length >= 3)
									{
										overlaysInAvatarSlot[ii].colorData.channelAdditiveMask[2] = dimToMetallic;
									}
								}
							}
						}
					}
				}
			}
			avatarToPhoto.umaData.dirty = false;
			avatarToPhoto.umaData.Dirty(false, true, false);
		}

		private bool SetBestRenderTexture(string wardrobeSlot = "")
		{
			if (wardrobeSlot == "Body" || (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.BodyRenderTexture))
			{
				if (bodyRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Body Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = bodyRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Face", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Head", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Helmet", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Complexion", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Eyebrows", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Beard", StringComparison.OrdinalIgnoreCase) > -1
						|| wardrobeSlot.IndexOf("Ears", StringComparison.OrdinalIgnoreCase) > -1
						|| (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.HeadRenderTexture))
			{

				if (headRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Head Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = headRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Shoulders", StringComparison.OrdinalIgnoreCase) > -1
			|| wardrobeSlot.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) > -1
			|| wardrobeSlot.IndexOf("Arms", StringComparison.OrdinalIgnoreCase) > -1
			|| (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.ChestRenderTexture)
			)
			{
				if (chestRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Chest Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = chestRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Hands", StringComparison.OrdinalIgnoreCase) > -1 || (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.HandsRenderTexture))
			{
				if (handsRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Hands Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = handsRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Waist", StringComparison.OrdinalIgnoreCase) > -1
			|| wardrobeSlot.IndexOf("Legs", StringComparison.OrdinalIgnoreCase) > -1
			|| (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.LegsRenderTexture))
			{
				if (legsRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Legs Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = legsRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Feet", StringComparison.OrdinalIgnoreCase) > -1 || (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.FeetRenderTexture))
			{
				if (feetRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Feet Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = feetRenderTexture;
					return true;
				}
			}
			else if (wardrobeSlot.IndexOf("Outfit", StringComparison.OrdinalIgnoreCase) > -1
				|| wardrobeSlot.IndexOf("Underwear", StringComparison.OrdinalIgnoreCase) > -1
				|| (!autoPhotosEnabled && textureToPhoto == renderTextureOpts.OutfitRenderTexture))
			{
				if (outfitRenderTexture == null)
				{
					if (Debug.isDebugBuild)
						Debug.Log("You need to set the Outfit Render Texture in the inspector!");
					return false;
				}
				else
				{
					renderTextureToUse = outfitRenderTexture;
					return true;
				}
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.Log("No suitable render texture found for " + wardrobeSlot + " using Body rendertexture");
				renderTextureToUse = bodyRenderTexture;
				return true;
			}

		}

	}

}
#endif
