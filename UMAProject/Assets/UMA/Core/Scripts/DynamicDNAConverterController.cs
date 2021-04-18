using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
using UMA.CharacterSystem;

namespace UMA
{
	//The DynamicDNAConverterController manages the list Converters (aka DynamicDNAPlugins) the user has decided to use.
	//It is a Scriptable Object, and as Converters are added to it, it creates instances of those and stores them inside itself
	//this is so all the assets this needs are packaged up with it UMA3 style.
	//This asset reploaces DynamicDNAConverterBehaviour and applies the converters to the avatar
	[System.Serializable]
	public class DynamicDNAConverterController : ScriptableObject, IDNAConverter, IDynamicDNAConverter
	{

		[SerializeField]
		[Tooltip("A DNA Asset defines the names that will be available to the DNA Converters when modifying the Avatar. Often displayed in the UI as 'sliders'. Click the 'Inspect' button to view the assigned asset")]
		private DynamicUMADnaAsset _dnaAsset;
		
		/// <summary>
		/// The List of all the plugins (converters) assigned to this ConverterController
		/// </summary>
		[SerializeField]
		private List<DynamicDNAPlugin> _plugins = new List<DynamicDNAPlugin>();

		[SerializeField]
		[BaseCharacterModifier.Config(true)]//does this stop it drawing the foldout? if so we dont want that here
		[Tooltip("Overall Modifiers apply to ALL characters that use this converter. You use this to make a Female race shorter than a Male race for example. They can change an entire races base scale, height and radius (used for fitting the collider), its mass, and update its bounds.  Its elements can selectively be enabled and are calculated after all other DNA Converters have made changes to the avatar. Usually you only use these once per race, on the base 'Converter Controller' for the race.")]
		private BaseCharacterModifier _overallModifiers = new BaseCharacterModifier();

		/// <summary>
		/// Contains a list of all the dna names used by all the plugins (converters) assigned to this ConverterController
		/// </summary>
		private List<string> _usedDNANames = new List<string>();

#pragma warning disable 649
		//only set in the editor
		[SerializeField]
		[Tooltip("A 'nice name' to use when Categorizing DNASetters in the UI")]
		private string _displayValue;
#pragma warning restore 649

		[System.NonSerialized]
		private List<DynamicDNAPlugin> _applyDNAPrepassPlugins = new List<DynamicDNAPlugin>();
		[System.NonSerialized]
		private List<DynamicDNAPlugin> _applyDNAPlugins = new List<DynamicDNAPlugin>();
		[System.NonSerialized]
		private bool _prepared = false;

		private Dictionary<string, List<UnityAction<string, float>>> _dnaCallbackDelegates = new Dictionary<string, List<UnityAction<string, float>>>();

		#region IDNAConverter IMPLIMENTATION

		public string DisplayValue
		{
			get { return _displayValue; }
		}

		public System.Type DNAType
		{
			get { return typeof(DynamicUMADna); }
		}

		/// <summary>
		/// Returns the dnaTypeHash from the assigned dnaAsset or 0 if no dnaAsset is set
		/// </summary>
		/// <returns></returns>
		public int DNATypeHash
		{
			get
			{
				if (_dnaAsset != null)
					return _dnaAsset.dnaTypeHash;
				else
					Debug.LogWarning(this.name + " did not have a DNA Asset assigned. This is required for DynamicDnaConverterControllers.");
				return 0;
			}
		}

		public DNAConvertDelegate PreApplyDnaAction
		{
			get { return ApplyDNAPrepass; }
		}

		public DNAConvertDelegate ApplyDnaAction
		{
			get { return ApplyDNA; }
		}

		//Prepare should be here too

		#endregion

		#region IDynamicDNAConverter IMPLIMENTATION

		public DynamicUMADnaAsset dnaAsset
		{
			get { return DNAAsset; }
		}

		#endregion

		public DynamicUMADnaAsset DNAAsset
		{
			get
			{
				if (_dnaAsset != null)
					return _dnaAsset;
				else
					return null;
			}
			set
			{
				_dnaAsset = value;
			}
		}

