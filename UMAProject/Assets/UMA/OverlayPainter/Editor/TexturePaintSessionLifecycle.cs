using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private enum PersistenceIntent
        {
            None,
            Recovery,
            ProjectSave
        }

        internal enum RecoveryLaunchAction
        {
            Recover,
            Cancel,
            DiscardRecovery,
            OpenRequestedDocument
        }

        [NonSerialized] private readonly Dictionary<EditableTextureTarget, long> persistedTextureRevisions =
            new Dictionary<EditableTextureTarget, long>();
        [NonSerialized] private TexturePaintDocumentStorage.CaptureOperation persistenceCapture;
        [NonSerialized] private TexturePaintRecoveryWriteOperation recoveryWrite;
        [NonSerialized] private TexturePaintProjectSaveOperation projectSave;
        [NonSerialized] private PersistenceIntent persistenceIntent;
        [NonSerialized] private string pendingDocumentPath;
        [NonSerialized] private string recoveryContextKey;
        [NonSerialized] private bool recoveryDirty;
        [NonSerialized] private bool closeAfterSave;
        [NonSerialized] private bool stageCloseAuthorized;
        [NonSerialized] private long documentChangeVersion;
        [NonSerialized] private long captureChangeVersion;
        [NonSerialized] private string persistenceError;
        [NonSerialized] private string persistenceStatus;
        [NonSerialized] private float persistenceProgress;

        internal bool IsDocumentTemporary => document == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(document));
        internal bool IsPersistenceActive => persistenceCapture != null || recoveryWrite != null || projectSave != null;
        internal string DocumentStateLabel
        {
            get
            {
                if (IsPersistenceActive) return "Saving";
                if (!string.IsNullOrEmpty(persistenceError)) return "Save failed";
                if (document != null && document.recoverySnapshot) return documentDirty ? "Recovered · Modified" : "Recovered";
                if (IsDocumentTemporary) return documentDirty ? "Temporary · Modified" : "Temporary";
                return documentDirty ? "Saved · Modified" : "Saved";
            }
        }

        private bool InitializeDocumentSession()
        {
            recoveryContextKey = launchContext != null && launchContext.IsStandalone
                ? TexturePaintRecoveryStore.GetContextKey(launchContext)
                : TexturePaintRecoveryStore.GetContextKey(avatar);
            TexturePaintDocument requested = launchDocument;
            launchDocument = null;
            TexturePaintDocument recovered = null;
            bool deleteRecoveryAfterRequestedOpen = false;
            if (TexturePaintRecoveryStore.HasRecovery(recoveryContextKey))
            {
                int choice;
                if (requested != null)
                {
                    choice = EditorUtility.DisplayDialogComplex("Open Overlay Painter Document?",
                        $"A recoverable Overlay Painter session also exists for this character. You explicitly " +
                        $"opened '{requested.name}'. Open that document and discard the older recovery, cancel, " +
                        "or recover the previous session instead?",
                        "Open " + requested.name, "Cancel Opening", "Recover Instead");
                }
                else
                {
                    choice = EditorUtility.DisplayDialogComplex("Recover Overlay Painter Session?",
                        "A recoverable Overlay Painter session exists for this character. Recovering restores " +
                        "the last complete recovery asset without changing the character, recipe, or source overlay.",
                        "Recover", "Cancel Opening", "Discard Recovery");
                }

                RecoveryLaunchAction action = ResolveRecoveryLaunchAction(requested != null, choice);
                if (action == RecoveryLaunchAction.Cancel) return false;
                if (action == RecoveryLaunchAction.DiscardRecovery)
                    TexturePaintRecoveryStore.Delete(recoveryContextKey);
                else if (action == RecoveryLaunchAction.OpenRequestedDocument)
                    deleteRecoveryAfterRequestedOpen = true;
                else if (!TexturePaintRecoveryStore.TryLoad(recoveryContextKey, out recovered, out string error))
                {
                    int corruptChoice = EditorUtility.DisplayDialogComplex("Recovery Could Not Be Loaded",
                        error + "\n\nThe recovery asset can be discarded, or stage opening can be canceled so it " +
                        "can be inspected manually.",
                        requested != null ? "Discard and Open Document" : "Discard and Start Fresh",
                        "Cancel Opening",
                        requested != null ? "Keep Recovery and Open Document" : "Keep Recovery and Start Fresh");
                    if (corruptChoice == 1) return false;
                    if (corruptChoice == 0) TexturePaintRecoveryStore.Delete(recoveryContextKey);
                }
            }

            document = recovered ?? requested ?? TexturePaintDocumentStorage.CreateTransient(avatar, launchContext);
            if ((recovered != null || requested != null) && !ValidateDocumentLaunchContext(document)) return false;
            controller.AttachDocument(document);
            bool restored = document.surfaces != null && document.surfaces.Count > 0;
            TexturePaintDocumentStorage.RestoreReport restore = default;
            if (restored)
            {
                restore = TexturePaintDocumentStorage.Restore(document, controller.Textures);
            }
            // Do not retire the safety snapshot until the explicitly requested document has
            // passed context validation and completed its restore successfully.
            if (deleteRecoveryAfterRequestedOpen)
                TexturePaintRecoveryStore.Delete(recoveryContextKey);
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            documentRevision = document.revisionId;
            documentDirty = recovered != null;
            recoveryDirty = false;
            documentChangeVersion = recovered != null ? 1L : 0L;
            persistenceStatus = recovered != null ? "Recovered the last complete temporary session" :
                requested != null ? "Loaded " + requested.name :
                "Temporary session · use Save As to create a project document";
            if (restore.HasUnboundLayers)
                ShowWorkspaceStatus($"{restore.unboundLayers} saved layer member" +
                    (restore.unboundLayers == 1 ? string.Empty : "s") +
                    " could not be rebound · see Console");
            return true;
        }

        internal static RecoveryLaunchAction ResolveRecoveryLaunchAction(bool hasRequestedDocument,
            int dialogChoice)
        {
            if (dialogChoice == 1) return RecoveryLaunchAction.Cancel;
            if (dialogChoice < 0 || dialogChoice > 2) return RecoveryLaunchAction.Cancel;
            if (hasRequestedDocument)
                return dialogChoice == 0
                    ? RecoveryLaunchAction.OpenRequestedDocument
                    : RecoveryLaunchAction.Recover;
            return dialogChoice == 0
                ? RecoveryLaunchAction.Recover
                : RecoveryLaunchAction.DiscardRecovery;
        }

        private bool ValidateDocumentLaunchContext(TexturePaintDocument candidate)
        {
            bool currentStandalone = launchContext != null && launchContext.IsStandalone;
            bool savedStandalone = candidate?.launchContext != null && candidate.launchContext.IsStandalone;
            if (currentStandalone != savedStandalone)
            {
                EditorUtility.DisplayDialog("Overlay Painter Context Mismatch",
                    "This document was created for a different launch workflow and cannot be rebound silently.", "OK");
                return false;
            }
            if (!currentStandalone) return true;
            TexturePaintLaunchContext saved = candidate.launchContext;
            if (saved.sourceMode != launchContext.sourceMode ||
                !string.Equals(saved.umaMaterialGuid, launchContext.umaMaterialGuid, StringComparison.Ordinal) ||
                !string.Equals(saved.udimGroupId ?? string.Empty, launchContext.udimGroupId ?? string.Empty,
                    StringComparison.Ordinal) || saved.members == null || launchContext.members == null ||
                saved.members.Count != launchContext.members.Count)
            {
                EditorUtility.DisplayDialog("Overlay Painter Context Mismatch",
                    "This document belongs to a different slot group or UMAMaterial. Open it from its original slot context.", "OK");
                return false;
            }
            bool changed = saved.resolution != launchContext.resolution ||
                saved.standaloneMeshTransformVersion != launchContext.standaloneMeshTransformVersion ||
                saved.fixupRotations != launchContext.fixupRotations ||
                (launchContext.fixupRotations &&
                    (saved.slotRotationEuler - launchContext.slotRotationEuler).sqrMagnitude > 0.000001f);
            for (int i = 0; i < saved.members.Count; i++)
            {
                TexturePaintStandaloneMemberContext oldMember = saved.members[i];
                TexturePaintStandaloneMemberContext currentMember = launchContext.members[i];
                if (!string.Equals(oldMember?.slotGuid, currentMember?.slotGuid, StringComparison.Ordinal))
                {
                    EditorUtility.DisplayDialog("Overlay Painter Context Mismatch",
                        "This document belongs to a different slot or UDIM member set. Open it from its original slot context.",
                        "OK");
                    return false;
                }
                if (!string.Equals(oldMember?.sourceFingerprint, currentMember?.sourceFingerprint, StringComparison.Ordinal))
                    changed = true;
            }
            if (!changed) return true;
            bool rebind = EditorUtility.DisplayDialog("Standalone Sources Changed",
                "One or more slot, overlay, resolution, or orientation settings changed since this document was saved. Rebind the document " +
                "to the currently selected sources, or cancel and inspect the assets first?",
                "Rebind to Current Sources", "Cancel");
            if (rebind) candidate.launchContext = launchContext.Clone();
            return rebind;
        }

        private void BeginPersistence(PersistenceIntent intent, string documentPath = null, bool closeWhenComplete = false)
        {
            if (document == null || controller?.Textures == null || IsPersistenceActive) return;
            if (controller.Painting != null && controller.Painting.IsPainting)
            {
                ShowWorkspaceStatus("Finish the active stroke before saving");
                closeAfterSave = false;
                return;
            }
            // Commit the active controls before taking the persistence snapshot. For a logical
            // UDIM path this also copies the path settings to every physical member.
            CaptureActivePaintLayerSettings();
            TextureSet activeSet = ActiveTextureSet;
            if (!IsLayerMaskMode(activeSet) &&
                TryGetActivePathLayer(activeSet, out TexturePaintLayer activePath))
                CaptureSplineSettings(activePath);
            if (intent == PersistenceIntent.ProjectSave)
            {
                documentPath = string.IsNullOrEmpty(documentPath) ? AssetDatabase.GetAssetPath(document) : documentPath;
                if (string.IsNullOrEmpty(documentPath)) return;
            }
            persistenceError = null;
            persistenceIntent = intent;
            pendingDocumentPath = documentPath;
            closeAfterSave = closeWhenComplete;
            document.editorStateJson = JsonUtility.ToJson(BuildState());
            captureChangeVersion = documentChangeVersion;
            persistenceCapture = TexturePaintDocumentStorage.BeginCapture(document, controller.Textures,
                persistedTextureRevisions, intent == PersistenceIntent.Recovery);
            persistenceStatus = intent == PersistenceIntent.ProjectSave ? "Capturing document changes…" :
                "Updating recovery asset…";
            persistenceProgress = 0f;
            TexturePaintDockWindow.RepaintOpenWindows();
        }

        private void PersistenceUpdate()
        {
            // A completed capture remains available until commit finishes because its snapshot and
            // revision map are the commit payload. Only tick/transition it while no commit operation
            // exists; otherwise this branch would recreate the recovery/project writer every frame.
            if (persistenceCapture != null && recoveryWrite == null && projectSave == null)
            {
                persistenceCapture.Tick();
                persistenceProgress = persistenceCapture.Progress * 0.6f;
                if (!persistenceCapture.IsDone) return;
                if (persistenceCapture.HasError)
                {
                    FailPersistence(persistenceCapture.Error);
                    return;
                }
                try
                {
                    if (persistenceIntent == PersistenceIntent.ProjectSave)
                    {
                        TexturePaintDocument existing = !IsDocumentTemporary &&
                            string.Equals(AssetDatabase.GetAssetPath(document), pendingDocumentPath,
                                StringComparison.OrdinalIgnoreCase) ? document : null;
                        projectSave = new TexturePaintProjectSaveOperation(persistenceCapture.Snapshot, existing,
                            pendingDocumentPath);
                        persistenceStatus = "Writing project document…";
                    }
                    else
                    {
                        recoveryWrite = TexturePaintRecoveryStore.BeginSave(persistenceCapture.Snapshot,
                            recoveryContextKey);
                        persistenceStatus = "Writing recovery asset…";
                    }
                }
                catch (Exception exception)
                {
                    FailPersistence(exception.Message);
                }
                return;
            }

            if (projectSave != null)
            {
                projectSave.Tick();
                persistenceProgress = 0.6f + projectSave.Progress * 0.4f;
                if (!projectSave.IsDone) return;
                if (projectSave.HasError)
                {
                    FailPersistence(projectSave.Error);
                    return;
                }
                CompleteProjectSave();
                return;
            }

            if (recoveryWrite != null)
            {
                recoveryWrite.Tick();
                persistenceProgress = 0.6f + recoveryWrite.Progress * 0.4f;
                if (!recoveryWrite.IsDone) return;
                if (recoveryWrite.HasError)
                {
                    FailPersistence(recoveryWrite.Error);
                    return;
                }
                CompleteRecoverySave();
                return;
            }

            if (!UMASettings.TexturePaintAutomaticRecovery || !recoveryDirty ||
                controller == null || controller.Painting == null || controller.Painting.IsPainting ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextAutosaveTime) return;
            PersistenceIntent autosaveIntent = IsDocumentTemporary ? PersistenceIntent.Recovery : PersistenceIntent.ProjectSave;
            BeginPersistence(autosaveIntent);
        }

        private void CompleteProjectSave()
        {
            TexturePaintDocument previous = document;
            TexturePaintDocument snapshot = persistenceCapture.Snapshot;
            document = projectSave.SavedDocument;
            controller.AttachDocument(document);
            documentRevision = document.revisionId;
            ReplacePersistedRevisions(persistenceCapture.CapturedRevisions);
            bool changedDuringSave = documentChangeVersion != captureChangeVersion || CapturedTargetsChanged();
            documentDirty = changedDuringSave;
            recoveryDirty = changedDuringSave;
            // A normal save only retires recovery when it captured the current revision. A save
            // requested by the close dialog freezes editing until commit, and the user's explicit
            // Save choice also means that the recovery asset must be discarded before leaving.
            if (!changedDuringSave || closeAfterSave) TexturePaintRecoveryStore.Delete(recoveryContextKey);
            persistenceStatus = changedDuringSave ? "Saved; newer changes are still pending" :
                "Saved " + document.name;
            DisposePersistenceOperations(false);
            if (snapshot != null && snapshot != document) DestroyImmediate(snapshot);
            if (previous != null && previous != document && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(previous)))
                DestroyImmediate(previous);
            RecordAutomaticSaveCompletion();
            ShowWorkspaceStatus(persistenceStatus);
            if (closeAfterSave)
            {
                documentDirty = false;
                recoveryDirty = false;
                ScheduleAuthorizedStageClose();
            }
            closeAfterSave = false;
            RepaintAll();
        }

        private void CompleteRecoverySave()
        {
            TexturePaintDocument previous = document;
            TexturePaintDocument snapshot = persistenceCapture.Snapshot;
            document = snapshot;
            document.name = previous != null ? previous.name : document.name;
            document.hideFlags = HideFlags.HideAndDontSave;
            controller.AttachDocument(document);
            documentRevision = document.revisionId;
            ReplacePersistedRevisions(persistenceCapture.CapturedRevisions);
            bool changedDuringSave = documentChangeVersion != captureChangeVersion || CapturedTargetsChanged();
            recoveryDirty = changedDuringSave;
            persistenceStatus = changedDuringSave ? "Recovery saved; newer changes are pending" :
                "Recovery asset is current";
            DisposePersistenceOperations(true);
            if (previous != null && previous != document && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(previous)))
                DestroyImmediate(previous);
            RecordAutomaticSaveCompletion();
            ShowWorkspaceStatus(persistenceStatus);
            if (closeAfterSave)
            {
                // The close workflow freezes both editing surfaces while capture/commit runs. A
                // durable snapshot now exists, so incidental preview revision changes must not
                // strand the stage in a permanent saving state.
                recoveryDirty = false;
                ScheduleAuthorizedStageClose();
            }
            closeAfterSave = false;
            RepaintAll();
        }

        private void FailPersistence(string message)
        {
            bool reopenAfterFailedClose = closeAfterSave;
            persistenceError = message;
            persistenceStatus = "Save failed";
            recoveryDirty = true;
            TexturePaintDocument snapshot = persistenceCapture?.Snapshot;
            DisposePersistenceOperations(false);
            if (snapshot != null && snapshot != document) DestroyImmediate(snapshot);
            closeAfterSave = false;
            ScheduleAutosaveAfterChange();
            Debug.LogError("Overlay Painter persistence: " + message);
            ShowWorkspaceStatus("Save failed · see Console");
            if (reopenAfterFailedClose)
                EditorApplication.delayCall += () =>
                {
                    if (controller != null && StageUtility.GetCurrentStage() == this)
                        TexturePaintDockWindow.ShowDockable();
                };
            RepaintAll();
        }

        private bool CapturedTargetsChanged()
        {
            if (persistenceCapture == null) return false;
            foreach (KeyValuePair<EditableTextureTarget, long> pair in persistenceCapture.CapturedRevisions)
                if (pair.Key == null || pair.Key.Revision != pair.Value) return true;
            return false;
        }

        private void ReplacePersistedRevisions(IReadOnlyDictionary<EditableTextureTarget, long> revisions)
        {
            persistedTextureRevisions.Clear();
            if (revisions == null) return;
            foreach (KeyValuePair<EditableTextureTarget, long> pair in revisions)
                if (pair.Key != null) persistedTextureRevisions[pair.Key] = pair.Value;
        }

        private void DisposePersistenceOperations(bool keepSnapshot)
        {
            TexturePaintDocument snapshot = persistenceCapture?.Snapshot;
            recoveryWrite?.Dispose();
            projectSave?.Dispose();
            recoveryWrite = null;
            projectSave = null;
            persistenceCapture = null;
            persistenceIntent = PersistenceIntent.None;
            pendingDocumentPath = null;
            persistenceProgress = 0f;
            if (!keepSnapshot && snapshot != null && snapshot != document && projectSave == null)
            {
                // The caller owns destruction after it has finished replacing document references.
            }
        }

        private void SaveEmergencyRecovery()
        {
            if (!recoveryDirty || document == null || controller?.Textures == null) return;
            TexturePaintDocument emergency = TexturePaintDocumentStorage.CreateTransient(avatar, launchContext);
            emergency.documentId = document.documentId;
            emergency.createdUtc = document.createdUtc;
            emergency.editorStateJson = JsonUtility.ToJson(BuildState());
            try
            {
                TexturePaintDocumentStorage.Save(emergency, controller.Textures, true);
                TexturePaintRecoveryStore.SaveImmediate(emergency, recoveryContextKey);
                recoveryDirty = false;
            }
            catch (Exception exception)
            {
                Debug.LogError("Overlay Painter emergency recovery failed: " + exception.Message);
            }
            finally
            {
                DestroyImmediate(emergency);
            }
        }

        internal bool RequestCloseStage()
        {
            if (IsPersistenceActive)
            {
                // A background autosave is already capturing the same document the user is
                // closing. Adopt it as the close save and let the dock disappear immediately;
                // reopening the window with a modal "wait" dialog made a healthy asynchronous
                // save look like a hung editor. Completion will authorize and close the stage.
                closeAfterSave = true;
                persistenceStatus = "Finishing save before closing…";
                TexturePaintDockWindow.RepaintOpenWindows();
                return true;
            }
            bool hasRecovery = TexturePaintRecoveryStore.HasRecovery(recoveryContextKey);
            if (documentDirty || hasRecovery)
            {
                int choice = EditorUtility.DisplayDialogComplex("Unsaved Overlay Painter Changes",
                    "Save the current overlay painting changes before closing? Save commits them to a project " +
                    "document and removes the recovery asset. Discard closes without saving and removes the " +
                    "recovery asset. Cancel returns to the editor.", "Save", "Cancel", "Discard");
                if (choice == 1) return false;
                if (choice == 0)
                {
                    closeAfterSave = true;
                    if (IsDocumentTemporary) SaveWorkspaceAs(true);
                    else BeginPersistence(PersistenceIntent.ProjectSave, AssetDatabase.GetAssetPath(document), true);
                    return false;
                }
                TexturePaintRecoveryStore.Delete(recoveryContextKey);
            }
            ScheduleAuthorizedStageClose();
            return true;
        }

        internal void DeferAutosaveAfterExternalOperation()
        {
            // Export performs synchronous AssetDatabase work on the editor thread. If the normal
            // debounce deadline expires during that work, the very next editor update otherwise
            // starts a large document capture before the user can close the export or painter UI.
            if (!IsPersistenceActive)
                nextAutosaveTime = Math.Max(nextAutosaveTime,
                    CalculateAutomaticSaveDeadline(EditorApplication.timeSinceStartup,
                        lastAutomaticSaveTime, UMASettings.TexturePaintRecoveryIdleDelaySeconds,
                        UMASettings.TexturePaintRecoveryMinimumIntervalSeconds));
        }

        private void ScheduleAutosaveAfterChange()
        {
            nextAutosaveTime = CalculateAutomaticSaveDeadline(
                EditorApplication.timeSinceStartup, lastAutomaticSaveTime,
                UMASettings.TexturePaintRecoveryIdleDelaySeconds,
                UMASettings.TexturePaintRecoveryMinimumIntervalSeconds);
        }

        private void RecordAutomaticSaveCompletion()
        {
            lastAutomaticSaveTime = EditorApplication.timeSinceStartup;
            nextAutosaveTime = CalculateAutomaticSaveDeadline(lastAutomaticSaveTime,
                lastAutomaticSaveTime, UMASettings.TexturePaintRecoveryIdleDelaySeconds,
                UMASettings.TexturePaintRecoveryMinimumIntervalSeconds);
        }

        internal static double CalculateAutomaticSaveDeadline(double now,
            double lastCompletedSave, double idleDelaySeconds, double minimumIntervalSeconds)
        {
            double idleDeadline = now + Math.Max(0d, idleDelaySeconds);
            if (double.IsNegativeInfinity(lastCompletedSave)) return idleDeadline;
            return Math.Max(idleDeadline,
                lastCompletedSave + Math.Max(0d, minimumIntervalSeconds));
        }

        private void ScheduleAuthorizedStageClose()
        {
            stageCloseAuthorized = true;
            EditorApplication.delayCall += () =>
            {
                if (StageUtility.GetCurrentStage() == this) StageUtility.GoBackToPreviousStage();
            };
        }

        private void DisposeDocumentSession()
        {
            if (!stageCloseAuthorized) SaveEmergencyRecovery();
            TexturePaintDocument snapshot = persistenceCapture?.Snapshot;
            persistenceCapture?.Cancel();
            DisposePersistenceOperations(false);
            if (snapshot != null && snapshot != document) DestroyImmediate(snapshot);
            if (document != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(document))) DestroyImmediate(document);
            persistedTextureRevisions.Clear();
            document = null;
        }

        private TexturePaintStageState LoadDocumentEditorState()
        {
            if (document == null || string.IsNullOrEmpty(document.editorStateJson)) return null;
            try { return JsonUtility.FromJson<TexturePaintStageState>(document.editorStateJson); }
            catch (Exception exception)
            {
                Debug.LogWarning("Overlay Painter document editor state could not be restored: " + exception.Message);
                return null;
            }
        }
    }
}
