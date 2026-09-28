using System;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    // Drawers have no reliable OnDisable. One shared subscription avoids retaining drawer targets.
    [InitializeOnLoad]
    internal static class AvatarInspectorCacheEpoch
    {
        internal static uint Revision { get; private set; }
        static AvatarInspectorCacheEpoch()
        {
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.playModeStateChanged += _ => Invalidate();
        }
        internal static void Invalidate() { unchecked { Revision++; } }
    }

    internal sealed class AvatarInspectorRefreshGate
    {
        private uint revision;
        private double nextRefresh = double.NegativeInfinity;
        private readonly double interval;
        internal AvatarInspectorRefreshGate(double interval) { this.interval = interval; }
        internal void Invalidate() { nextRefresh = double.NegativeInfinity; }
        internal bool ShouldRefresh(double now, bool force = false)
        {
            if (!force && revision == AvatarInspectorCacheEpoch.Revision && now < nextRefresh) return false;
            revision = AvatarInspectorCacheEpoch.Revision;
            nextRefresh = now + interval;
            return true;
        }
    }

    public partial class DynamicCharacterAvatarEditor
    {
        private bool _hasInspectorLayout;
        private bool _layoutEditorBusy;
        private uint _layoutDefinitionRevision;
        private uint _layoutCacheRevision;
        private Action _pendingAvatarLoad;
        private readonly AvatarInspectorRefreshGate _serializedRefresh = new AvatarInspectorRefreshGate(0.25);
        private int _serializedDirtyCount;
        private int _serializedRefreshCount;
        private bool _inspectorRepaintPending;
        private static readonly Unity.Profiling.ProfilerMarker InspectorDrawMarker = new Unity.Profiling.ProfilerMarker("UMA.DCAInspector.Draw");
        private static readonly Unity.Profiling.ProfilerMarker SerializedRefreshMarker = new Unity.Profiling.ProfilerMarker("UMA.DCAInspector.RefreshSerializedState");

        private bool PrepareInspectorFrame()
        {
            if (!(target is DynamicCharacterAvatar avatar)) return false;
            bool busy = IsEditorBusy();
            if (Event.current.type == EventType.Layout)
            {
                _inspectorRepaintPending = false;
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
                // UpdateIfRequiredOrScript still refreshes scripts. Do not serialize the entire
                // generated recipe for every idle Layout. Direct runtime writes get a 4 Hz fallback.
                // Refresh only at Layout, preserving the LoadAvatarDefinition control-tree fence.
                int dirtyCount = EditorUtility.GetDirtyCount(avatar);
                if (_serializedRefresh.ShouldRefresh(EditorApplication.timeSinceStartup,
                    !_hasInspectorLayout || _layoutDefinitionRevision != avatar.EditorAvatarDefinitionRevision ||
                    dirtyCount != _serializedDirtyCount))
                {
                    using var refreshMarker = SerializedRefreshMarker.Auto();
                    serializedObject.Update();
                    _serializedDirtyCount = dirtyCount;
                    _serializedRefreshCount++;
                }
                _layoutDefinitionRevision = avatar.EditorAvatarDefinitionRevision;
                _layoutCacheRevision = AvatarInspectorCacheEpoch.Revision;
                _layoutEditorBusy = busy;
                _hasInspectorLayout = true;
            }
            else if (!_hasInspectorLayout || _layoutDefinitionRevision != avatar.EditorAvatarDefinitionRevision ||
                     _layoutCacheRevision != AvatarInspectorCacheEpoch.Revision ||
                     _layoutEditorBusy != busy)
            {
                RequestInspectorRepaint();
                GUIUtility.ExitGUI();
            }
            return true;
        }

        private void RequestInspectorRepaint()
        {
            // Hidden/locked Inspectors may not consume Layout until their tab is shown.
            if (_inspectorRepaintPending) return;
            _inspectorRepaintPending = true;
            Repaint();
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
                RequestInspectorRepaint();
            }
        }

        private void CancelPendingAvatarLoad()
        {
            EditorApplication.delayCall -= RunPendingAvatarLoad;
            _pendingAvatarLoad = null;
        }
    }
}