		/// <summary>
		/// Returns the number of plugins assigned to this ConverterController Asset
		/// </summary>
		public int PluginCount
		{
			get { return _plugins.Count; }
		}


		public BaseCharacterModifier overallModifiers
		{
			get { return _overallModifiers; }
		}

		/// <summary>
		/// Changes the characters base scale at runtime. This is reset per character everyime dna is applied, so its not shared like everything else in overallModifiers is.
		/// It should only be used by the 'OverallScaleDNAConverterPlugin' or similar
		/// </summary>
		public float liveScale
		{
			get { return _overallModifiers.liveScale; }
			set { _overallModifiers.liveScale = value; }
		}

		/// <summary>
		/// Gets the base scale as set in the 'overall modifiers' section of this converter
		/// </summary>
		public float baseScale
		{
			get { return _overallModifiers.scale; }
		}
#if UNITY_EDITOR
		public void ImportConverterBehaviourData(DynamicDNAConverterBehaviour DCB)
		{
			_dnaAsset = DCB.dnaAsset;
			_overallModifiers.ImportSettings(DCB.overallModifiers);
		}
#endif
		public void Prepare()
		{
			if (!_prepared)
			{
				for (int i = 0; i < _plugins.Count; i++)
				{
					if (_plugins[i].ApplyPass == DynamicDNAPlugin.ApplyPassOpts.Standard)
					{
						if (!_applyDNAPlugins.Contains(_plugins[i]))
							_applyDNAPlugins.Add(_plugins[i]);
					}
					else if (_plugins[i].ApplyPass == DynamicDNAPlugin.ApplyPassOpts.PrePass)
					{
						if (!_applyDNAPrepassPlugins.Contains(_plugins[i]))
						{
							_applyDNAPrepassPlugins.Add(_plugins[i]);
						}
					}
				}
				_prepared = true;
			}
		}

		public bool AddDnaCallbackDelegate(UnityAction<string, float> callback, string targetDnaName)
		{
			bool added = false;

			if (!_dnaCallbackDelegates.ContainsKey(targetDnaName))
				_dnaCallbackDelegates.Add(targetDnaName, new List<UnityAction<string, float>>());

			if (!_dnaCallbackDelegates[targetDnaName].Contains(callback))
			{
				_dnaCallbackDelegates[targetDnaName].Add(callback);
				added = true;
			}
			return added;
		}

		public bool RemoveDnaCallbackDelegate(UnityAction<string, float> callback, string targetDnaName)
		{
			bool removed = false;

			if (!_dnaCallbackDelegates.ContainsKey(targetDnaName))
			{
				removed = true;
			}
			else
			{
				if (_dnaCallbackDelegates[targetDnaName].Contains(callback))
				{
					_dnaCallbackDelegates[targetDnaName].Remove(callback);
					removed = true;
				}
				if (_dnaCallbackDelegates[targetDnaName].Count == 0)
				{
					_dnaCallbackDelegates.Remove(targetDnaName);
				}
			}
			return removed;
		}

		/// <summary>
		/// Calls ApplyDNA on all this convertersControllers plugins (aka converters) that apply dna during the pre-pass
		/// </summary>
		/// <param name="umaData">The umaData on the avatar</param>
		/// <param name="skeleton">The avatars skeleton</param>
		/// <param name="dnaTypeHash">The dnaTypeHash that this converters behaviour is using</param>
		public void ApplyDNAPrepass(UMAData umaData, UMASkeleton skeleton)
		{
			if (!_prepared)
				Prepare();

			UMADnaBase umaDna = umaData.GetDna(DNATypeHash);
			//Make the DNAAssets match if they dont already, can happen when some parts are in bundles and others arent
			if (((DynamicUMADnaBase)umaDna).dnaAsset != DNAAsset && DNAAsset != null)
				((DynamicUMADnaBase)umaDna).dnaAsset = DNAAsset;

			if (_applyDNAPrepassPlugins.Count > 0)
			{
				for (int i = 0; i < _applyDNAPrepassPlugins.Count; i++)
				{
					_applyDNAPrepassPlugins[i].ApplyDNA(umaData, skeleton, DNATypeHash);
				}
			}
		}

