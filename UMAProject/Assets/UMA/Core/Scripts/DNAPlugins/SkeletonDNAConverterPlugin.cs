using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA.CharacterSystem;

namespace UMA
{
	public class SkeletonDNAConverterPlugin : DynamicDNAPlugin
	{
		#region FIELDS

		[SerializeField]
		private List<SkeletonModifier> _skeletonModifiers = new List<SkeletonModifier>();

		#endregion

		#region PUBLIC PROPERTIES

		public List<SkeletonModifier> skeletonModifiers
		{
			get { return _skeletonModifiers; }
			set { _skeletonModifiers = value; }
		}

		#endregion

		#region PUBLIC METHODS

		public void AddModifier(SkeletonModifier modifier)
		{
			_skeletonModifiers.Add(modifier);
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN METHODS PROPERTIES

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
		/// <summary>
		/// Apply the modifiers using the given dna (determined by the typehash)
		/// </summary>
		/// <param name="umaData"></param>
		/// <param name="skeleton"></param>
		/// <param name="dnaTypeHash"></param>
		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			var umaDna = umaData.GetDna(dnaTypeHash);
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			if (masterWeightCalc == 0f)
				return;
			for (int i = 0; i < _skeletonModifiers.Count; i++)
			{
				_skeletonModifiers[i].umaDNA = umaDna;

				var thisHash = (_skeletonModifiers[i].hash != 0) ? _skeletonModifiers[i].hash : UMAUtils.StringToHash(_skeletonModifiers[i].hashName);

				//check skeleton has the bone we want to change
				if (!skeleton.HasBone(thisHash))
				{
					Debug.LogWarning("You were trying to apply skeleton modifications to a bone that didn't exist (" + _skeletonModifiers[i].hashName + ") on " + umaData.gameObject.name);
					continue;
				}

				//With these ValueX.x is the calculated value and ValueX.y is min and ValueX.z is max
				var thisValueX = _skeletonModifiers[i].CalculateValueX(umaDna);
				var thisValueY = _skeletonModifiers[i].CalculateValueY(umaDna);
				var thisValueZ = _skeletonModifiers[i].CalculateValueZ(umaDna);

				if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Position)
				{
					skeleton.SetPositionRelative(thisHash,
						new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)), masterWeightCalc);
				}
				else if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Rotation)
				{
					skeleton.SetRotationRelative(thisHash,
						Quaternion.Euler(new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z))), masterWeightCalc);
				}
				else if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
				{
					//If there are two sets of skeletonModifiers and both are at 50% it needs to apply them both but the result should be cumulative
					//so we need to work out the difference this one is making, weight that and add it to the current scale of the bone
					var scale = new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z));
					//we cant use val.value here because the initial values always need to be applied
					var defaultVal = SkeletonModifier.skelAddDefaults[SkeletonModifier.SkeletonPropType.Scale].x;
					var scaleDiff = new Vector3(scale.x - defaultVal,
						scale.y - defaultVal,
						scale.z - defaultVal);
					var weightedScaleDiff = scaleDiff * masterWeightCalc;
					var fullScale = skeleton.GetScale(_skeletonModifiers[i].hash) + weightedScaleDiff;
					skeleton.SetScale(thisHash, fullScale);
				}

			}
		}

		#endregion

		#region DYNAMICDNAPLUGIN EDITOR OVERRIDES

#if UNITY_EDITOR

		public override string PluginHelp
		{
			get { return "Skeleton DNA Converters use dna values to transform the bones in an avatars skeleton."; }
		}

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

