#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;
using UMA;
using UMA.Integrations;

namespace UMAEditor
{
    [CustomEditor(typeof(UMA.UMAData), true)]
    public class UMADataEditor : CharacterBaseEditor
    {
        protected UMAData _umaData;

        public void OnEnable()
        {
            if (!NeedsReenable())
                return;

            showBaseEditor = false;
            _umaData = target as UMAData;
            _errorMessage = null;
            _recipe = _umaData.umaRecipe;
			if (_recipe == null || _recipe.raceData == null)
            {				
                _errorMessage = "UMA Data not loaded.";
            } 
            else
            {
				DNAMasterEditor.umaGenerator = _umaData.umaGenerator;
                dnaEditor = new DNAMasterEditor(_recipe);
                slotEditor = new SlotMasterEditor(_recipe);

                _rebuildOnLayout = true;
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
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