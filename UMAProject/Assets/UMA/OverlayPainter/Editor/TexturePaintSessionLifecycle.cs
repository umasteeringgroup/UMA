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
            TexturePaintDocument recovered = null;
            if (TexturePaintRecoveryStore.HasRecovery(recoveryContextKey))
            {
                int choice = EditorUtility.DisplayDialogComplex("Recover Overlay Painter Session?",
                    "A recoverable Overlay Painter session exists for this character. Recovering restores " +
                    "the last complete recovery asset without changing the character, recipe, or source overlay.",
                    "Recover", "Cancel Opening", "Discard Recovery");
                if (choice == 1) return false;
                if (choice == 2) TexturePaintRecoveryStore.Delete(recoveryContextKey);
                else if (!TexturePaintRecoveryStore.TryLoad(recoveryContextKey, out recovered, out string error))
                {
                    int corruptChoice = EditorUtility.DisplayDialogComplex("Recovery Could Not Be Loaded",
                        error + "\n\nThe recovery asset can be discarded, or stage opening can be canceled so it can be inspected manually.",
                        "Discard and Start Fresh", "Cancel Opening", "Keep Recovery and Start Fresh");
                    if (corruptChoice == 1) return false;
                    if (corruptChoice == 0) TexturePaintRecoveryStore.Delete(recoveryContextKey);
                }
            }

            document = recovered ?? TexturePaintDocumentStorage.CreateTransient(avatar, launchContext);
            if (recovered != null && !ValidateDocumentLaunchContext(recovered)) return false;
            controller.AttachDocument(document);
            bool restored = document.surfaces != null && document.surfaces.Count > 0;
            if (restored)
            {
                TexturePaintDocumentStorage.Restore(document, controller.Textures);
                TexturePaintDocumentStorage.RestoreMasks(document, controller.Masks);
            }
            TexturePaintDocumentStorage.RecordCurrentRevisions(controller.Textures, persistedTextureRevisions);
            documentRevision = document.revisionId;
            documentDirty = recovered != null;
            recoveryDirty = false;
            documentChangeVersion = recovered != null ? 1L : 0L;
            persistenceStatus = recovered != null ? "Recovered the last complete temporary session" :
                "Temporary session · use Save As to create a project document";
            return true;
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
                if (!string.Equals(oldMember?.slotGuid, currentMember?.slotGuid, StringComparison.Ordinal)) return false;
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
                controller.Masks, persistedTextureRevisions, intent == PersistenceIntent.Recovery);
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

            if (!recoveryDirty || controller == null || controller.Painting == null || controller.Painting.IsPainting ||
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
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
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
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
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
            persistenceError = message;
            persistenceStatus = "Save failed";
            recoveryDirty = true;
            TexturePaintDocument snapshot = persistenceCapture?.Snapshot;
            DisposePersistenceOperations(false);
            if (snapshot != null && snapshot != document) DestroyImmediate(snapshot);
            closeAfterSave = false;
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
            Debug.LogError("Overlay Painter persistence: " + message);
            ShowWorkspaceStatus("Save failed · see Console");
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
                TexturePaintDocumentStorage.Save(emergency, controller.Textures, controller.Masks, true);
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
                EditorUtility.DisplayDialog("Overlay Painter Is Saving",
                    closeAfterSave
                        ? "The project document is being committed. The stage will close automatically when it finishes."
                        : "Wait for the current save to finish before closing the stage.", "OK");
                return false;
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
