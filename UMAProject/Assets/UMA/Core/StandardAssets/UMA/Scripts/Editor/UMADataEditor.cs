#if UNITY_EDITOR
using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMA.UMAData), true)]
    public class UMADataEditor : CharacterBaseEditor
    {
        protected UMAData _umaData;

		//To keep the DNA inspector uptodate when DCA changes the recipe we need to track
		//the active dna and update the editor for it when the recipe changes.
		private int[] _currentDnaTypeHashes;

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
			if (_currentDnaTypeHashes.Length == 0 || currentRecipe == null || currentRecipe.raceData == null)
				return false;
			UMADnaBase[] allDna = currentRecipe.GetAllDna();
			for (int i = 0; i < allDna.Length; i++)
			{
				bool found = false;
				for (int ii = 0; ii < _currentDnaTypeHashes.Length; ii++)
				{
					if (_currentDnaTypeHashes[ii] == allDna[i].DNATypeHash)
						found = true;
				}
				if (!found)
					return false;
			}
			return true;
		}

		public override void OnInspectorGUI()
        {
			if (dnaEditor != null)
				if (!CheckCurrentDNATypeHashes())
				{
					dnaEditor = new DNAMasterEditor(_recipe);
					SetCurrentDnaTypeHashes();
				}
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