		/// <summary>
		/// Calls ApplyDNA on all this convertersControllers plugins (aka converters) that apply dna at the standard time
		/// </summary>
		/// <param name="umaData">The umaData on the avatar</param>
		/// <param name="skeleton">The avatars skeleton</param>
		/// <param name="dnaTypeHash">The dnaTypeHash that this converters behaviour is using</param>
		public void ApplyDNA(UMAData umaData, UMASkeleton skeleton)
		{
			UMADnaBase umaDna = null;
			//reset the live scale on the overallModifiers ready for any adjustments any plugins might make
			liveScale = -1;
			
			//Add this ApplyHeightMassRadius method to this umaDatas CharacterUpdated event so that HeightMassRadius and bounds BaseCharacterModifiers get applied after all ConverterControllers on this character
			umaData.OnCharacterBeforeUpdated += ApplyHeightMassRadius;
			//Add this ApplyAdjustScale method to this umaDatas DnaUpdated event so that we adjust the global scale just after all other dna adjustments
			umaData.OnCharacterBeforeDnaUpdated += ApplyAdjustScale;
			
			//fixDNAPrefabs- do we need to deal with 'reset' as dnaconverterBehaviour used to do? If so wouldn't we just apply all the plugins with MasterWeight set to 0?
			//if (!asReset)
			//{
				umaDna = umaData.GetDna(DNATypeHash);
				//Make the DNAAssets match if they dont already, can happen when some parts are in bundles and others arent
				if (((DynamicUMADnaBase)umaDna).dnaAsset != DNAAsset)
					((DynamicUMADnaBase)umaDna).dnaAsset = DNAAsset;
			//}
			for (int i = 0; i < _applyDNAPlugins.Count; i++)
			{
				_applyDNAPlugins[i].ApplyDNA(umaData, skeleton, DNATypeHash);
			}
			//_overallModifiers.UpdateCharacter(umaData, skeleton, false);
			ApplyDnaCallbackDelegates(umaData);
		}

		/// <summary>
		/// Applies OverallModifiers after all other dna changes have completed
		/// </summary>
		/// <param name="umaData"></param>
		public void ApplyAdjustScale(UMAData umaData)
		{
			_overallModifiers.AdjustScale(umaData.skeleton);
			//remove this listener from this umaData
			umaData.OnCharacterBeforeDnaUpdated -= ApplyAdjustScale;
		}

		/// <summary>
		/// Applies ApplyHeightMassRadius after all other dna changes have completed
		/// </summary>
		/// <param name="umaData"></param>
		public void ApplyHeightMassRadius(UMAData umaData)
		{
			_overallModifiers.UpdateCharacterHeightMassRadius(umaData, umaData.skeleton);
			//remove this listener from this umaData
			umaData.OnCharacterBeforeUpdated -= ApplyHeightMassRadius;
		}

		public void ApplyDnaCallbackDelegates(UMAData umaData)
		{
			if (_dnaCallbackDelegates.Count == 0)
				return;
			UMADnaBase umaDna;
			//need to use the typehash
			umaDna = umaData.GetDna(DNATypeHash);
			if (umaDna.Count == 0)
				return;
			foreach (KeyValuePair<string, List<UnityAction<string, float>>> kp in _dnaCallbackDelegates)
			{
				for (int i = 0; i < kp.Value.Count; i++)
				{
					kp.Value[i].Invoke(kp.Key, (umaDna as DynamicUMADna).GetValue(kp.Key, true));
				}
			}
		}

		/// <summary>
		/// Gets all the used dna names from all the plugins (aka converters). This can be used to speed up searching the dna for names by string
		/// </summary>
		/// <param name="forceRefresh">Set this to true if you know the dna names used by any of the plugins has been changed at runtime</param>
		/// <returns></returns>
		public List<string> GetUsedDNANames(bool forceRefresh = false)
		{
			if (_usedDNANames.Count == 0 || forceRefresh)
				CompileUsedDNANamesList();

			return _usedDNANames;
		}

