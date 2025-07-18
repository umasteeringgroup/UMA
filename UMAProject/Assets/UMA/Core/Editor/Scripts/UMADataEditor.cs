#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMA.UMAData), true)]
    public class UMADataEditor : CharacterBaseEditor
    {
        protected UMAData _umaData;
        public bool initialized = false;

		//To keep the DNA inspector uptodate when DCA changes the recipe we need to track
		//the active dna and update the editor for it when the recipe changes.
		private int[] _currentDnaTypeHashes;

        public void InitializeUMADataEditor()
        {
            //   if (!NeedsReenable())
            //       return;

            dnaEditor = null;
            slotEditor = null;
            showBaseEditor = false;
            _umaData = target as UMAData;
            _errorMessage = null;
            if (_umaData == null)
            {
                _errorMessage = "UmaData is null";
                return;
            }
            _recipe = _umaData.umaRecipe;
            if (_recipe == null || _recipe.raceData == null)
            {
                _errorMessage = "Recipe data has not been generated.";
            }
            else
            {
                DNAMasterEditor.umaGenerator = _umaData.umaGenerator;
                dnaEditor = new DNAMasterEditor(_recipe);
                slotEditor = new SlotMasterEditor(_recipe);

                SetCurrentDnaTypeHashes();

                _rebuildOnLayout = true;
            }
        }

        private void SetCurrentDnaTypeHashes()
		{
			UMADnaBase[] allDna = (target as UMAData).umaRecipe.GetAllDna();
			_currentDnaTypeHashes = new int[allDna.Length];
			for (int i = 0; i < allDna.Length; i++)
			{
				_currentDnaTypeHashes[i] = allDna[i].DNATypeHash;
			}
		}

		private bool CheckCurrentDNATypeHashes()
		{
			var currentRecipe = (target as UMAData).umaRecipe;
			if (_currentDnaTypeHashes == null)
            {
				SetCurrentDnaTypeHashes();
            }
			if (_currentDnaTypeHashes.Length == 0 || currentRecipe == null || currentRecipe.raceData == null)
            {
                return false;
            }

            UMADnaBase[] allDna = currentRecipe.GetAllDna();
			for (int i = 0; i < allDna.Length; i++)
			{
				bool found = false;
				for (int ii = 0; ii < _currentDnaTypeHashes.Length; ii++)
				{
					if (_currentDnaTypeHashes[ii] == allDna[i].DNATypeHash)
                    {
                        found = true;
                    }
                }
				if (!found)
                {
                    return false;
                }
            }
			return true;
		}

		public static bool ShowOverrides;

		public override void OnInspectorGUI()
        {
            if (dnaEditor == null)
            {
                InitializeUMADataEditor();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (GUIHelper.BeginCollapsableGroup(ref ShowOverrides, "Override Info"))
                {
					EditorGUILayout.LabelField("Object ID", _umaData.GetInstanceID().ToString());
					EditorGUILayout.LabelField("TPose Override", (_umaData.OverrideTpose != null).ToString());
					EditorGUILayout.LabelField("Texture Override", (_umaData.TextureOverrides.Count != 0).ToString());

					GUIHelper.EndCollapsableGroup();
                }
				if (dnaEditor != null)
                {
                    if (!CheckCurrentDNATypeHashes())
					{
						dnaEditor = new DNAMasterEditor(_recipe);
						SetCurrentDnaTypeHashes();
					}
                }
                if (GUILayout.Button("Rebuild"))
                {
                    DoUpdate();
                }
                base.OnInspectorGUI(); 
			}
			else
            {
                DoEditTimeInfo();
            }
        }

        protected void DoEditTimeInfo()
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 1f));
            EditorGUILayout.LabelField("Edit Time Info", EditorStyles.boldLabel);
            EditorGUILayout.IntField("Instance ID", _umaData.GetInstanceID());
            EditorGUILayout.Toggle("Using 32 bit", _umaData.force32bit);
            if (_umaData.umaRecipe != null)
            {
                EditorGUILayout.IntField("SlotCount", _umaData.umaRecipe.slotDataList.Length);
                foreach(SlotData slot in _umaData.umaRecipe.slotDataList)
                {
                    if (slot != null)
                    {
                        EditorGUILayout.LabelField("Slot: " + slot.asset.slotName);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No Recipe Data");
            }
            GUIHelper.EndVerticalPadded();
        }


        protected override void DoUpdate()
        {
            _umaData.Dirty(_dnaDirty, _textureDirty, _meshDirty);
            _needsUpdate = false;
            _dnaDirty = false;
            _textureDirty = false;
            _meshDirty = false;
            Rebuild();
        }

        protected override void Rebuild()
        {
            base.Rebuild();
        }
    }
}
#endif
