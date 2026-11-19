using System;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Remembers the Vertex Editor's tool choices: the workflow (Sculpt, Vertex Paint, Advanced or
    /// Blendshapes), the display options (Original Materials and Vertex Colors; Wireframe keeps its
    /// own key), the Sculpt and Vertex Paint brushes with their size, effect, falloff and options,
    /// and the Advanced tool, tab, mode and action. They are written to EditorPrefs when they change
    /// and restored every time the Vertex Editor opens, so they last through closing the editor, a
    /// domain reload and an Editor restart.
    /// </summary>
    /// <remarks>
    /// Only the Vertex Editor itself does this. The slot weight editors and Touchup Weights use the
    /// same stage but open on their own tools, and leave the stored choices alone.
    /// </remarks>
    public partial class VertexEditorStage
    {
        // Set when the Vertex Editor opens. A new stage object is made every time it opens.
        [NonSerialized] private bool persistToolSettings;
        [NonSerialized] private string savedToolSettingsJson;
        // Sculpt and Vertex Paint replace the Advanced selection tool, so the one last used in
        // Advanced is kept here while another workflow is active.
        [NonSerialized] private AdvancedTool rememberedAdvancedTool = AdvancedTool.Select;
        // The Advanced tab, for a stage without a Mesh Modifiers window (it normally lives there).
        [NonSerialized] private MeshModifierEditor.EditorMode rememberedAdvancedMode = MeshModifierEditor.EditorMode.VertexAdjustments;

        /// <summary>The current tool choices, as <see cref="RestoreToolSettingsOnOpen"/> stores them.</summary>
        public VertexEditorToolSettings CaptureToolSettings()
        {
            if (sceneToolMode == SceneToolMode.Select || sceneToolMode == SceneToolMode.SelectionBrush)
            {
                rememberedAdvancedTool = CurrentAdvancedTool;
            }
            if (modifierEditor != null)
            {
                rememberedAdvancedMode = modifierEditor.AdvancedEditorMode;
            }

            string textureGuid = string.Empty;
            if (vertexPaintBrushTexture != null &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(vertexPaintBrushTexture, out string guid, out long _))
            {
                textureGuid = guid;
            }

            return new VertexEditorToolSettings
            {
                showOriginalMaterials = VertexEditorToolSettings.WriteBool(showOriginalMaterials),
                showVertexColors = VertexEditorToolSettings.WriteBool(showVertexColorsView),

                workflow = CurrentAuthoringWorkflow().ToString(),
                advancedMode = rememberedAdvancedMode.ToString(),
                advancedTool = rememberedAdvancedTool.ToString(),
                defineMode = currentDefineMode.ToString(),
                selectAction = currentMode.ToString(),
                selectionBrushRadiusPixels = selectionBrushRadiusPixels,

                sculptTool = sculptTool.ToString(),
                sculptMaskTool = sculptMaskTool.ToString(),
                sculptFalloff = sculptFalloff.ToString(),
                sculptCustomFalloff = VertexEditorToolSettings.CopyCurve(sculptCustomFalloff),
                sculptRadius = sculptRadius,
                sculptStrengthPercent = sculptStrengthPercent,
                sculptPinchStrength = sculptPinchStrength,
                sculptCreaseDepth = sculptCreaseDepth,
                sculptPlaneMode = sculptPlaneMode.ToString(),
                sculptBoundaryMode = sculptBoundaryMode.ToString(),
                sculptElasticMode = sculptElasticMode.ToString(),
                sculptElasticVolumePreservation = sculptElasticVolumePreservation,
                sculptSymmetryX = VertexEditorToolSettings.WriteBool(sculptSymmetryX),
                sculptConnectedOnly = VertexEditorToolSettings.WriteBool(sculptConnectedOnly),
                sculptUpdateNormalsWhileSculpting = VertexEditorToolSettings.WriteBool(sculptUpdateNormalsWhileSculpting),
                sculptFrontFacesOnly = VertexEditorToolSettings.WriteBool(sculptFrontFacesOnly),
                showSculptNormals = VertexEditorToolSettings.WriteBool(showSculptNormals),
                sculptNormalLength = sculptNormalLength,
                sculptUVLockBorders = VertexEditorToolSettings.WriteBool(sculptUVLockBorders),
                sculptBeautifyEvenSpacing = sculptBeautifyEvenSpacing,
                sculptBeautifyKeepShape = VertexEditorToolSettings.WriteBool(sculptBeautifyKeepShape),
                sculptBeautifyKeepTexture = VertexEditorToolSettings.WriteBool(sculptBeautifyKeepTexture),
                brushIgnoresLockedSlots = VertexEditorToolSettings.WriteBool(brushIgnoresLockedSlots),

                vertexPaintTool = vertexPaintTool.ToString(),
                vertexPaintBrushShape = vertexPaintBrushShape.ToString(),
                vertexPaintBrushColor = VertexEditorToolSettings.WriteColor(vertexPaintBrushColor),
                vertexPaintBrushTextureGuid = textureGuid,
                vertexPaintBlend = vertexPaintBlend.ToString(),
                vertexPaintRadius = vertexPaintRadius,
                vertexPaintStrengthPercent = vertexPaintStrengthPercent,
                vertexPaintFalloff = vertexPaintFalloff.ToString(),
                vertexPaintCustomFalloff = VertexEditorToolSettings.CopyCurve(vertexPaintCustomFalloff),
                vertexPaintSmoothRadius = vertexPaintSmoothRadius,
                vertexPaintSymmetryX = VertexEditorToolSettings.WriteBool(vertexPaintSymmetryX),
                vertexPaintAffectAlpha = VertexEditorToolSettings.WriteBool(vertexPaintAffectAlpha),
                vertexPaintNoiseType = vertexPaintNoiseType.ToString(),
                vertexPaintNoiseScale = vertexPaintNoiseScale,
                vertexPaintNoiseAmount = vertexPaintNoiseAmount,
                vertexPaintNoiseColor = VertexEditorToolSettings.WriteBool(vertexPaintNoiseColor),
                vertexPaintNoiseRepeat = VertexEditorToolSettings.WriteBool(vertexPaintNoiseRepeat),
                vertexPaintNoiseSeed = vertexPaintNoiseSeed,
            };
        }

        /// <summary>
        /// Sets the brushes, their settings and the Advanced choices from <paramref name="settings"/>.
        /// A value that is missing, unknown or out of range leaves that setting as it is (on a newly
        /// opened editor, its default). The workflow is not changed here; the editor switches to the
        /// stored workflow itself when it opens.
        /// </summary>
        public void ApplyToolSettings(VertexEditorToolSettings settings)
        {
            if (settings == null)
            {
                return;
            }
            const float minRadius = StageBrushSize.MinRadiusMm / 1000f;
            const float maxRadius = StageBrushSize.MaxRadiusMm / 1000f;

            // Display: the model is drawn one way or the other, as the two switches enforce.
            showOriginalMaterials = VertexEditorToolSettings.ReadBool(settings.showOriginalMaterials, showOriginalMaterials);
            showVertexColorsView = !showOriginalMaterials && VertexEditorToolSettings.ReadBool(settings.showVertexColors, showVertexColorsView);

            // Advanced: the action has to belong to the mode (Add/Remove/Invert define a set,
            // Activate/Deactivate/Toggle define a state). Otherwise the mode's first action is used,
            // as when the mode is chosen on the toolbar.
            rememberedAdvancedTool = VertexEditorToolSettings.ReadEnum(settings.advancedTool, rememberedAdvancedTool);
            MeshModifierEditor.EditorMode advancedMode = VertexEditorToolSettings.ReadEnum(settings.advancedMode, rememberedAdvancedMode);
            if (advancedMode == MeshModifierEditor.EditorMode.VertexAdjustments || advancedMode == MeshModifierEditor.EditorMode.MeshModifiers)
            {
                rememberedAdvancedMode = advancedMode;
            }
            currentDefineMode = VertexEditorToolSettings.ReadEnum(settings.defineMode, currentDefineMode);
            selectMode defaultAction = DefaultSelectAction(currentDefineMode);
            selectMode action = VertexEditorToolSettings.ReadEnum(settings.selectAction, IsSelectActionOf(currentMode, currentDefineMode) ? currentMode : defaultAction);
            currentMode = IsSelectActionOf(action, currentDefineMode) ? action : defaultAction;
            selectionBrushRadiusPixels = VertexEditorToolSettings.ReadInt(
                settings.selectionBrushRadiusPixels, MinSelectionBrushRadiusPixels, MaxSelectionBrushRadiusPixels, selectionBrushRadiusPixels);

            sculptTool = VertexEditorToolSettings.ReadEnum(settings.sculptTool, sculptTool);
            sculptMaskTool = VertexEditorToolSettings.ReadEnum(settings.sculptMaskTool, sculptMaskTool);
            sculptFalloff = VertexEditorToolSettings.ReadEnum(settings.sculptFalloff, sculptFalloff);
            sculptCustomFalloff = VertexEditorToolSettings.ReadCurve(settings.sculptCustomFalloff, sculptCustomFalloff);
            sculptRadius = VertexEditorToolSettings.ReadFloat(settings.sculptRadius, minRadius, maxRadius, sculptRadius);
            sculptStrengthPercent = VertexEditorToolSettings.ReadFloat(settings.sculptStrengthPercent, 0f, 100f, sculptStrengthPercent);
            sculptPinchStrength = VertexEditorToolSettings.ReadFloat(settings.sculptPinchStrength, 0f, 2f, sculptPinchStrength);
            sculptCreaseDepth = VertexEditorToolSettings.ReadFloat(settings.sculptCreaseDepth, -1f, 1f, sculptCreaseDepth);
            sculptPlaneMode = VertexEditorToolSettings.ReadEnum(settings.sculptPlaneMode, sculptPlaneMode);
            sculptBoundaryMode = VertexEditorToolSettings.ReadEnum(settings.sculptBoundaryMode, sculptBoundaryMode);
            sculptElasticMode = VertexEditorToolSettings.ReadEnum(settings.sculptElasticMode, sculptElasticMode);
            sculptElasticVolumePreservation = VertexEditorToolSettings.ReadFloat(settings.sculptElasticVolumePreservation, 0f, 1f, sculptElasticVolumePreservation);
            sculptSymmetryX = VertexEditorToolSettings.ReadBool(settings.sculptSymmetryX, sculptSymmetryX);
            sculptConnectedOnly = VertexEditorToolSettings.ReadBool(settings.sculptConnectedOnly, sculptConnectedOnly);
            sculptUpdateNormalsWhileSculpting = VertexEditorToolSettings.ReadBool(settings.sculptUpdateNormalsWhileSculpting, sculptUpdateNormalsWhileSculpting);
            sculptFrontFacesOnly = VertexEditorToolSettings.ReadBool(settings.sculptFrontFacesOnly, sculptFrontFacesOnly);
            showSculptNormals = VertexEditorToolSettings.ReadBool(settings.showSculptNormals, showSculptNormals);
            sculptNormalLength = VertexEditorToolSettings.ReadFloat(settings.sculptNormalLength, MinNormalLength, MaxNormalLength, sculptNormalLength);
            sculptUVLockBorders = VertexEditorToolSettings.ReadBool(settings.sculptUVLockBorders, sculptUVLockBorders);
            sculptBeautifyEvenSpacing = VertexEditorToolSettings.ReadFloat(settings.sculptBeautifyEvenSpacing, 0f, 1f, sculptBeautifyEvenSpacing);
            sculptBeautifyKeepShape = VertexEditorToolSettings.ReadBool(settings.sculptBeautifyKeepShape, sculptBeautifyKeepShape);
            sculptBeautifyKeepTexture = VertexEditorToolSettings.ReadBool(settings.sculptBeautifyKeepTexture, sculptBeautifyKeepTexture);
            brushIgnoresLockedSlots = VertexEditorToolSettings.ReadBool(settings.brushIgnoresLockedSlots, brushIgnoresLockedSlots);

            vertexPaintTool = VertexEditorToolSettings.ReadEnum(settings.vertexPaintTool, vertexPaintTool);
            vertexPaintBrushShape = VertexEditorToolSettings.ReadEnum(settings.vertexPaintBrushShape, vertexPaintBrushShape);
            vertexPaintBrushColor = VertexEditorToolSettings.ReadColor(settings.vertexPaintBrushColor, vertexPaintBrushColor);
            if (!string.IsNullOrEmpty(settings.vertexPaintBrushTextureGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(settings.vertexPaintBrushTextureGuid);
                Texture2D texture = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null && texture != vertexPaintBrushTexture)
                {
                    vertexPaintBrushTexture = texture;
                    DestroyVertexPaintBrushCache();
                }
            }
            vertexPaintBlend = VertexEditorToolSettings.ReadEnum(settings.vertexPaintBlend, vertexPaintBlend);
            vertexPaintRadius = VertexEditorToolSettings.ReadFloat(settings.vertexPaintRadius, minRadius, maxRadius, vertexPaintRadius);
            vertexPaintStrengthPercent = VertexEditorToolSettings.ReadFloat(settings.vertexPaintStrengthPercent, 0f, 100f, vertexPaintStrengthPercent);
            vertexPaintFalloff = VertexEditorToolSettings.ReadEnum(settings.vertexPaintFalloff, vertexPaintFalloff);
            vertexPaintCustomFalloff = VertexEditorToolSettings.ReadCurve(settings.vertexPaintCustomFalloff, vertexPaintCustomFalloff);
            vertexPaintSmoothRadius = VertexEditorToolSettings.ReadFloat(settings.vertexPaintSmoothRadius, 0.001f, 0.5f, vertexPaintSmoothRadius);
            vertexPaintSymmetryX = VertexEditorToolSettings.ReadBool(settings.vertexPaintSymmetryX, vertexPaintSymmetryX);
            vertexPaintAffectAlpha = VertexEditorToolSettings.ReadBool(settings.vertexPaintAffectAlpha, vertexPaintAffectAlpha);
            vertexPaintNoiseType = VertexEditorToolSettings.ReadEnum(settings.vertexPaintNoiseType, vertexPaintNoiseType);
            vertexPaintNoiseScale = VertexEditorToolSettings.ReadFloat(settings.vertexPaintNoiseScale, 0.1f, 100f, vertexPaintNoiseScale);
            vertexPaintNoiseAmount = VertexEditorToolSettings.ReadFloat(settings.vertexPaintNoiseAmount, 0f, 1f, vertexPaintNoiseAmount);
            vertexPaintNoiseColor = VertexEditorToolSettings.ReadBool(settings.vertexPaintNoiseColor, vertexPaintNoiseColor);
            vertexPaintNoiseRepeat = VertexEditorToolSettings.ReadBool(settings.vertexPaintNoiseRepeat, vertexPaintNoiseRepeat);
            vertexPaintNoiseSeed = VertexEditorToolSettings.ReadInt(settings.vertexPaintNoiseSeed, int.MinValue + 1, int.MaxValue, vertexPaintNoiseSeed);
        }

        private static selectMode DefaultSelectAction(DefineMode mode)
        {
            return mode == DefineMode.DefineVertexSet ? selectMode.Add : selectMode.ToggleState;
        }

        private static bool IsSelectActionOf(selectMode action, DefineMode mode)
        {
            bool setAction = action == selectMode.Add || action == selectMode.Remove || action == selectMode.InvertSelection;
            return mode == DefineMode.DefineVertexSet ? setAction : !setAction;
        }

        /// <summary>The workflow the editor shows, as the workflow bar draws it.</summary>
        private MeshModifierEditor.AuthoringWorkflow CurrentAuthoringWorkflow()
        {
            switch (sceneToolMode)
            {
                case SceneToolMode.Sculpt:
                    return MeshModifierEditor.AuthoringWorkflow.Sculpt;
                case SceneToolMode.VertexPaint:
                    return MeshModifierEditor.AuthoringWorkflow.VertexPaint;
            }
            return modifierEditor != null && modifierEditor.IsBlendshapeExtractionWorkflow
                ? MeshModifierEditor.AuthoringWorkflow.BlendshapeExtraction
                : MeshModifierEditor.AuthoringWorkflow.AdvancedVertexAdjustments;
        }

        /// <summary>
        /// Called once the Vertex Editor has opened (its mesh and Mesh Modifiers window exist):
        /// restores the stored choices and starts saving changes to them.
        /// </summary>
        private void RestoreToolSettingsOnOpen()
        {
            persistToolSettings = true;
            VertexEditorToolSettings settings = VertexEditorToolSettings.Load();
            if (settings != null)
            {
                ApplyToolSettings(settings);
                if (modifierEditor != null)
                {
                    modifierEditor.AdvancedEditorMode = rememberedAdvancedMode;
                }
                MeshModifierEditor.AuthoringWorkflow workflow =
                    VertexEditorToolSettings.ReadEnum(settings.workflow, MeshModifierEditor.AuthoringWorkflow.Sculpt);
                SelectAuthoringWorkflow(workflow);
                if (workflow == MeshModifierEditor.AuthoringWorkflow.AdvancedVertexAdjustments &&
                    rememberedAdvancedTool != AdvancedTool.Select)
                {
                    SetAdvancedTool(rememberedAdvancedTool);
                }
                // Draw the model the way it was left.
                if (showVertexColorsView && !vertexPaintPreviewInitialized && BakedMesh != null)
                {
                    InitializeVertexPaintPreview();
                }
                ApplyVertexDisplayOptions();
            }
            // What is stored now; nothing is written until a choice changes.
            savedToolSettingsJson = CaptureToolSettings().ToJson();
        }

        /// <summary>Switches workflow the way the workflow bar does, without an Undo step.</summary>
        private void SelectAuthoringWorkflow(MeshModifierEditor.AuthoringWorkflow workflow)
        {
            if (modifierEditor != null)
            {
                modifierEditor.SetAuthoringWorkflowWithoutUndo(workflow);
                return;
            }
            switch (workflow)
            {
                case MeshModifierEditor.AuthoringWorkflow.Sculpt:
                    ChangeSceneToolMode(SceneToolMode.Sculpt);
                    break;
                case MeshModifierEditor.AuthoringWorkflow.VertexPaint:
                    ChangeSceneToolMode(SceneToolMode.VertexPaint);
                    break;
                default:
                    ChangeSceneToolMode(SceneToolMode.Select);
                    break;
            }
        }

        /// <summary>Editing a blendshape is done in Sculpt, whichever workflow the editor opened on.</summary>
        private void ShowSculptForBlendshapeEdit()
        {
            if (sceneToolMode != SceneToolMode.Sculpt)
            {
                SelectAuthoringWorkflow(MeshModifierEditor.AuthoringWorkflow.Sculpt);
            }
        }

        /// <summary>Writes the choices to EditorPrefs when they differ from what was last stored.</summary>
        private void SaveToolSettingsIfChanged()
        {
            if (!persistToolSettings)
            {
                return;
            }
            string json = CaptureToolSettings().ToJson();
            if (string.Equals(json, savedToolSettingsJson, StringComparison.Ordinal))
            {
                return;
            }
            savedToolSettingsJson = json;
            EditorPrefs.SetString(VertexEditorToolSettings.PrefsKey, json);
        }
    }
}
