using System;
using UnityEditor;
using UnityEngine;

namespace UMA.PoseTools
{
    public sealed class UMARaceTPoseEditorWindow : EditorWindow
    {
        private UMARaceTPoseEditSession session;
        private UMABonePoseEditor poseEditor;
        private Vector2 scroll;
        [SerializeField] private string initialPoseJson;
        private string error;
        [SerializeField] private UMAData recoverSource;
        [SerializeField] private UmaTPose recoverDestination;
        [SerializeField] private byte[] recoverBaseline;
        [SerializeField] private byte[] recoverExpected;
        [SerializeField] private string recoverPoseJson;
        [SerializeField] private RaceData copyRace;
        [SerializeField] private string copyStatus;

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CaptureDraft;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (recoverSource != null) EditorApplication.delayCall += RestoreDraft;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || session == null) return;
            EditorApplication.isPlaying = false;
            ShowNotification(new GUIContent("Close the race T-pose editor before entering Play mode."));
        }

        private void CaptureDraft()
        {
            if (session?.Pose == null) return;
            recoverSource = session.Source;
            recoverDestination = session.Destination;
            recoverBaseline = session.BaselineChunk;
            recoverExpected = session.ExpectedChunk;
            recoverPoseJson = EditorJsonUtility.ToJson(session.Pose);
        }

        private void RestoreDraft()
        {
            if (this == null || session != null || recoverSource == null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RestoreDraft;
                return;
            }
            try
            {
                if (UMARaceTPoseEditSession.GetRace(recoverSource)?.TPose != recoverDestination)
                    throw new InvalidOperationException("The character's race T-pose changed during reload. Reopen the editor to extract it again.");
                session = UMARaceTPoseEditSession.Create(recoverSource, recoverBaseline, recoverExpected);
                EditorJsonUtility.FromJsonOverwrite(recoverPoseJson, session.Pose);
                session.Pose.RefreshPreview();
                poseEditor = (UMABonePoseEditor)Editor.CreateEditor(session.Pose, typeof(UMABonePoseEditor));
            }
            catch (Exception exception) { session?.Dispose(); session = null; error = exception.Message; }
            Repaint();
        }

        [MenuItem("UMA/Tools/Pose Tools/Edit Selected Character Race T-Pose")]
        private static void OpenSelected()
        {
            var selected = Selection.activeGameObject;
            Open(selected == null ? null : selected.GetComponentInParent<UMAData>());
        }

        public static void Open(UMAData source)
        {
            // One session at a time prevents two sets of Scene view handles competing.
            foreach (var existing in Resources.FindObjectsOfTypeAll<UMARaceTPoseEditorWindow>())
            {
                existing.Focus();
                return;
            }
            UMARaceTPoseEditSession created = null;
            try
            {
                created = UMARaceTPoseEditSession.Create(source);
                var window = CreateInstance<UMARaceTPoseEditorWindow>();
                window.titleContent = new GUIContent("Race T-Pose");
                window.minSize = new Vector2(440, 400);
                window.session = created;
                window.initialPoseJson = EditorJsonUtility.ToJson(created.Pose);
                window.CaptureDraft();
                window.poseEditor = (UMABonePoseEditor)Editor.CreateEditor(created.Pose, typeof(UMABonePoseEditor));
                window.saveChangesMessage = "Save this temporary bone pose back to the race T-pose asset?";
                window.Show();
                created.FramePreview();
            }
            catch (Exception exception)
            {
                created?.Dispose();
                EditorUtility.DisplayDialog("Edit Race T-Pose", exception.Message, "Close");
            }
        }

        private void OnGUI()
        {
            if (session == null || session.Pose == null)
            {
                EditorGUILayout.HelpBox(error ?? "The temporary preview session has ended. Reopen Edit Race T-Pose to extract it again.", MessageType.Info);
                if (GUILayout.Button("Close")) Close();
                return;
            }
            hasUnsavedChanges = initialPoseJson != EditorJsonUtility.ToJson(session.Pose);
            EditorGUILayout.LabelField("Race: " + (session.Race != null ? session.Race.raceName : "(removed)"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Save to T-Pose", session.Destination, typeof(UmaTPose), false);
            EditorGUILayout.HelpBox("Edit the temporary character beside the original using the bone controls below or Scene view handles. Values are offsets from the race T-pose. Only Save writes the asset; all races sharing it will use the change on their next build. The original character and Animator are untouched.", MessageType.Info);
            EditorGUILayout.LabelField($"{session.MatchedBones} bones visible in preview / {session.TotalBones} destination T-pose bones.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    copyRace = (RaceData)EditorGUILayout.ObjectField("Source race", copyRace, typeof(RaceData), false);
                    using (new EditorGUI.DisabledScope(copyRace == null || copyRace.TPose == null))
                        if (GUILayout.Button("Copy from race", GUILayout.Width(120))) CopyFromSelectedRace();
                }
                EditorGUILayout.LabelField("Replaces this draft with matching local bone transforms. Missing bones reset to the original T-pose; extra source bones are ignored. Save still writes to the destination above. Undo restores the previous draft.", EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(copyStatus)) EditorGUILayout.LabelField(copyStatus, EditorStyles.wordWrappedMiniLabel);
                if (copyRace != null && copyRace.TPose == null)
                    EditorGUILayout.HelpBox("This race has no T-pose to copy.", MessageType.Warning);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Frame Preview")) session.FramePreview();
            if (GUILayout.Button("Save to Race T-Pose")) SaveChanges();
            if (GUILayout.Button("Cancel / Close"))
            {
                Close(); // EditorWindow provides Save / Discard / Cancel when changes are pending.
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            poseEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        private void CopyFromSelectedRace()
        {
            try
            {
                poseEditor.serializedObject.ApplyModifiedProperties();
                var result = session.CopyFromRace(copyRace);
                poseEditor.RefreshReplacedRaceTPose();
                copyStatus = $"Copied {result.Copied} bones; reset {result.Reset}; ignored {result.Ignored} source-only bones.";
                if (result.WithoutPreview > 0)
                    copyStatus += $" {result.WithoutPreview} copied bones are absent from the preview but will be saved.";
                error = null;
                hasUnsavedChanges = initialPoseJson != EditorJsonUtility.ToJson(session.Pose);
                CaptureDraft();
            }
            catch (Exception exception) { error = exception.Message; }
            Repaint();
        }

        public override void SaveChanges()
        {
            try
            {
                session.Save();
                initialPoseJson = EditorJsonUtility.ToJson(session.Pose);
                CaptureDraft();
                error = null;
                base.SaveChanges();
                ShowNotification(new GUIContent("Race T-pose saved"));
            }
            catch (Exception exception) { error = exception.Message; Repaint(); }
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnDisable()
        {
            CaptureDraft();
            AssemblyReloadEvents.beforeAssemblyReload -= CaptureDraft;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall -= RestoreDraft;
            if (poseEditor != null) DestroyImmediate(poseEditor);
            session?.Dispose();
            session = null;
            SceneView.RepaintAll();
        }
    }
}