		/// <summary>
		/// Gets a plugin from the list of plugins assigned to this converterController by index
		/// </summary>
		public DynamicDNAPlugin GetPlugin(int index)
		{
			if (_plugins.Count > index)
			{
				return _plugins[index];
			}
			return null;
		}

		/// <summary>
		/// Gets a plugin from the list of plugins assigned to this converterController by name
		/// </summary>
		public DynamicDNAPlugin GetPlugin(string name)
		{
			for(int i = 0; i < _plugins.Count; i++)
			{
				if (_plugins[i].name == name)
					return _plugins[i];
			}
			return null;
		}

		/// <summary>
		/// Gets all plugins assigned to this converterController that are of the given type
		/// </summary>
		public List<DynamicDNAPlugin> GetPlugins()
		{
			return _plugins;
		}


		/// <summary>
		/// Gets all plugins assigned to this converterController that are of the given type
		/// </summary>
		public List<DynamicDNAPlugin> GetPlugins(System.Type pluginType)
		{
			var pluginsOfType = new List<DynamicDNAPlugin>();
			for (int i = 0; i < _plugins.Count; i++)
			{
				if (pluginType.IsAssignableFrom(_plugins[i].GetType()))
					pluginsOfType.Add(_plugins[i]);
			}
			return pluginsOfType;
		}

		/// <summary>
		/// Creates a plugin of the given type (must descend from DynamicDNAPlugin), adds it to this converterControllers plugins list,  and stores its asset in the given DynamicDNAConverterController asset
		/// </summary>
		/// <param name="pluginType">The type of dna plugin to create (must descend from DynamicDNAPlugin)</param>
		/// <returns>Returns the created plugin</returns>
		//This can happen at runtime but no asset is created or stored, it just exists in memory
		public DynamicDNAPlugin AddPlugin(System.Type pluginType)
		{
			DynamicDNAPlugin plugin = null;
			plugin = CreatePlugin(pluginType, this);
			if (plugin != null)
			{
				_prepared = false;
				_plugins.Add(plugin);
#if UNITY_EDITOR
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
#endif
				//ensure the new plugin is added to the _applyPlugins lists
				if(Application.isPlaying)
					Prepare();
				return plugin;
			}
			return null;
		}

		/// <summary>
		/// Removes the given plugin from this converterController, and deletes its asset (in the Editor)
		/// </summary>
		/// <param name="pluginToDelete"></param>
		/// <returns></returns>
		public bool DeletePlugin(DynamicDNAPlugin pluginToDelete)
		{
			//check if the given plugin is indeed inside this asset
			//if it is DestroyImmediate
			if (_plugins.Contains(pluginToDelete))
			{
				_prepared = false;
				_plugins.Remove(pluginToDelete);
				Debug.Log(pluginToDelete.name + " successfully deleted from " + this.name);
#if UNITY_EDITOR
				DestroyImmediate(pluginToDelete, true);
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
#endif
			}
			//then Validate the list
			ValidatePlugins();
			return false;
		}