#pragma warning disable 618 //disable obsolete warning

		/// <summary>
		/// Imports SkeletomModifiers from another object into this SkeletonModifiersDNAConverterPlugin
		/// </summary>
		/// <param name="pluginToImport">You can import another SkeletonModifiersDNAConverterPlugin or settings from a legacy DynamicDNAConverterBehaviour prefab</param>
		/// <param name="importMethod">Use 0 to Add to the existing list, 1 to Replace the existing list, 2 to only Overwrite anything in the existing list with matching modifiers in the incoming list, or 3 to Overwrite any existing entries and then Add any entries that were not already in the existing list</param>
		/// <returns>True if any settings were imported successfully, otherwise false</returns>
		public override bool ImportSettings(Object pluginToImport, int importMethod)
		{
			List<SkeletonModifier> importedSkeletonModifiers = new List<SkeletonModifier>();
			bool isLegacy = false;
			if (pluginToImport.GetType() == this.GetType())
				importedSkeletonModifiers = (pluginToImport as SkeletonDNAConverterPlugin)._skeletonModifiers;
			else if(pluginToImport.GetType().IsAssignableFrom(typeof(DynamicDNAConverterController)))
			{
				var skelModPlugs = (pluginToImport as DynamicDNAConverterController).GetPlugins(typeof(SkeletonDNAConverterPlugin));
				if(skelModPlugs.Count > 0)
				{
					importedSkeletonModifiers = (skelModPlugs[0] as SkeletonDNAConverterPlugin)._skeletonModifiers;
				}
			}
			else
			{
				if (typeof(GameObject).IsAssignableFrom(pluginToImport.GetType()))
				{
					var DDCB = (pluginToImport as GameObject).GetComponent<DynamicDNAConverterBehaviour>();
					if(DDCB != null)
					{
						importedSkeletonModifiers = DDCB.skeletonModifiers;
						//hmm this is not always the case because of the backwards compatible property giving us the first found skelModsPlugin aswell
						//so if there is no converter controller, *then* its legacy- 
						//or is it? the user could still assign a controller without upgrading and then try and drag the behaviour in here
						//UMA2.8+ FixDNAPrefabs ConverterController doesn't do this backwards compatibility now
						//if(DDCB.ConverterController == null)
							isLegacy = true;
					}
				}
			}
			if(importedSkeletonModifiers != null)
			{
				// add the modifiers- if the import method is Replace this is a new list
				var currentModifiers = importMethod == 1 ? new List<SkeletonModifier>() : _skeletonModifiers;
				var incomingModifiers = importedSkeletonModifiers;

				List<string> existingDNANames = new List<string>();
				if (DNAAsset != null)
					existingDNANames.AddRange(DNAAsset.Names);
				List<string> missingDNANames = new List<string>();
				//If any dnanames are misisng give the user the option to only overwrite matching dna names
				//or add the missing dna names and continue
				//or cancel
				//string nameToCheck = "";
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
					var usedDNANames = SkeletonModifierUsedDNANames(incomingModifiers[i], isLegacy);
					for(int nc = 0; nc < usedDNANames.Count; nc++)
					{
						if (!existingDNANames.Contains(usedDNANames[nc]) && !missingDNANames.Contains(usedDNANames[nc]))
							missingDNANames.Add(usedDNANames[nc]);
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
					//the inspector will sort this out later
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
							if (importMethod == 2 || importMethod == 3)
							{
								//handle the overwrites
								currentModifiers[ci].valuesX.min = incomingModifiers[i].valuesX.min;
								currentModifiers[ci].valuesX.max = incomingModifiers[i].valuesX.max;
								currentModifiers[ci].valuesX.val.value = incomingModifiers[i].valuesX.val.value;
								//now currentModifiers should only ever have modifyingDNA but incomingModifiers might contain data in  legacy 'modifiers' OR in 'modifyingDNA'
								if (isLegacy)
									ProcessSkelModOverwrites(currentModifiers[ci].valuesX.val.modifyingDNA, incomingModifiers[i].valuesX.val.modifiers, existingDNANames);
								else
									ProcessSkelModOverwrites(currentModifiers[ci].valuesX.val.modifyingDNA, incomingModifiers[i].valuesX.val.modifyingDNA, existingDNANames);

								currentModifiers[ci].valuesY.min = incomingModifiers[i].valuesY.min;
								currentModifiers[ci].valuesY.max = incomingModifiers[i].valuesY.max;
								currentModifiers[ci].valuesY.val.value = incomingModifiers[i].valuesY.val.value;
								if (isLegacy)
									ProcessSkelModOverwrites(currentModifiers[ci].valuesY.val.modifyingDNA, incomingModifiers[i].valuesY.val.modifiers, existingDNANames);
								else
									ProcessSkelModOverwrites(currentModifiers[ci].valuesY.val.modifyingDNA, incomingModifiers[i].valuesY.val.modifyingDNA, existingDNANames);

								currentModifiers[ci].valuesZ.min = incomingModifiers[i].valuesZ.min;
								currentModifiers[ci].valuesZ.max = incomingModifiers[i].valuesZ.max;
								currentModifiers[ci].valuesZ.val.value = incomingModifiers[i].valuesZ.val.value;
								if (isLegacy)
									ProcessSkelModOverwrites(currentModifiers[ci].valuesZ.val.modifyingDNA, incomingModifiers[i].valuesZ.val.modifiers, existingDNANames);
								else
									ProcessSkelModOverwrites(currentModifiers[ci].valuesZ.val.modifyingDNA, incomingModifiers[i].valuesZ.val.modifyingDNA, existingDNANames);
							}
							break;
						}
					}
					if (!existed && importMethod != 2)//if the method is anything other overwrite add the missing modifier
					{
						currentModifiers.Add(new SkeletonModifier(incomingModifiers[i], true));
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
#pragma warning restore 618


#pragma warning disable 618 //disable obsolete warning

		/// <summary>
		/// Converts the legacy incoming values into DNAEvaluators and then overwrites any DNAEvaluators in the current settings with the settings from the incoming DNAEvaluators if the dnaName matches, otherwise adds a new evaluator for the dnaName
		/// </summary>
		/// <param name="existingDNANames">If the dnaName used by a DNAEvaluator in the incoming settings is not found in this list it will not be processed</param>
		private void ProcessSkelModOverwrites(DNAEvaluatorList currentMods, List<SkeletonModifier.spVal.spValValue.spValModifier> incomingMods, List<string> existingDNANames)
		{
			SkeletonModifier.spVal.spValValue tempValValue = new SkeletonModifier.spVal.spValValue();
			tempValValue.modifiers = new List<SkeletonModifier.spVal.spValValue.spValModifier>(incomingMods);
			tempValValue.ConvertToDNAEvaluators();
			ProcessSkelModOverwrites(currentMods, tempValValue.modifyingDNA, existingDNANames);
		}
#pragma warning restore 618

		/// <summary>
		/// overwrites any DNAEvaluators in the current settings with the settings from the incoming DNAEvaluators if the dnaName matches, otherwise adds a new evaluator for the dnaName
		/// </summary>
		/// <param name="existingDNANames">If the dnaName used by a DNAEvaluator in the incoming settings is not found in this list it will not be processed</param>
		private void ProcessSkelModOverwrites(DNAEvaluatorList currentMods, DNAEvaluatorList incomingMods, List<string> existingDNANames)
		{
			for (int i = 0; i < incomingMods.Count; i++)
			{
				if (!existingDNANames.Contains(incomingMods[i].dnaName))
					continue;
				var foundInCurrent = false;
				for (int ci = 0; ci < currentMods.Count; ci++)
				{
					if (currentMods[ci].dnaName == incomingMods[i].dnaName)
					{
						currentMods[ci].calcOption = incomingMods[i].calcOption;
						currentMods[ci].evaluator = new DNAEvaluationGraph(incomingMods[i].evaluator);
						currentMods[ci].multiplier = incomingMods[i].multiplier;
						foundInCurrent = true;
					}
				}
				if (!foundInCurrent)
				{
					currentMods.Add(new DNAEvaluator(incomingMods[i].dnaName, incomingMods[i].evaluator, incomingMods[i].multiplier, incomingMods[i].calcOption));
				}
			}
		}

#endif

		#endregion

		#region PRIVATE METHODS

#pragma warning disable 618 //disable obsolete warning
		/// <summary>
		/// Returns the DNANames used by the given skeleton modifier, 
		/// optionally filtering by a given name, in which case the returned list count will only be greater than zero if the modifier used the name.
		/// This can be used to query if any modifiers were using the given name
		/// </summary>
		/// <returns></returns>
		private List<string> SkeletonModifierUsedDNANames(SkeletonModifier skeletonModifier, bool searchLegacy = false, string dnaName = "")
		{
			List<string> usedNames = new List<string>();
			//names from new _modifyingDNA in the modifiers
			var xNames = skeletonModifier.valuesX.val.modifyingDNA.UsedDNANames;
			var yNames = skeletonModifier.valuesY.val.modifyingDNA.UsedDNANames;
			var zNames = skeletonModifier.valuesZ.val.modifyingDNA.UsedDNANames;
			for (int i = 0; i < xNames.Count; i++)
			{
				if (!usedNames.Contains(xNames[i]) && (dnaName == "" || (!string.IsNullOrEmpty(dnaName) && xNames[i] == dnaName)))
					usedNames.Add(xNames[i]);
			}
			for (int i = 0; i < yNames.Count; i++)
			{
				if (!usedNames.Contains(yNames[i]) && (dnaName == "" || (!string.IsNullOrEmpty(dnaName) && yNames[i] == dnaName)))
					usedNames.Add(yNames[i]);
			}
			for (int i = 0; i < zNames.Count; i++)
			{
				if (!usedNames.Contains(zNames[i]) && (dnaName == "" || (!string.IsNullOrEmpty(dnaName) && zNames[i] == dnaName)))
					usedNames.Add(zNames[i]);
			}
			if (searchLegacy)
			{
				//legacy names
				for (int xi = 0; xi < skeletonModifier.valuesX.val.modifiers.Count; xi++)
				{
					if (!string.IsNullOrEmpty(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName) &&
						dnaName == "" || (!string.IsNullOrEmpty(dnaName) && skeletonModifier.valuesX.val.modifiers[xi].DNATypeName == dnaName))
					{
						if (!usedNames.Contains(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName))
							usedNames.Add(skeletonModifier.valuesX.val.modifiers[xi].DNATypeName);
					}
				}
				for (int yi = 0; yi < skeletonModifier.valuesY.val.modifiers.Count; yi++)
				{
					if (!string.IsNullOrEmpty(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName) &&
						dnaName == "" || (!string.IsNullOrEmpty(dnaName) && skeletonModifier.valuesY.val.modifiers[yi].DNATypeName == dnaName))
					{
						if (!usedNames.Contains(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName))
							usedNames.Add(skeletonModifier.valuesY.val.modifiers[yi].DNATypeName);
					}
				}
				for (int zi = 0; zi < skeletonModifier.valuesZ.val.modifiers.Count; zi++)
				{
					if (!string.IsNullOrEmpty(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName) &&
						dnaName == "" || (!string.IsNullOrEmpty(dnaName) && skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName == dnaName))
					{
						if (!usedNames.Contains(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName))
							usedNames.Add(skeletonModifier.valuesZ.val.modifiers[zi].DNATypeName);
					}
				}
			}
			return usedNames;
		}
#pragma warning restore 618 //restore obsolete warning

		#endregion
	}
}
