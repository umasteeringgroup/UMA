using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    // The "Editing tools" section: the editors that work on this character's meshes (Vertex Editor, Face Editor,
    // Touchup Weights, Create Slot From Scene Mesh). It is drawn near the top of the inspector, in Standard and
    // Advanced view, so the tools can be found; they used to be under Utilities at the bottom of Advanced view.
    public partial class DynamicCharacterAvatarEditor
    {
        private const string EditingToolsHelp =
            "Vertex Editor works on the vertices of the slots: Sculpt (brushes, masks, Autosculpt for clothing), " +
            "Vertex Paint (vertex colors for shading, and Hair Root to Tip, which colors hair cards from black at the root " +
            "to white at the tip for the hair sway), Advanced (select vertices to move them, scale them along their normals, " +
            "or build other Mesh Modifiers) and Blendshapes (extract blendshapes onto other slots). Its work is saved as a " +
            "Mesh Modifier asset, which changes the slots without touching them, or written straight into a slot.\n\n" +
            "Face Editor hides faces of the slots, for example the body under clothing so it does not poke through. Paint or " +
            "select the faces, or have them found by raycasting from the clothing. Its work is saved as a Mesh Hide Asset.\n\n" +
            "Touchup Weights fixes a slot's bone weights on this character, in its current pose, with a brush, and saves " +
            "them back to the slot.\n\n" +
            "Create Slot From Scene Mesh turns a mesh in the scene into a UMA slot fitted to this character.\n\n" +
            "To edit an existing Mesh Modifier, Mesh Hide Asset or Mesh Hide Collection, drop it on the box at the bottom " +
            "of this section. The tools work on the built character, so Editor Time Generation has to be on (or the game " +
            "running), and not in Prefab Mode.";

        private void DrawEditingToolsSection()
        {
            using (var section = inspectorView.CollapsibleSection("Editing tools", EditingToolsHelp))
            {
                if (!section.Expanded)
                {
                    return;
                }
                bool built = Application.isPlaying || thisDCA.editorTimeGeneration;
                if (!built)
                {
                    EditorGUILayout.HelpBox(
                        "These tools work on the built character. Turn on Editor Time Generation to use them.",
                        MessageType.Info);
                    if (GUILayout.Button("Turn On Editor Time Generation"))
                    {
                        SerializedProperty generation = serializedObject.FindProperty("editorTimeGeneration");
                        generation.boolValue = true;
                        serializedObject.ApplyModifiedProperties();
                        UpdateCharacter();
                    }
                }

                using (new EditorGUI.DisabledScope(!built))
                {
                    DrawEditingToolButton(
                        "Open Vertex Editor (Sculpt, Paint, Mesh Modifiers)",
                        "Open this character in the Vertex Editor. A new Mesh Modifier is started; nothing is saved until you save it.",
                        "Sculpt slots, paint vertex colors (including hair root to tip), move vertices, extract blendshapes. Saved as a Mesh Modifier or into a slot.",
                        () =>
                        {
                            thisDCA.ignoreMeshHideAssets = true;
                            thisDCA.GenerateNow();
                            VertexEditorStage.ShowStage(thisDCA, null);
                        });
                    DrawEditingToolButton(
                        "Open Face Editor (Mesh Hide Assets)",
                        "Open this character in the Face Editor. A new Mesh Hide Asset is started; nothing is saved until you save it.",
                        "Hide faces of slots, for example the body under clothing so it does not poke through. Saved as a Mesh Hide Asset.",
                        () =>
                        {
                            thisDCA.ignoreMeshHideAssets = true;
                            thisDCA.GenerateNow();
                            FaceEditorStage.ShowStage(thisDCA, (MeshHideAsset)null);
                        });
                    DrawEditingToolButton(
                        "Touchup Weights",
                        "Touch up a slot's bone weights on this character in its current pose.",
                        "Fix a slot's bone weights on this character in its current pose, with a brush and weight colors, and save them back to the slot.",
                        () => VertexEditorStage.OpenTouchupWeights(thisDCA));
                    DrawEditingToolButton(
                        "Create Slot From Scene Mesh",
                        "Make a UMA slot from a mesh in the scene, fitted to this character.",
                        "Turn a mesh in the scene into a UMA slot fitted to this character.",
                        () => SceneMeshSlotBuilderWindow.ShowWindow(thisDCA),
                        needsSceneCharacter: false);
                    DrawEditExistingDropBox();
                }
            }
        }

        private void DrawEditingToolButton(string label, string tooltip, string description, System.Action open, bool needsSceneCharacter = true)
        {
            if (GUILayout.Button(new GUIContent(label, tooltip)))
            {
                if (!needsSceneCharacter || CanOpenMeshEditor())
                {
                    open();
                }
            }
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2f);
        }

        /// <summary>Drop a Mesh Modifier, Mesh Hide Asset or Mesh Hide Collection to open it in its editor.</summary>
        private void DrawEditExistingDropBox()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            var dropLabel = new GUIContent(
                "Drag & Drop a MeshModifier, Mesh Hide Asset, or Mesh Hide Collection here to edit",
                "Drop a MeshModifier, MeshHideAsset, or MeshHideAssetCollection asset");
            GUI.Box(dropRect, dropLabel, EditorStyles.helpBox);

            Event evt = Event.current;
            if (!GUI.enabled || !dropRect.Contains(evt.mousePosition) ||
                (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
            {
                return;
            }

            bool valid = false;
            foreach (Object o in DragAndDrop.objectReferences)
            {
                if (o is MeshModifier || o is MeshHideAsset || o is MeshHideAssetCollection)
                {
                    valid = true;
                    break;
                }
            }
            if (!valid)
            {
                return;
            }
            if (!CanOpenMeshEditor(evt.type == EventType.DragPerform))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                if (evt.type == EventType.DragPerform)
                {
                    evt.Use();
                }
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object o in DragAndDrop.objectReferences)
                {
                    // Only the first asset dropped is opened.
                    if (o is MeshModifier mm)
                    {
                        thisDCA.ignoreMeshHideAssets = true;
                        thisDCA.GenerateNow();
                        MeshModifier = mm;
                        VertexEditorStage.ShowStage(thisDCA, MeshModifier);
                        break;
                    }
                    if (o is MeshHideAsset mha)
                    {
                        thisDCA.ignoreMeshHideAssets = true;
                        thisDCA.GenerateNow();
                        FaceEditorStage.ShowStage(thisDCA, mha);
                        break;
                    }
                    if (o is MeshHideAssetCollection mhac)
                    {
                        thisDCA.ignoreMeshHideAssets = true;
                        thisDCA.GenerateNow();
                        FaceEditorStage.ShowStage(thisDCA, mhac);
                        break;
                    }
                }
            }
            evt.Use();
        }
    }
}
