using System;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
    public partial class DynamicCharacterAvatarEditor : IUMAPluginHost
    {
        private IDisposable pluginPlacement;
        public bool CanRunPluginActions => thisDCA != null && targets.Length == 1 && !IsEditorBusy();
        public bool TryPreparePluginAction()
        {
            if (!CanRunPluginActions) return false;
            serializedObject.ApplyModifiedProperties();
            return true;
        }
        public void RefreshAfterPluginAction()
        {
            serializedObject.Update();
            _hasInspectorLayout = false;
            Repaint();
        }
    }
}
