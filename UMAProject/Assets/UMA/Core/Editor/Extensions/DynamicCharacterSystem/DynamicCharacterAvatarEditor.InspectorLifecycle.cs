using System;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    public partial class DynamicCharacterAvatarEditor
    {
        private bool _hasInspectorLayout;
        private bool _layoutEditorBusy;
        private uint _layoutDefinitionRevision;
        private Action _pendingAvatarLoad;

        private bool PrepareInspectorFrame()
        {
            if (!(target is DynamicCharacterAvatar avatar)) return false;
            bool busy = IsEditorBusy();
            if (Event.current.type == EventType.Layout)
            {
                if (!_hasInspectorLayout || _layoutDefinitionRevision != avatar.EditorAvatarDefinitionRevision)
                {
                    cachedRace = string.Empty;
                    cachedRaceDNA = rawcachedRaceDNA = Array.Empty<string>();
                    _cachedDNACollectionRef = null;
                    _nameToGroupCache.Clear();
                    _nameToDnaCache.Clear();
                    _groupsSnapshot.Clear();
                    _groupDnaCounts.Clear();
                    _groupNamesCache = null;
                    animationController = null;
                }
                // Refresh external changes only when a new control tree can be laid out.
                serializedObject.Update();
                _layoutDefinitionRevision = avatar.EditorAvatarDefinitionRevision;
                _layoutEditorBusy = busy;
                _hasInspectorLayout = true;
            }
            else if (!_hasInspectorLayout || _layoutDefinitionRevision != avatar.EditorAvatarDefinitionRevision ||
                     _layoutEditorBusy != busy)
            {
                Repaint();
                GUIUtility.ExitGUI();
            }
            return true;
        }

        private void QueueAvatarDefinitionLoad(AvatarDefinition definition)
        {
            var avatar = target as DynamicCharacterAvatar;
            if (avatar == null || _pendingAvatarLoad != null) return;
            // Finish pending edits before replacing the same serialized collections.
            serializedObject.ApplyModifiedProperties();
            _pendingAvatarLoad = () =>
            {
                if (avatar == null || target != avatar) return;
                avatar.LoadAvatarDefinition(definition);
                avatar.BuildCharacter(false);
            };
            EditorApplication.delayCall += RunPendingAvatarLoad;
        }

        private void RunPendingAvatarLoad()
        {
            EditorApplication.delayCall -= RunPendingAvatarLoad;
            var action = _pendingAvatarLoad;
            _pendingAvatarLoad = null;
            if (this == null || target == null || action == null) return;
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception exception) { Debug.LogException(exception, target); }
            finally
            {
                _hasInspectorLayout = false;
                Repaint();
            }
        }

        private void CancelPendingAvatarLoad()
        {
            EditorApplication.delayCall -= RunPendingAvatarLoad;
            _pendingAvatarLoad = null;
        }
    }
}