		/// <summary>
		/// At run time this simply clears the plugins list of any empty entries, or null entries, and assigns itself as the converterController for the plugin
		/// At edit time all instantiated plugins inside the given converterController are checked to see if they belong in this list and if they are they get added
		/// This can happen when a plugin script is deleted but then restored again (like when working on different branches in sourceControl)
		/// </summary>
		public void ValidatePlugins()
		{
#if UNITY_EDITOR
			bool changed = false;
#endif
			var cleanList = new List<DynamicDNAPlugin>();
			for (int i = 0; i < _plugins.Count; i++)
			{
				if (_plugins[i] != null)
				{
					if (DynamicDNAPlugin.IsValidPlugin(_plugins[i]))
					{
						cleanList.Add(_plugins[i]);
						if (_plugins[i].converterController != this)
						{
							_plugins[i].converterController = this;
#if UNITY_EDITOR
							EditorUtility.SetDirty(_plugins[i]);
							changed = true;
#endif
						}
					}
				}
			}
			_plugins = cleanList;
#if UNITY_EDITOR
			//if we are in the editor get all the assets inside the given converterController asset and check if any of those should be in this list
			var thisAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this));
			for (int i = 0; i < thisAssets.Length; i++)
			{
				if (thisAssets[i] == this)
					continue;
				if (!DynamicDNAPlugin.IsValidPlugin(thisAssets[i]))
					continue;
				if (!_plugins.Contains(thisAssets[i] as DynamicDNAPlugin))
				{
					_plugins.Add(thisAssets[i] as DynamicDNAPlugin);
					changed = true;
				}
			}
			if (changed)
			{
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
			}
#endif
			CompileUsedDNANamesList();
		}

		/// <summary>
		/// Compiles the used names cache. This can be used to speed up searching the dna for names by string
		/// </summary>
		private void CompileUsedDNANamesList()
		{
			_usedDNANames.Clear();
			for (int i = 0; i < _plugins.Count; i++)
			{
				foreach(KeyValuePair<string, List<int>> kp in _plugins[i].IndexesForDnaNames)
				{
					if (!_usedDNANames.Contains(kp.Key) && !string.IsNullOrEmpty(kp.Key))
						_usedDNANames.Add(kp.Key);
				}
			}
		}

		/// <summary>
		/// Creates a new plugin of the given type and stores it inside the given converterController asset
		/// </summary>
		/// <returns>Returns the created asset</returns>
		private static DynamicDNAPlugin CreatePlugin(System.Type pluginType, DynamicDNAConverterController converter)
		{
			//Checks and warnings
			if (pluginType == null)
			{
				Debug.LogWarning("Could not create plugin because the plugin type was null");
				return null;
			}
			if (converter == null)
			{
				Debug.LogWarning("Could not create plugin because no converterController was provided to add it to");
				return null;
			}
			if (!DynamicDNAPlugin.IsValidPluginType(pluginType))
			{
				Debug.LogWarning("Could not create plugin because it did not descend from DynamicDNAPlugin");
				return null;
			}

			DynamicDNAPlugin asset = ScriptableObject.CreateInstance(pluginType) as DynamicDNAPlugin;
			asset.name = converter.GetUniquePluginName(pluginType.Name.Replace("Plugin","") + "s");
#if UNITY_EDITOR
			Debug.Log(pluginType + " created successfully! Its asset '" + asset.name + "' has been stored in " + converter.name);
			AssetDatabase.AddObjectToAsset(asset, converter);
#endif
			return asset;
		}
		
		/// <summary>
		/// Gets a unique name for a plugin relative to this converterController
		/// </summary>
		/// <param name="desiredName">The name you'd like</param>
		public string GetUniquePluginName(string desiredName, DynamicDNAPlugin existingPlugin = null)
		{
			var intSuffix = 0;
			for (int i = 0; i < _plugins.Count; i++)
			{
				if (_plugins[i].name == desiredName && (existingPlugin == null || (existingPlugin != null && existingPlugin != _plugins[i])))
					intSuffix++;
			}
			return desiredName + (intSuffix != 0 ? intSuffix.ToString() : "");
		}

#if UNITY_EDITOR
		[UnityEditor.MenuItem("UMA/Create Dynamic DNA Converter Controller")]
		public static DynamicDNAConverterController CreateDynamicDNAConverterControllerAsset()
		{
			return UMA.CustomAssetUtility.CreateAsset<DynamicDNAConverterController>();
		}
		public static DynamicDNAConverterController CreateDynamicDNAConverterControllerAsset(string newAssetPath, bool selectCreatedAsset = true, string baseName = "New")
		{
			return UMA.CustomAssetUtility.CreateAsset<DynamicDNAConverterController>(newAssetPath, selectCreatedAsset, baseName);
		}
#endif
	}
}
