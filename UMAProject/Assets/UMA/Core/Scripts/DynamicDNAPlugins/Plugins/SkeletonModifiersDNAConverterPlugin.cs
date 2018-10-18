using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA.CharacterSystem;

namespace UMA
{
	public class SkeletonModifiersDNAConverterPlugin : DynamicDNAPlugin
	{
		[SerializeField]
		private List<DynamicDNAConverterBehaviour.SkeletonModifier> _skeletonModifiers = new List<DynamicDNAConverterBehaviour.SkeletonModifier>();

		public List<DynamicDNAConverterBehaviour.SkeletonModifier> skeletonModifiers
		{
			get { return _skeletonModifiers; }
			set { _skeletonModifiers = value; }
		}
		#region REQUIRED DYNAMICDNAPLUGIN PROPERTIES

		public override string PluginHelp
		{
			get { return "Skeleton DNA Converters use dna values to transform the bones in an avatars skeleton."; }
		}

		#endregion

		#region PUBLIC METHODS

		public void AddModifier(DynamicDNAConverterBehaviour.SkeletonModifier modifier)
		{
			_skeletonModifiers.Add(modifier);
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN METHODS

		/// <summary>
		/// Returns a dictionary of all the dna names in use by the plugin and the entries in its converter list that reference them
		/// </summary>
		/// <returns></returns>
		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i = 0; i < _skeletonModifiers.Count; i++)
				{
					var skelModsUsedNames = SkeletonModifierUsedDNANames(_skeletonModifiers[i]);
					for (int ci = 0; ci < skelModsUsedNames.Count; ci++)
					{
						if (!dict.ContainsKey(skelModsUsedNames[ci]))
							dict.Add(skelModsUsedNames[ci], new List<int>());

						dict[skelModsUsedNames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			var umaDna = umaData.GetDna(dnaTypeHash);
			for (int i = 0; i < _skeletonModifiers.Count; i++)
			{
				_skeletonModifiers[i].umaDNA = umaDna;

				var thisHash = (_skeletonModifiers[i].hash != 0) ? _skeletonModifiers[i].hash : UMAUtils.StringToHash(_skeletonModifiers[i].hashName);
				//With these ValueX.x is the calculated value and ValueX.y is min and ValueX.z is max
				var thisValueX = _skeletonModifiers[i].ValueX;
				var thisValueY = _skeletonModifiers[i].ValueY;
				var thisValueZ = _skeletonModifiers[i].ValueZ;

				//use the overallScaleBoneHash property instead so the user can define the bone that is used here (by default its the 'Position' bone in an UMA Rig)
				/*if (_skeletonModifiers[i].hash == overallScaleBoneHash && _skeletonModifiers[i].property == DynamicDNAConverterBehaviour.SkeletonModifier.SkeletonPropType.Scale)
				{
					var calcVal = thisValueX.x - _skeletonModifiers[i].valuesX.val.value + overallScale;
					overallScaleCalc = Mathf.Clamp(calcVal, thisValueX.y, thisValueX.z);
					skeleton.SetScale(_skeletonModifiers[i].hash, new Vector3(overallScaleCalc, overallScaleCalc, overallScaleCalc));
				}
				else */if (_skeletonModifiers[i].property == DynamicDNAConverterBehaviour.SkeletonModifier.SkeletonPropType.Position)
				{
					skeleton.SetPositionRelative(thisHash,
						new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
				}
				else if (_skeletonModifiers[i].property == DynamicDNAConverterBehaviour.SkeletonModifier.SkeletonPropType.Rotation)
				{
					skeleton.SetRotationRelative(thisHash,
						Quaternion.Euler(new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z))), 1f);
				}
				else if (_skeletonModifiers[i].property == DynamicDNAConverterBehaviour.SkeletonModifier.SkeletonPropType.Scale)
				{
					skeleton.SetScale(thisHash,
						new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
				}

			}
		}

#if UNITY_EDITOR

		public override string[] ImportSettingsMethods
		{
			get
			{
				return new string[]
				{
				"Add",
				"Replace",
				"Overwrite",
				"AddOverwrite"
				};
			}
		}

		public override float GetListFooterHeight
		{
			get
			{
				return 16f + (/*EditorGUIUtility.singleLineHeight +*/ (EditorGUIUtility.standardVerticalSpacing * 2));
			}
		}

		public override bool ImportSettings(Object pluginToImport, int importMethod)
		{
			List<DynamicDNAConverterBehaviour.SkeletonModifier> importedSkeletonModifiers = new List<DynamicDNAConverterBehaviour.SkeletonModifier>();
			if (pluginToImport.GetType() == this.GetType())
				importedSkeletonModifiers = (pluginToImport as SkeletonModifiersDNAConverterPlugin)._skeletonModifiers;
			else
			{
				if (typeof(GameObject).IsAssignableFrom(pluginToImport.GetType()))
				{
					var DDCB = (pluginToImport as GameObject).GetComponent<DynamicDNAConverterBehaviour>();
					if(DDCB != null)
					{
						importedSkeletonModifiers = DDCB.skeletonModifiers;
					}
				}
			}

			if(importedSkeletonModifiers != null)
			{
				//Make sure all the bone hashes are there- OBSOLETE
				//AddDNABoneHashes(DDCB, 0); 
				//now add the modifiers
				var currentModifiers = importMethod != 0 ? _skeletonModifiers : new List<DynamicDNAConverterBehaviour.SkeletonModifier>();
				var incomingModifiers = importedSkeletonModifiers;

				List<string> existingDNANames = new List<string>();
				if (DNAAsset != null)
					existingDNANames.AddRange(DNAAsset.Names);
				List<string> missingDNANames = new List<string>();
				//If any dnanames are misisng give the user the option to only overwrite matching dna names
				//or add the missing dna names and continue
				//or cancel
				string nameToCheck = "";
				bool existed = false;

				//if the names in the dnaAsset get changed during this process we need to save that too
				bool updateDNAAsset = false;

				for (int i = 0; i < incomingModifiers.Count; i++)
				{
					//if the Add method is OverwriteAndAdd we need to check dnanames in all the incoming converters
					//otherwise we only need to check names for incoming converters that have a matching one in the current list
					//was Add = 0  Replace = 1  Overwrite = 2  AddOverwrite = 3
					if (importMethod ==  2)
					{
						existed = false;
						for (int ci = 0; ci < currentModifiers.Count; ci++)
						{
							if ((currentModifiers[ci].hash == incomingModifiers[i].hash) && currentModifiers[ci].property == incomingModifiers[i].property)
							{
								existed = true;
								break;
							}
						}
						if (!existed)
							continue;
					}
					//check x
					for (int vi = 0; vi < incomingModifiers[i].valuesX.val.modifiers.Count; vi++)
					{
						nameToCheck = incomingModifiers[i].valuesX.val.modifiers[vi].DNATypeName;
						if (!string.IsNullOrEmpty(nameToCheck) && !existingDNANames.Contains(nameToCheck) && !missingDNANames.Contains(nameToCheck))
						{
							missingDNANames.Add(nameToCheck);
						}
					}
					//check y
					for (int vi = 0; vi < incomingModifiers[i].valuesY.val.modifiers.Count; vi++)
					{
						nameToCheck = incomingModifiers[i].valuesY.val.modifiers[vi].DNATypeName;
						if (!string.IsNullOrEmpty(nameToCheck) && !existingDNANames.Contains(nameToCheck) && !missingDNANames.Contains(nameToCheck))
						{
							missingDNANames.Add(nameToCheck);
						}
					}
					//check Z
					for (int vi = 0; vi < incomingModifiers[i].valuesZ.val.modifiers.Count; vi++)
					{
						nameToCheck = incomingModifiers[i].valuesZ.val.modifiers[vi].DNATypeName;
						if (!string.IsNullOrEmpty(nameToCheck) && !existingDNANames.Contains(nameToCheck) && !missingDNANames.Contains(nameToCheck))
						{
							missingDNANames.Add(nameToCheck);
						}
					}
				}
				if (missingDNANames.Count > 0 && DNAAsset != null)
				{
					string missingDNAMsg = "";
					if (missingDNANames.Count > 10)
					{
						missingDNAMsg = "There were over 10 missing dna names in this converter compared to the one you want to overwrite from";
					}
					else
					{
						missingDNAMsg = "The following dna names were missing in this converter compared to the one you want to overwrite from: ";
						missingDNAMsg += string.Join(", ", missingDNANames.ToArray());

					}
					missingDNAMsg += ". Please choose how you would like to proceed.";
					//options: "Only Overwrite Existing DNA" "Add Missing DNA" "Cancel"
					var missingDNAOption = EditorUtility.DisplayDialogComplex("Missing DNA in Current Converter", missingDNAMsg, "Only Overwrite Existing DNA", "Add Missing DNA", "Cancel");
					if (missingDNAOption == 2)
						return false;
					else if (missingDNAOption == 1)
					{
						//add the missing names
						var assetNames = new List<string>(DNAAsset.Names);
						assetNames.AddRange(missingDNANames);
						DNAAsset.Names = assetNames.ToArray();
						existingDNANames.AddRange(missingDNANames);
						updateDNAAsset = true;
					}
					//now we just add any settings for DNANames that exist, since the ones we need will have either been added or the user knows they will be skipped
				}
				else if (DNAAsset == null)
				{
					//tell the user that they will need to assign a dna asset to this converter to merge the one they dropped
					//if (EditorUtility.DisplayDialog("Missing DNA Asset", "To overwrite settings in this converter with the ones from the converter you dropped you will need to assign or create a DynamicDNA Asset to this converter.", "Ok"))
					//	return false;
					//Just carry on regardless?
				}
				//if the method is add or overwriteAdd we need to add any missing ones (if the method is replace the list will be empty so everything will get added here)
				for (int i = 0; i < incomingModifiers.Count; i++)
				{
					existed = false;
					for (int ci = 0; ci < currentModifiers.Count; ci++)
					{
						if ((currentModifiers[ci].hash == incomingModifiers[i].hash) && currentModifiers[ci].property == incomingModifiers[i].property)
						{
							existed = true;
							break;
						}
					}
					if (!existed)
					{
						currentModifiers.Add(new DynamicDNAConverterBehaviour.SkeletonModifier(incomingModifiers[i]));
					}
				}
				//now if the method is overwrite or addoverwrite we need to overwrite any existing values
				if (importMethod == 2 || importMethod ==3)
				{
					for (int i = 0; i < incomingModifiers.Count; i++)
					{
						for (int ci = 0; ci < currentModifiers.Count; ci++)
						{
							if ((currentModifiers[ci].hash == incomingModifiers[i].hash) && currentModifiers[ci].property == incomingModifiers[i].property)
							{
								//handle the overwrites
								currentModifiers[ci].valuesX.min = incomingModifiers[i].valuesX.min;
								currentModifiers[ci].valuesX.max = incomingModifiers[i].valuesX.max;
								currentModifiers[ci].valuesX.val.value = incomingModifiers[i].valuesX.val.value;
								ProcessSkelModOverwrites(currentModifiers[ci].valuesX.val.modifiers, incomingModifiers[i].valuesX.val.modifiers, existingDNANames);
								currentModifiers[ci].valuesY.min = incomingModifiers[i].valuesY.min;
								currentModifiers[ci].valuesY.max = incomingModifiers[i].valuesY.max;
								currentModifiers[ci].valuesY.val.value = incomingModifiers[i].valuesY.val.value;
								ProcessSkelModOverwrites(currentModifiers[ci].valuesY.val.modifiers, incomingModifiers[i].valuesY.val.modifiers, existingDNANames);
								currentModifiers[ci].valuesZ.min = incomingModifiers[i].valuesZ.min;
								currentModifiers[ci].valuesZ.max = incomingModifiers[i].valuesZ.max;
								currentModifiers[ci].valuesZ.val.value = incomingModifiers[i].valuesZ.val.value;
								ProcessSkelModOverwrites(currentModifiers[ci].valuesZ.val.modifiers, incomingModifiers[i].valuesZ.val.modifiers, existingDNANames);

								break;
							}
						}
					}
				}
				_skeletonModifiers = currentModifiers;
				EditorUtility.SetDirty(this);
				if (updateDNAAsset)
				{
					EditorUtility.SetDirty(DNAAsset);
				}
				AssetDatabase.SaveAssets();
				return true;
			}
			else
			{
				return false;
			}
		}

		private void ProcessSkelModOverwrites(List<DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier> currentMods, List<DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier> incomingMods, List<string> existingDNANames)
		{
			int modCount = 0;
			DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier tempMods;
			DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier tempNextMods;
			modCount = incomingMods.Count;
			for (int vi = 0; vi < modCount; vi++)
			{
				tempMods = incomingMods[vi];
				if (tempMods.modifier.ToString().IndexOf("DNA") > -1 && existingDNANames.Contains(tempMods.DNATypeName))
				{
					//if this is not the last one it will be in a pair i.e what the dna is multiplied/divided by or added to/subtracted from will be defined on the next line 
					//loop over the currentModifiers[ci].valuesi.val.modifiers and see if there is an existing matching entry
					bool foundInCurrent = false;
					for (int mi = 0; mi < currentMods.Count; mi++)
					{
						if (currentMods[mi].DNATypeName == tempMods.DNATypeName)
						{
							foundInCurrent = true;
							//is there an add/subtract/multiply/divide in temp dnas *following* line?

							//if this is the last line in temp or the following line is also a dna name
							if (vi + 1 == modCount || incomingMods[vi + 1].modifier.ToString().IndexOf("DNA") > -1)
							{
								//if this dna name in the current list is followed by and AddSubtractMultipleDivide operation
								//we need to make that command Multiply by 1 (or delete the line [check in the behaviour that this would be handled right])
								if (mi + 1 != currentMods.Count && currentMods[mi + 1].modifier.ToString().IndexOf("DNA") == -1)
								{
									currentMods[mi + 1].modifier = DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier.spValModifierType.Multiply;
									currentMods[mi + 1].modifierValue = 1f;
								}
							}
							else //this line in temp is followed by an AddSubtractMultipleDivide operation
							{
								tempNextMods = incomingMods[vi + 1];
								//if there is a following AddSubtractMultipleDivide operation in current we need to make its values match the one in temp
								if (mi + 1 != currentMods.Count && currentMods[mi + 1].modifier.ToString().IndexOf("DNA") == -1)
								{
									currentMods[mi + 1].modifier = tempNextMods.modifier;
									currentMods[mi + 1].modifierValue = tempNextMods.modifierValue;
								}
								else
								{
									var newMod = new DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier();
									newMod.modifier = tempNextMods.modifier;
									newMod.modifierValue = tempNextMods.modifierValue;
									//we need to add a line in current that matches the one in temp
									if (mi + 1 != currentMods.Count)//insertAt
										currentMods.Insert(mi + 1, newMod);
									else//add
										currentMods.Add(newMod);
								}
							}
							break;
						}
					}
					if (!foundInCurrent)
					{
						//we need to add this dna entry (and any following addMultiply/subtractdivide opertaion) to current
						var newDNAMod = new DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier();
						newDNAMod.DNATypeName = tempMods.DNATypeName;
						newDNAMod.modifier = tempMods.modifier;
						newDNAMod.modifierValue = tempMods.modifierValue;
						currentMods.Add(newDNAMod);
						//if theres a following operation in temp
						if (vi + 1 != modCount && incomingMods[vi + 1].modifier.ToString().IndexOf("DNA") == -1)
						{
							tempNextMods = incomingMods[vi + 1];
							var newOpMod = new DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier();
							newOpMod.modifier = tempNextMods.modifier;
							newOpMod.modifierValue = tempNextMods.modifierValue;
							currentMods.Add(newOpMod);
						}
					}
				}
			}
		}
#endif

		#endregion

		#region PRIVATE METHODS

		/// <summary>
		/// Returns the DNANames used by the given skeleton modifier, 
		/// optionally filtering by a given name, in which case the returned list count will only be greater than zero if the modifier used the name
		/// </summary>
		/// <returns></returns>
		private List<string> SkeletonModifierUsedDNANames(DynamicDNAConverterBehaviour.SkeletonModifier skeletonModifier, string dnaName = "")
		{
			List<string> usedNames = new List<string>();
			for (int xi = 0; xi < skeletonModifier.valuesX.val.modifiers.Count; xi++)
			{
				if (!string.IsNullOrEmpty(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName) &&
					dnaName == "" || skeletonModifier.valuesX.val.modifiers[xi].DNATypeName == dnaName)
				{
					if (!usedNames.Contains(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName))
					{
						usedNames.Add(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName);
					}
				}
			}
			for (int yi = 0; yi < skeletonModifier.valuesY.val.modifiers.Count; yi++)
			{
				if (!string.IsNullOrEmpty(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName) &&
					dnaName == "" || skeletonModifier.valuesY.val.modifiers[yi].DNATypeName == dnaName)
				{
					if (!usedNames.Contains(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName))
					{
						usedNames.Add(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName);
					}
				}
			}
			for (int zi = 0; zi < skeletonModifier.valuesZ.val.modifiers.Count; zi++)
			{
				if (!string.IsNullOrEmpty(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName) &&
					dnaName == "" || skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName == dnaName)
				{
					if (!usedNames.Contains(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName))
					{
						usedNames.Add(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName);
					}
				}
			}
			return usedNames;
		}

		#endregion
	}
}
