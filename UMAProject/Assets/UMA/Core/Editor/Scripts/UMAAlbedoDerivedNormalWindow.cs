using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors.TextureUtilities;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Builds a normal map for a modified albedo while preserving the normals from
    /// the matching reference face.
    /// </summary>
    public sealed class UMAAlbedoDerivedNormalWindow : EditorWindow
    {
        private enum HeightSource
        {
            Luminance,
            RedOnly,
            GreenAndBlue,
        }

        private enum MaskChannel
        {
            Alpha,
            Luminance,
            Red,
            Green,
            Blue,
        }

        private enum PreviewMode
        {
            LitResult,
            NormalMap,
            DerivedHeight,
            EffectMask,
            ModifiedAlbedo,
        }

        [Serializable]
        private sealed class PersistedSettings
        {
            public int version = 1;
            public string referenceAlbedoReference;
            public string referenceNormalReference;
            public string modifiedAlbedoReference;
            public string effectMaskReference;
            public string maskRaceReference;
            public string maskSlotReference;
            public int slotMaskEdgeSize;
            public int slotMaskBlurRadius;
            public int normalDecodeMode;
            public int heightSource;
            public int maskChannel;
            public float bumpiness;
            public float differenceGain;
            public float differenceThreshold;
            public float smoothingRadius;
            public bool invertHeightDirection;
            public bool invertMask;
            public float previewLightContrast;
            public Vector3 previewLightDirection;
            public int previewMode;
            public bool liveGeneration;
            public string lastSavedPath;
        }

        private const string SettingsKey = "UMA.AlbedoDerivedNormalWindow.Settings.v1";
        private const string GeneratorShaderName = "Hidden/UMA/AlbedoDerivedNormal";
        private const string PreviewShaderName = "Hidden/UMA/TextureUtilitiesRawNormalPreview";

        [SerializeField] private Texture2D referenceAlbedo;
        [SerializeField] private Texture2D referenceNormal;
        [SerializeField] private Texture2D modifiedAlbedo;
        [SerializeField] private Texture2D effectMask;
        [SerializeField] private RaceData maskRace;
        [SerializeField] private SlotDataAsset maskSlot;
        [SerializeField] private int slotMaskEdgeSize = 8;
        [SerializeField] private int slotMaskBlurRadius = 8;
        [SerializeField] private NormalMapDecodeMode normalDecodeMode = NormalMapDecodeMode.Auto;
        [SerializeField] private HeightSource heightSource = HeightSource.Luminance;
        [SerializeField] private MaskChannel maskChannel = MaskChannel.Alpha;
        [SerializeField] private float bumpiness = 4f;
        [SerializeField] private float differenceGain = 1f;
        [SerializeField] private float differenceThreshold = 0.005f;
        [SerializeField] private float smoothingRadius = 1f;
        [SerializeField] private bool invertHeightDirection;
        [SerializeField] private bool invertMask;
        [SerializeField] private float previewLightContrast = 1.25f;
        [SerializeField] private Vector3 previewLightDirection = new Vector3(0.35f, 0.45f, 0.82f);
        [SerializeField] private PreviewMode previewMode;
        [SerializeField] private bool liveGeneration;

        private Texture2D generatedNormal;
        private Texture2D generatedSlotMask;
        private Material generatorMaterial;
        private Material previewMaterial;
        private Vector2 settingsScroll;
        private bool outputIsStale;
        private string lastSavedPath;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;
        private bool settingsLoaded;
        private readonly List<RaceData> maskRaceChoices = new List<RaceData>();
        private readonly List<SlotDataAsset> maskSlotChoices = new List<SlotDataAsset>();
        private string[] maskRaceOptionNames = { "Select Race..." };
        private string[] maskSlotOptionNames = { "Select Slot..." };
        private RaceData slotChoicesRace;
        private string raceChoicesMessage;
        private string slotChoicesMessage;
        private bool liveGenerationPending;
        private bool liveSlotMaskGenerationPending;
        private double lastLiveGenerationTime;

        [MenuItem("UMA/Textures/Generate Normal From Albedo Changes", priority = 126)]
        public static void Open()
        {
            UMAAlbedoDerivedNormalWindow window = GetWindow<UMAAlbedoDerivedNormalWindow>();
            window.titleContent = new GUIContent("UMA Albedo to Normal");
            window.minSize = new Vector2(780f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
            settingsLoaded = true;
            RefreshRaceChoices();
            EditorApplication.projectChanged += HandleProjectChanged;
            previewLightDirection = previewLightDirection.sqrMagnitude > 0.001f
                ? previewLightDirection.normalized
                : new Vector3(0.35f, 0.45f, 0.82f).normalized;
            if (liveGeneration)
            {
                QueueLiveGeneration(maskRace != null && maskSlot != null && effectMask == null);
            }
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= HandleProjectChanged;
            EditorApplication.update -= ProcessLiveGeneration;
            if (settingsLoaded)
            {
                SaveSettings();
            }
            DestroyImmediateSafe(ref generatedNormal);
            DestroyImmediateSafe(ref generatedSlotMask);
            DestroyImmediateSafe(ref generatorMaterial);
            DestroyImmediateSafe(ref previewMaterial);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawSettingsPanel();
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();

            if (GUI.changed)
            {
                SaveSettings();
            }
        }

        private void DrawSettingsPanel()
        {
            bool generateSlotMaskRequested = false;
            bool saveMaskRequested = false;
            Texture2D previousReferenceAlbedo = referenceAlbedo;
            Texture2D previousReferenceNormal = referenceNormal;
            Texture2D previousModifiedAlbedo = modifiedAlbedo;
            Texture2D previousEffectMask = effectMask;
            RaceData previousMaskRace = maskRace;
            SlotDataAsset previousMaskSlot = maskSlot;
            int previousSlotMaskEdgeSize = slotMaskEdgeSize;
            int previousSlotMaskBlurRadius = slotMaskBlurRadius;
            NormalMapDecodeMode previousNormalDecodeMode = normalDecodeMode;
            HeightSource previousHeightSource = heightSource;
            MaskChannel previousMaskChannel = maskChannel;
            float previousBumpiness = bumpiness;
            float previousDifferenceGain = differenceGain;
            float previousDifferenceThreshold = differenceThreshold;
            float previousSmoothingRadius = smoothingRadius;
            bool previousInvertHeightDirection = invertHeightDirection;
            bool previousInvertMask = invertMask;

            EditorGUILayout.BeginVertical(GUILayout.Width(350f), GUILayout.ExpandHeight(true));
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);

            EditorGUILayout.LabelField("Normal Map From Albedo Changes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use a matching reference face and normal map, then supply the modified face. " +
                "The utility converts local albedo changes into height detail and combines that detail with the reference normal.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Reference Maps", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            referenceAlbedo = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Reference Albedo", "The original face albedo before the edits."),
                referenceAlbedo, typeof(Texture2D), false);
            referenceNormal = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Reference Normal", "The normal map that matches the reference albedo. Its dimensions define the output size."),
                referenceNormal, typeof(Texture2D), false);
            normalDecodeMode = (NormalMapDecodeMode)EditorGUILayout.EnumPopup(
                new GUIContent("Normal Encoding", "Auto uses Unity normal decoding for imported normal maps and detects common raw RGB or DXT5nm textures."),
                normalDecodeMode);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Modified Maps", EditorStyles.boldLabel);
            modifiedAlbedo = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Modified Albedo", "The edited face albedo for which a matching normal will be generated."),
                modifiedAlbedo, typeof(Texture2D), false);
            effectMask = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Mask (Optional)", "Controls how much generated detail is applied. With Alpha selected, this behaves like an UMA overlay alpha mask."),
                effectMask, typeof(Texture2D), false);
            if (effectMask != null)
            {
                maskChannel = (MaskChannel)EditorGUILayout.EnumPopup("Mask Channel", maskChannel);
                invertMask = EditorGUILayout.Toggle("Invert Mask", invertMask);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Mask From Slot Triangles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Rasterizes the selected slot's UV triangles into a mask. The inset removes detail near UV-island edges, " +
                "and the blur softens the transition into the usable interior.",
                MessageType.None);
            DrawMaskRaceAndSlotDropdowns();
            slotMaskEdgeSize = EditorGUILayout.IntSlider(
                new GUIContent("Edge Size (Pixels)", "Number of pixels to mask inward from each UV-island edge."),
                slotMaskEdgeSize, 0, 256);
            slotMaskBlurRadius = EditorGUILayout.IntSlider(
                new GUIContent("Blur Radius (Pixels)", "Width of the soft transition after the masked edge."),
                slotMaskBlurRadius, 0, 128);

            GetSlotMaskDimensions(out int maskWidth, out int maskHeight, out string maskSizeSource);
            EditorGUILayout.LabelField(
                "Generated Size",
                maskWidth + " x " + maskHeight + " (" + maskSizeSource + ")",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(maskRace == null || maskSlot == null))
            {
                if (GUILayout.Button("Generate Mask From Slot Triangles", GUILayout.Height(27f)))
                {
                    generateSlotMaskRequested = true;
                }
            }
            using (new EditorGUI.DisabledScope(effectMask == null))
            {
                if (GUILayout.Button(
                    new GUIContent(
                        "Save Mask...",
                        "Save the active mask as a grayscale PNG and use the imported persistent texture."),
                    GUILayout.Height(24f)))
                {
                    saveMaskRequested = true;
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Height Interpretation", EditorStyles.boldLabel);
            heightSource = (HeightSource)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Albedo Source",
                    "Luminance uses visible RGB brightness. Red Only compares red. Green And Blue averages the two non-red channels, which is useful for detecting red scars on skin."),
                heightSource);
            invertHeightDirection = EditorGUILayout.Toggle(
                new GUIContent("Invert Direction", "Off means darker changes are lower and lighter changes are higher."),
                invertHeightDirection);
            bumpiness = EditorGUILayout.Slider(
                new GUIContent("Bumpiness", "Strength of the normal detail created from the albedo difference."),
                bumpiness, 0f, 20f);
            differenceGain = EditorGUILayout.Slider(
                new GUIContent("Difference Gain", "Amplifies or reduces the height difference before normals are calculated."),
                differenceGain, 0f, 4f);
            differenceThreshold = EditorGUILayout.Slider(
                new GUIContent("Ignore Small Changes", "Suppresses tiny color differences caused by compression or paint noise."),
                differenceThreshold, 0f, 0.1f);
            smoothingRadius = EditorGUILayout.Slider(
                new GUIContent("Smoothing (Pixels)", "Smooths the derived height before calculating its slope. Higher values create broader, softer facial detail."),
                smoothingRadius, 0f, 8f);

            if (EditorGUI.EndChangeCheck())
            {
                outputIsStale = generatedNormal != null;
                statusMessage = null;
                Repaint();

                bool slotMaskSettingsChanged = previousMaskRace != maskRace
                    || previousMaskSlot != maskSlot
                    || previousSlotMaskEdgeSize != slotMaskEdgeSize
                    || previousSlotMaskBlurRadius != slotMaskBlurRadius;
                bool normalSettingsChanged = previousReferenceAlbedo != referenceAlbedo
                    || previousReferenceNormal != referenceNormal
                    || previousModifiedAlbedo != modifiedAlbedo
                    || previousEffectMask != effectMask
                    || previousNormalDecodeMode != normalDecodeMode
                    || previousHeightSource != heightSource
                    || previousMaskChannel != maskChannel
                    || !Mathf.Approximately(previousBumpiness, bumpiness)
                    || !Mathf.Approximately(previousDifferenceGain, differenceGain)
                    || !Mathf.Approximately(previousDifferenceThreshold, differenceThreshold)
                    || !Mathf.Approximately(previousSmoothingRadius, smoothingRadius)
                    || previousInvertHeightDirection != invertHeightDirection
                    || previousInvertMask != invertMask;
                if (liveGeneration && (slotMaskSettingsChanged || normalSettingsChanged))
                {
                    QueueLiveGeneration(slotMaskSettingsChanged);
                }
            }

            if (generateSlotMaskRequested)
            {
                GenerateMaskFromSlotTriangles();
                if (liveGeneration)
                {
                    QueueLiveGeneration(false);
                }
            }

            if (saveMaskRequested)
            {
                SaveMask();
            }

            if (GUILayout.Button("Reset Generation Settings"))
            {
                ResetGenerationSettings();
                if (liveGeneration)
                {
                    QueueLiveGeneration(false);
                }
            }

            DrawInputWarnings();

            EditorGUILayout.Space(10f);
            EditorGUI.BeginChangeCheck();
            liveGeneration = EditorGUILayout.Toggle(
                new GUIContent(
                    "Live Generation",
                    "Regenerate the normal map automatically while inputs and generation settings are changed."),
                liveGeneration);
            if (EditorGUI.EndChangeCheck())
            {
                if (liveGeneration)
                {
                    QueueLiveGeneration(maskRace != null && maskSlot != null && effectMask == null);
                }
                else
                {
                    CancelLiveGeneration();
                }
            }

            using (new EditorGUI.DisabledScope(!CanGenerate()))
            {
                if (GUILayout.Button("Generate Normal Map", GUILayout.Height(32f)))
                {
                    Generate();
                }
            }

            using (new EditorGUI.DisabledScope(generatedNormal == null))
            {
                if (GUILayout.Button("Save Normal Map As PNG...", GUILayout.Height(26f)))
                {
                    SaveGeneratedNormal();
                }

                if (GUILayout.Button(
                    new GUIContent(
                        "Save and Build",
                        "Overwrite the suggested normal beside the modified albedo, then rebuild loaded UMAs that use the modified albedo or saved normal."),
                    GUILayout.Height(30f)))
                {
                    SaveAndBuild();
                }
            }

            if (outputIsStale)
            {
                EditorGUILayout.HelpBox("Inputs or settings changed after the last generation. Generate again to update the result.", MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawInputWarnings()
        {
            if (referenceAlbedo == null || referenceNormal == null || modifiedAlbedo == null)
            {
                EditorGUILayout.HelpBox("Assign the reference albedo, reference normal, and modified albedo to generate.", MessageType.None);
                return;
            }

            float outputAspect = referenceNormal.height > 0 ? (float)referenceNormal.width / referenceNormal.height : 1f;
            bool referenceAspectMismatch = HasAspectMismatch(referenceAlbedo, outputAspect);
            bool modifiedAspectMismatch = HasAspectMismatch(modifiedAlbedo, outputAspect);
            bool maskAspectMismatch = effectMask != null && HasAspectMismatch(effectMask, outputAspect);
            if (referenceAspectMismatch || modifiedAspectMismatch || maskAspectMismatch)
            {
                EditorGUILayout.HelpBox(
                    "One or more maps have a different aspect ratio from the reference normal. They will be resampled by UV, which may misalign the generated detail.",
                    MessageType.Warning);
            }
            else if (referenceAlbedo.width != referenceNormal.width || referenceAlbedo.height != referenceNormal.height
                || modifiedAlbedo.width != referenceNormal.width || modifiedAlbedo.height != referenceNormal.height
                || (effectMask != null && (effectMask.width != referenceNormal.width || effectMask.height != referenceNormal.height)))
            {
                EditorGUILayout.HelpBox(
                    "Source dimensions differ. The maps will be resampled to " + referenceNormal.width + " x " + referenceNormal.height + ".",
                    MessageType.Info);
            }
        }

        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                generatedNormal == null
                    ? "Preview"
                    : "Preview (" + generatedNormal.width + " x " + generatedNormal.height + ")",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!CanGenerate()))
            {
                previewMode = (PreviewMode)EditorGUILayout.EnumPopup(previewMode, GUILayout.Width(145f));
            }
            EditorGUILayout.EndHorizontal();

            if (previewMode == PreviewMode.LitResult && generatedNormal != null)
            {
                EditorGUILayout.BeginHorizontal();
                previewLightContrast = EditorGUILayout.Slider("Light Contrast", previewLightContrast, 0f, 3f);
                if (GUILayout.Button("Reset Light", GUILayout.Width(90f)))
                {
                    previewLightDirection = new Vector3(0.35f, 0.45f, 0.82f).normalized;
                    previewLightContrast = 1.25f;
                }
                EditorGUILayout.EndHorizontal();
            }

            Rect availableRect = GUILayoutUtility.GetRect(
                100f, 10000f, 100f, 10000f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(availableRect, new Color(0.12f, 0.12f, 0.12f, 1f));

            Texture previewTexture = ResolvePreviewTexture();
            if (previewTexture == null)
            {
                GUI.Label(availableRect, "Generate a normal map to preview the result.", CenteredLabelStyle());
                EditorGUILayout.EndVertical();
                return;
            }

            Rect imageRect = FitRect(availableRect, previewTexture.width, previewTexture.height);
            if (previewMode == PreviewMode.LitResult && generatedNormal != null && modifiedAlbedo != null)
            {
                Material material = GetPreviewMaterial();
                if (material != null)
                {
                    material.SetTexture("_BaseMap", modifiedAlbedo);
                    material.SetTexture("_BumpMap", generatedNormal);
                    material.SetVector("_LightDir", previewLightDirection.normalized);
                    material.SetFloat("_LightContrast", previewLightContrast);
                    EditorGUI.DrawPreviewTexture(imageRect, generatedNormal, material, ScaleMode.StretchToFill);
                }
                else
                {
                    EditorGUI.DrawPreviewTexture(imageRect, generatedNormal, null, ScaleMode.StretchToFill);
                }
            }
            else if ((previewMode == PreviewMode.DerivedHeight || previewMode == PreviewMode.EffectMask) && CanGenerate())
            {
                Material material = GetGeneratorMaterial();
                if (material != null)
                {
                    ConfigureGeneratorMaterial(material, referenceNormal.width, referenceNormal.height);
                    material.SetFloat("_OutputMode", previewMode == PreviewMode.DerivedHeight ? 1f : 2f);
                    EditorGUI.DrawPreviewTexture(imageRect, referenceAlbedo, material, ScaleMode.StretchToFill);
                }
            }
            else
            {
                EditorGUI.DrawPreviewTexture(imageRect, previewTexture, null, ScaleMode.StretchToFill);
            }

            EditorGUILayout.EndVertical();
        }

        private Texture ResolvePreviewTexture()
        {
            switch (previewMode)
            {
                case PreviewMode.ModifiedAlbedo:
                    return modifiedAlbedo;
                case PreviewMode.DerivedHeight:
                    return CanGenerate() ? referenceAlbedo : null;
                case PreviewMode.EffectMask:
                    return CanGenerate() ? referenceAlbedo : effectMask;
                case PreviewMode.NormalMap:
                case PreviewMode.LitResult:
                default:
                    return generatedNormal;
            }
        }

        private bool CanGenerate()
        {
            return referenceAlbedo != null
                && referenceNormal != null
                && modifiedAlbedo != null
                && referenceNormal.width > 0
                && referenceNormal.height > 0;
        }

        private void QueueLiveGeneration(bool regenerateSlotMask)
        {
            if (!liveGeneration)
            {
                return;
            }

            liveGenerationPending = true;
            liveSlotMaskGenerationPending |= regenerateSlotMask;
            EditorApplication.update -= ProcessLiveGeneration;
            EditorApplication.update += ProcessLiveGeneration;
        }

        private void ProcessLiveGeneration()
        {
            if (!liveGeneration || !liveGenerationPending)
            {
                CancelLiveGeneration();
                return;
            }

            const double MinimumGenerationInterval = 0.1d;
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - lastLiveGenerationTime < MinimumGenerationInterval)
            {
                return;
            }

            bool regenerateSlotMask = liveSlotMaskGenerationPending;
            liveGenerationPending = false;
            liveSlotMaskGenerationPending = false;
            EditorApplication.update -= ProcessLiveGeneration;
            lastLiveGenerationTime = currentTime;

            if (regenerateSlotMask && maskRace != null && maskSlot != null)
            {
                GenerateMaskFromSlotTriangles(false);
            }

            if (CanGenerate())
            {
                Generate();
            }
        }

        private void CancelLiveGeneration()
        {
            liveGenerationPending = false;
            liveSlotMaskGenerationPending = false;
            EditorApplication.update -= ProcessLiveGeneration;
        }

        private void GenerateMaskFromSlotTriangles(bool showProgress = true)
        {
            if (maskRace == null || maskSlot == null)
            {
                return;
            }

            UMAMeshData meshData = maskSlot.meshData;
            if (meshData == null || meshData.uv == null || meshData.uv.Length == 0)
            {
                SetStatus("The selected slot has no UV0 data from which to generate a mask.", MessageType.Error);
                return;
            }

            if (meshData.submeshes == null
                || maskSlot.subMeshIndex < 0
                || maskSlot.subMeshIndex >= meshData.submeshes.Length
                || meshData.submeshes[maskSlot.subMeshIndex] == null)
            {
                SetStatus("The selected slot has no valid triangle data at its configured submesh index.", MessageType.Error);
                return;
            }

            int[] triangles = meshData.submeshes[maskSlot.subMeshIndex].GetBaseTriangles();
            if (triangles == null || triangles.Length < 3)
            {
                SetStatus("The selected slot's base LOD contains no triangles.", MessageType.Error);
                return;
            }

            GetSlotMaskDimensions(out int width, out int height, out _);
            byte[] coverage = new byte[width * height];
            int validTriangleCount = 0;
            int invalidTriangleCount = 0;

            try
            {
                int triangleCount = triangles.Length / 3;
                for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
                {
                    if (showProgress && (triangleIndex & 255) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Generating Slot Mask",
                            "Rasterizing " + maskSlot.name + " UV triangles...",
                            triangleCount > 0 ? (float)triangleIndex / triangleCount * 0.55f : 0f);
                    }

                    int offset = triangleIndex * 3;
                    int index0 = triangles[offset];
                    int index1 = triangles[offset + 1];
                    int index2 = triangles[offset + 2];
                    if ((uint)index0 >= (uint)meshData.uv.Length
                        || (uint)index1 >= (uint)meshData.uv.Length
                        || (uint)index2 >= (uint)meshData.uv.Length)
                    {
                        invalidTriangleCount++;
                        continue;
                    }

                    if (RasterizeUvTriangle(
                        coverage,
                        width,
                        height,
                        meshData.uv[index0],
                        meshData.uv[index1],
                        meshData.uv[index2]))
                    {
                        validTriangleCount++;
                    }
                }

                if (validTriangleCount == 0 || !ContainsCoveredPixel(coverage))
                {
                    SetStatus(
                        "No UV triangles from the selected slot overlap the 0-1 texture area.",
                        MessageType.Error);
                    return;
                }

                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar(
                        "Generating Slot Mask",
                        "Masking and softening UV-island edges...",
                        0.65f);
                }
                ApplyInsetAndBlur(
                    coverage,
                    width,
                    height,
                    Mathf.Max(0, slotMaskEdgeSize),
                    Mathf.Max(0, slotMaskBlurRadius));

                Texture2D result = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = maskSlot.name + "_TriangleMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                result.SetPixelData(coverage, 0);
                result.Apply(false, false);

                DestroyImmediateSafe(ref generatedSlotMask);
                generatedSlotMask = result;
                effectMask = generatedSlotMask;
                maskChannel = MaskChannel.Red;
                invertMask = false;
                outputIsStale = generatedNormal != null;
                if (showProgress)
                {
                    previewMode = PreviewMode.EffectMask;
                }

                string skippedMessage = invalidTriangleCount > 0
                    ? " Skipped " + invalidTriangleCount + " triangle"
                        + (invalidTriangleCount == 1 ? "" : "s") + " with invalid vertex indices."
                    : string.Empty;
                SetStatus(
                    "Generated a " + width + " x " + height + " mask from "
                    + validTriangleCount + " slot triangle" + (validTriangleCount == 1 ? "." : "s.")
                    + skippedMessage,
                    invalidTriangleCount > 0 ? MessageType.Warning : MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("Slot mask generation failed: " + exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private void GetSlotMaskDimensions(out int width, out int height, out string source)
        {
            Texture2D sizeTexture = referenceNormal != null ? referenceNormal : modifiedAlbedo;
            if (sizeTexture != null && sizeTexture.width > 0 && sizeTexture.height > 0)
            {
                width = sizeTexture.width;
                height = sizeTexture.height;
                source = referenceNormal != null ? "Reference Normal" : "Modified Albedo";
                return;
            }

            width = 1024;
            height = 1024;
            source = "Default";
        }

        private void DrawMaskRaceAndSlotDropdowns()
        {
            if (maskRaceChoices.Count == 0 && string.IsNullOrEmpty(raceChoicesMessage))
            {
                RefreshRaceChoices();
            }

            int currentRaceIndex = IndexOf(maskRaceChoices, maskRace) + 1;
            int selectedRaceIndex = EditorGUILayout.Popup(
                new GUIContent("Race", "Select a race from the UMA asset index."),
                currentRaceIndex,
                maskRaceOptionNames);
            if (selectedRaceIndex != currentRaceIndex)
            {
                maskRace = selectedRaceIndex > 0
                    ? maskRaceChoices[selectedRaceIndex - 1]
                    : null;
                RefreshSlotChoices();
            }
            else if (slotChoicesRace != maskRace)
            {
                RefreshSlotChoices();
            }

            if (!string.IsNullOrEmpty(raceChoicesMessage))
            {
                EditorGUILayout.HelpBox(raceChoicesMessage, MessageType.Warning);
            }

            int currentSlotIndex = IndexOf(maskSlotChoices, maskSlot) + 1;
            using (new EditorGUI.DisabledScope(maskRace == null || maskSlotChoices.Count == 0))
            {
                int selectedSlotIndex = EditorGUILayout.Popup(
                    new GUIContent(
                        "Slot",
                        "Select one of the SlotDataAssets used by the race's base recipe."),
                    currentSlotIndex,
                    maskSlotOptionNames);
                if (selectedSlotIndex != currentSlotIndex)
                {
                    maskSlot = selectedSlotIndex > 0
                        ? maskSlotChoices[selectedSlotIndex - 1]
                        : null;
                }
            }

            if (maskRace != null && !string.IsNullOrEmpty(slotChoicesMessage))
            {
                EditorGUILayout.HelpBox(slotChoicesMessage, MessageType.Warning);
            }
        }

        private void HandleProjectChanged()
        {
            RefreshRaceChoices();
            Repaint();
        }

        private void RefreshRaceChoices()
        {
            maskRaceChoices.Clear();
            raceChoicesMessage = null;

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            RaceData[] allRaces = indexer != null ? indexer.GetAllRaces() : null;
            if (allRaces == null)
            {
                raceChoicesMessage = "The UMA asset index is not available. Refresh the UMA Global Library and try again.";
                maskRaceOptionNames = new[] { "No Races Available" };
                maskSlot = null;
                RefreshSlotChoices();
                return;
            }

            for (int i = 0; i < allRaces.Length; i++)
            {
                RaceData race = allRaces[i];
                if (race != null && IndexOf(maskRaceChoices, race) < 0)
                {
                    maskRaceChoices.Add(race);
                }
            }

            maskRaceChoices.Sort((left, right) =>
                string.Compare(left.raceName, right.raceName, StringComparison.OrdinalIgnoreCase));
            maskRaceOptionNames = new string[maskRaceChoices.Count + 1];
            maskRaceOptionNames[0] = "Select Race...";
            for (int i = 0; i < maskRaceChoices.Count; i++)
            {
                maskRaceOptionNames[i + 1] = maskRaceChoices[i].raceName;
            }

            if (maskRaceChoices.Count == 0)
            {
                raceChoicesMessage = "No RaceData assets were found in the UMA asset index.";
                maskRaceOptionNames[0] = "No Races Available";
            }
            else if (maskRace != null && IndexOf(maskRaceChoices, maskRace) < 0)
            {
                maskRace = null;
            }

            RefreshSlotChoices();
        }

        private void RefreshSlotChoices()
        {
            maskSlotChoices.Clear();
            slotChoicesRace = maskRace;
            slotChoicesMessage = null;
            maskSlotOptionNames = new[] { "Select Slot..." };

            if (maskRace == null)
            {
                maskSlot = null;
                return;
            }

            if (maskRace.baseRaceRecipe == null)
            {
                maskSlot = null;
                slotChoicesMessage = "The selected race does not define a base race recipe.";
                maskSlotOptionNames[0] = "No Base Recipe";
                return;
            }

            try
            {
                UMAData.UMARecipe recipe = maskRace.baseRaceRecipe.GetCachedRecipe(true);
                if (recipe == null || recipe.slotDataList == null)
                {
                    maskSlot = null;
                    slotChoicesMessage = "The selected race's base recipe could not be loaded.";
                    maskSlotOptionNames[0] = "No Slots Available";
                    return;
                }

                for (int i = 0; i < recipe.slotDataList.Length; i++)
                {
                    SlotData slot = recipe.slotDataList[i];
                    SlotDataAsset slotAsset = slot != null ? slot.asset : null;
                    if (slotAsset != null && IndexOf(maskSlotChoices, slotAsset) < 0)
                    {
                        maskSlotChoices.Add(slotAsset);
                    }
                }
            }
            catch (Exception exception)
            {
                maskSlot = null;
                slotChoicesMessage = "The selected race's base recipe could not be loaded: " + exception.Message;
                maskSlotOptionNames[0] = "No Slots Available";
                return;
            }

            maskSlotOptionNames = new string[maskSlotChoices.Count + 1];
            maskSlotOptionNames[0] = "Select Slot...";
            for (int i = 0; i < maskSlotChoices.Count; i++)
            {
                SlotDataAsset slot = maskSlotChoices[i];
                maskSlotOptionNames[i + 1] = string.IsNullOrEmpty(slot.slotName)
                    ? slot.name
                    : slot.slotName;
            }

            if (maskSlotChoices.Count == 0)
            {
                maskSlot = null;
                slotChoicesMessage = "The selected race's base recipe contains no loaded SlotDataAssets.";
                maskSlotOptionNames[0] = "No Slots Available";
            }
            else if (maskSlot != null && IndexOf(maskSlotChoices, maskSlot) < 0)
            {
                maskSlot = null;
            }
        }

        private static int IndexOf<T>(List<T> values, T value) where T : UnityEngine.Object
        {
            if (value == null)
            {
                return -1;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool RasterizeUvTriangle(
            byte[] coverage,
            int width,
            int height,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2)
        {
            if (!IsFinite(uv0) || !IsFinite(uv1) || !IsFinite(uv2))
            {
                return false;
            }

            Vector2 point0 = new Vector2(uv0.x * width, uv0.y * height);
            Vector2 point1 = new Vector2(uv1.x * width, uv1.y * height);
            Vector2 point2 = new Vector2(uv2.x * width, uv2.y * height);
            float area = EdgeFunction(point0, point1, point2);
            if (Mathf.Abs(area) < 0.000001f)
            {
                return false;
            }

            int minX = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(point0.x, Mathf.Min(point1.x, point2.x)) - 0.5f),
                0,
                width - 1);
            int maxX = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(point0.x, Mathf.Max(point1.x, point2.x)) - 0.5f),
                0,
                width - 1);
            int minY = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(point0.y, Mathf.Min(point1.y, point2.y)) - 0.5f),
                0,
                height - 1);
            int maxY = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(point0.y, Mathf.Max(point1.y, point2.y)) - 0.5f),
                0,
                height - 1);

            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            float tolerance = Mathf.Abs(area) * 0.000001f + 0.00001f;
            bool positiveArea = area > 0f;
            bool wrotePixel = false;
            for (int y = minY; y <= maxY; y++)
            {
                float sampleY = y + 0.5f;
                int rowOffset = y * width;
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 sample = new Vector2(x + 0.5f, sampleY);
                    float edge0 = EdgeFunction(point1, point2, sample);
                    float edge1 = EdgeFunction(point2, point0, sample);
                    float edge2 = EdgeFunction(point0, point1, sample);
                    bool inside = positiveArea
                        ? edge0 >= -tolerance && edge1 >= -tolerance && edge2 >= -tolerance
                        : edge0 <= tolerance && edge1 <= tolerance && edge2 <= tolerance;
                    if (inside)
                    {
                        coverage[rowOffset + x] = byte.MaxValue;
                        wrotePixel = true;
                    }
                }
            }

            return wrotePixel;
        }

        private static void ApplyInsetAndBlur(
            byte[] coverage,
            int width,
            int height,
            int edgeSize,
            int blurRadius)
        {
            const int StraightCost = 16;
            const int DiagonalCost = 23;
            int requiredDistance = Mathf.Max(2, edgeSize + blurRadius + 2) * StraightCost;
            ushort maximumDistance = (ushort)Mathf.Min(ushort.MaxValue - 1, requiredDistance);
            ushort[] distance = new ushort[coverage.Length];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int index = rowOffset + x;
                    if (coverage[index] == 0)
                    {
                        distance[index] = 0;
                    }
                    else if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        // Treat the area beyond the texture boundary as empty.
                        distance[index] = StraightCost;
                    }
                    else
                    {
                        distance[index] = maximumDistance;
                    }
                }
            }

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int index = rowOffset + x;
                    ushort value = distance[index];
                    if (value == 0)
                    {
                        continue;
                    }

                    if (x > 0)
                    {
                        value = MinimumDistance(value, distance[index - 1], StraightCost, maximumDistance);
                    }
                    if (y > 0)
                    {
                        value = MinimumDistance(value, distance[index - width], StraightCost, maximumDistance);
                        if (x > 0)
                        {
                            value = MinimumDistance(value, distance[index - width - 1], DiagonalCost, maximumDistance);
                        }
                        if (x + 1 < width)
                        {
                            value = MinimumDistance(value, distance[index - width + 1], DiagonalCost, maximumDistance);
                        }
                    }
                    distance[index] = value;
                }
            }

            for (int y = height - 1; y >= 0; y--)
            {
                int rowOffset = y * width;
                for (int x = width - 1; x >= 0; x--)
                {
                    int index = rowOffset + x;
                    ushort value = distance[index];
                    if (value == 0)
                    {
                        continue;
                    }

                    if (x + 1 < width)
                    {
                        value = MinimumDistance(value, distance[index + 1], StraightCost, maximumDistance);
                    }
                    if (y + 1 < height)
                    {
                        value = MinimumDistance(value, distance[index + width], StraightCost, maximumDistance);
                        if (x > 0)
                        {
                            value = MinimumDistance(value, distance[index + width - 1], DiagonalCost, maximumDistance);
                        }
                        if (x + 1 < width)
                        {
                            value = MinimumDistance(value, distance[index + width + 1], DiagonalCost, maximumDistance);
                        }
                    }
                    distance[index] = value;
                }
            }

            for (int i = 0; i < coverage.Length; i++)
            {
                if (distance[i] == 0)
                {
                    coverage[i] = 0;
                    continue;
                }

                float distanceInPixels = distance[i] / (float)StraightCost;
                if (blurRadius == 0)
                {
                    coverage[i] = distanceInPixels > edgeSize ? byte.MaxValue : (byte)0;
                    continue;
                }

                float blend = Mathf.Clamp01((distanceInPixels - edgeSize) / blurRadius);
                blend = blend * blend * (3f - 2f * blend);
                coverage[i] = (byte)Mathf.RoundToInt(blend * byte.MaxValue);
            }
        }

        private static ushort MinimumDistance(
            ushort current,
            ushort neighbor,
            int movementCost,
            ushort maximumDistance)
        {
            int candidate = neighbor + movementCost;
            return candidate < current
                ? (ushort)Mathf.Min(candidate, maximumDistance)
                : current;
        }

        private static bool ContainsCoveredPixel(byte[] coverage)
        {
            for (int i = 0; i < coverage.Length; i++)
            {
                if (coverage[i] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y)
                && !float.IsInfinity(value.y);
        }

        private static float EdgeFunction(Vector2 point0, Vector2 point1, Vector2 point)
        {
            return (point.x - point0.x) * (point1.y - point0.y)
                - (point.y - point0.y) * (point1.x - point0.x);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private void Generate()
        {
            if (!CanGenerate())
            {
                return;
            }

            Material material = GetGeneratorMaterial();
            if (material == null)
            {
                statusMessage = "The generator shader '" + GeneratorShaderName + "' could not be found.";
                statusType = MessageType.Error;
                return;
            }

            RenderTexture renderTexture = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                int width = referenceNormal.width;
                int height = referenceNormal.height;
                renderTexture = RenderTexture.GetTemporary(
                    width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                renderTexture.filterMode = FilterMode.Bilinear;
                renderTexture.wrapMode = TextureWrapMode.Clamp;

                ConfigureGeneratorMaterial(material, width, height);
                material.SetFloat("_OutputMode", 0f);
                Graphics.Blit(null, renderTexture, material);
                RenderTexture.active = renderTexture;

                Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = modifiedAlbedo.name + "_GeneratedNormal",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                result.Apply(false, false);

                DestroyImmediateSafe(ref generatedNormal);
                generatedNormal = result;
                outputIsStale = false;
                statusMessage = "Generated a " + width + " x " + height + " normal map.";
                statusType = MessageType.Info;
                Repaint();
            }
            catch (Exception exception)
            {
                statusMessage = "Generation failed: " + exception.Message;
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                RenderTexture.active = previous;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }
        }

        private void ConfigureGeneratorMaterial(Material material, int width, int height)
        {
            material.SetTexture("_ReferenceAlbedo", referenceAlbedo);
            material.SetTexture("_ReferenceNormal", referenceNormal);
            material.SetTexture("_ModifiedAlbedo", modifiedAlbedo);
            material.SetTexture("_EffectMask", effectMask != null ? effectMask : Texture2D.whiteTexture);
            material.SetVector("_OutputTexelSize", new Vector4(1f / width, 1f / height, width, height));
            material.SetFloat("_NormalDecodeMode", (float)ResolveNormalDecodeMode());
            material.SetFloat("_HeightSource", (float)heightSource);
            material.SetFloat("_MaskChannel", (float)maskChannel);
            material.SetFloat("_HasMask", effectMask != null ? 1f : 0f);
            material.SetFloat("_InvertMask", invertMask ? 1f : 0f);
            material.SetFloat("_InvertHeight", invertHeightDirection ? 1f : 0f);
            material.SetFloat("_Bumpiness", bumpiness);
            material.SetFloat("_DifferenceGain", differenceGain);
            material.SetFloat("_DifferenceThreshold", differenceThreshold);
            material.SetFloat("_SmoothingRadius", smoothingRadius);
        }

        private void ResetGenerationSettings()
        {
            heightSource = HeightSource.Luminance;
            maskChannel = effectMask != null && effectMask == generatedSlotMask
                ? MaskChannel.Red
                : MaskChannel.Alpha;
            normalDecodeMode = NormalMapDecodeMode.Auto;
            bumpiness = 4f;
            differenceGain = 1f;
            differenceThreshold = 0.005f;
            smoothingRadius = 1f;
            invertHeightDirection = false;
            invertMask = false;
            outputIsStale = generatedNormal != null;
            statusMessage = null;
            Repaint();
        }

        private NormalMapDecodeMode ResolveNormalDecodeMode()
        {
            if (normalDecodeMode != NormalMapDecodeMode.Auto)
            {
                return normalDecodeMode;
            }

            string path = AssetDatabase.GetAssetPath(referenceNormal);
            TextureImporter importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType == TextureImporterType.NormalMap)
            {
                return NormalMapDecodeMode.UnityNormal;
            }

            return DetectRawNormalEncoding(referenceNormal);
        }

        private static NormalMapDecodeMode DetectRawNormalEncoding(Texture2D texture)
        {
            const int sampleSize = 64;
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                temporary = RenderTexture.GetTemporary(
                    sampleSize, sampleSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(sampleSize, sampleSize, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0f, 0f, sampleSize, sampleSize), 0, 0, false);
                readable.Apply(false, false);

                Color32[] pixels = readable.GetPixels32();
                int usefulAlpha = 0;
                int redVariance = 0;
                byte previousRed = pixels.Length > 0 ? pixels[0].r : (byte)128;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    if (pixel.a > 8 && pixel.a < 247)
                    {
                        usefulAlpha++;
                    }
                    redVariance += Mathf.Abs(pixel.r - previousRed);
                    previousRed = pixel.r;
                }

                return usefulAlpha > pixels.Length / 8 && redVariance < pixels.Length * 18
                    ? NormalMapDecodeMode.Dxt5nm
                    : NormalMapDecodeMode.RawRgb;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
                if (readable != null)
                {
                    DestroyImmediate(readable);
                }
            }
        }

        private void SaveGeneratedNormal()
        {
            if (generatedNormal == null)
            {
                return;
            }

            string initialDirectory = GetSaveDirectory();
            string fileName = modifiedAlbedo != null ? modifiedAlbedo.name + "_Normal.png" : "GeneratedNormal.png";
            string path = EditorUtility.SaveFilePanel("Save Generated Normal Map", initialDirectory, fileName, "png");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                File.WriteAllBytes(path, generatedNormal.EncodeToPNG());
                lastSavedPath = path;
                ImportNormalIfInProject(path);
                statusMessage = "Saved the normal map to " + path;
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = "Save failed: " + exception.Message;
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        private void SaveMask()
        {
            if (effectMask == null)
            {
                return;
            }

            string suggestedName = modifiedAlbedo != null
                ? modifiedAlbedo.name + "_Mask"
                : effectMask.name + "_Mask";
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save Mask",
                suggestedName,
                "png",
                "Save the active mask as a persistent grayscale PNG.",
                GetMaskSaveFolder());
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            Material material = GetGeneratorMaterial();
            if (material == null)
            {
                SetStatus(
                    "The generator shader '" + GeneratorShaderName + "' could not be found.",
                    MessageType.Error);
                return;
            }

            RenderTexture renderTexture = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readableMask = null;
            try
            {
                int width = effectMask.width;
                int height = effectMask.height;
                renderTexture = RenderTexture.GetTemporary(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear);
                renderTexture.filterMode = FilterMode.Bilinear;
                renderTexture.wrapMode = TextureWrapMode.Clamp;

                material.SetTexture("_EffectMask", effectMask);
                material.SetFloat("_MaskChannel", (float)maskChannel);
                material.SetFloat("_HasMask", 1f);
                material.SetFloat("_InvertMask", invertMask ? 1f : 0f);
                material.SetFloat("_OutputMode", 2f);
                Graphics.Blit(null, renderTexture, material);

                RenderTexture.active = renderTexture;
                readableMask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readableMask.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readableMask.Apply(false, false);

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absolutePath = Path.Combine(
                    projectRoot,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolutePath, readableMask.EncodeToPNG());

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }

                Texture2D persistentMask = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (persistentMask == null)
                {
                    throw new InvalidOperationException(
                        "Unity imported the PNG, but it could not be loaded as a Texture2D.");
                }

                effectMask = persistentMask;
                maskChannel = MaskChannel.Red;
                invertMask = false;
                DestroyImmediateSafe(ref generatedSlotMask);
                lastSavedPath = absolutePath;
                SaveSettings();
                SetStatus("Saved and assigned the persistent mask " + assetPath + ".", MessageType.Info);
                EditorGUIUtility.PingObject(persistentMask);
            }
            catch (Exception exception)
            {
                SetStatus("Save mask failed: " + exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                RenderTexture.active = previous;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
                if (readableMask != null)
                {
                    DestroyImmediate(readableMask);
                }
            }
        }

        private string GetMaskSaveFolder()
        {
            string assetPath = modifiedAlbedo != null
                ? AssetDatabase.GetAssetPath(modifiedAlbedo)
                : AssetDatabase.GetAssetPath(effectMask);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string folder = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                {
                    return folder;
                }
            }

            return "Assets";
        }

        private void SaveAndBuild()
        {
            if (generatedNormal == null)
            {
                return;
            }

            if (!TryGetSuggestedNormalPaths(out string assetPath, out string absolutePath))
            {
                statusMessage = "Save and Build requires the modified albedo to be an asset inside this project's Assets folder.";
                statusType = MessageType.Error;
                return;
            }

            try
            {
                File.WriteAllBytes(absolutePath, generatedNormal.EncodeToPNG());
                lastSavedPath = absolutePath;
                ImportNormalIfInProject(absolutePath);

                string modifiedAlbedoPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(modifiedAlbedo));
                int releasedOverlayCount = ReleaseIndexedOverlaysUsingTextures(
                    modifiedAlbedoPath,
                    assetPath,
                    out bool assetIndexerAvailable,
                    out HashSet<string> affectedOverlayNames);
                int rebuiltCount = RebuildAvatarsUsingTextures(
                    modifiedAlbedoPath,
                    assetPath,
                    affectedOverlayNames);
                if (rebuiltCount > 0)
                {
                    statusMessage = "Saved " + assetPath + ", released "
                        + releasedOverlayCount + " cached overlay reference"
                        + (releasedOverlayCount == 1 ? ", and queued a full rebuild for " : "s, and queued a full rebuild for ")
                        + rebuiltCount + (rebuiltCount == 1 ? " avatar." : " avatars.");
                    statusType = assetIndexerAvailable ? MessageType.Info : MessageType.Warning;
                }
                else
                {
                    statusMessage = "Saved " + assetPath
                        + " and released " + releasedOverlayCount + " cached overlay reference"
                        + (releasedOverlayCount == 1 ? ", but no loaded UMA avatars referencing the texture were found."
                            : "s, but no loaded UMA avatars referencing the texture were found.");
                    statusType = MessageType.Warning;
                }
                if (!assetIndexerAvailable)
                {
                    statusMessage += " The UMA global library was unavailable, so its overlay references could not be cleared.";
                }
            }
            catch (Exception exception)
            {
                statusMessage = "Save and Build failed: " + exception.Message;
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        private bool TryGetSuggestedNormalPaths(out string assetPath, out string absolutePath)
        {
            assetPath = null;
            absolutePath = null;
            if (modifiedAlbedo == null)
            {
                return false;
            }

            string modifiedAssetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(modifiedAlbedo));
            if (string.IsNullOrEmpty(modifiedAssetPath)
                || (!modifiedAssetPath.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    && !modifiedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string folder = NormalizeAssetPath(Path.GetDirectoryName(modifiedAssetPath));
            if (string.IsNullOrEmpty(folder))
            {
                folder = "Assets";
            }

            assetPath = folder + "/" + modifiedAlbedo.name + "_Normal.png";
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }

        private static int ReleaseIndexedOverlaysUsingTextures(
            string modifiedAlbedoPath,
            string savedNormalPath,
            out bool assetIndexerAvailable,
            out HashSet<string> affectedOverlayNames)
        {
            affectedOverlayNames = new HashSet<string>(StringComparer.Ordinal);
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            assetIndexerAvailable = indexer != null;
            if (indexer == null)
            {
                return 0;
            }

            int releasedCount = 0;
            List<OverlayDataAsset> overlays = indexer.GetAllAssets<OverlayDataAsset>();
            if (overlays == null)
            {
                return 0;
            }

            HashSet<OverlayDataAsset> releasedOverlays = new HashSet<OverlayDataAsset>();
            for (int i = 0; i < overlays.Count; i++)
            {
                OverlayDataAsset overlay = overlays[i];
                if (overlay == null
                    || releasedOverlays.Contains(overlay)
                    || !OverlayAssetUsesTexturePath(overlay, modifiedAlbedoPath, savedNormalPath))
                {
                    continue;
                }

                // This is the same cache invalidation used by
                // UMAUpdateProcessor.UpdateOverlay and the OverlayDataAsset editor.
                indexer.ReleaseReference(overlay);
                affectedOverlayNames.Add(overlay.overlayName);
                releasedOverlays.Add(overlay);
                releasedCount++;
            }

            return releasedCount;
        }

        private static int RebuildAvatarsUsingTextures(
            string modifiedAlbedoPath,
            string savedNormalPath,
            HashSet<string> affectedOverlayNames)
        {
            int rebuiltCount = 0;
            UMAData[] avatars = Resources.FindObjectsOfTypeAll<UMAData>();
            for (int i = 0; i < avatars.Length; i++)
            {
                UMAData avatar = avatars[i];
                if (!IsLoadedSceneAvatar(avatar)
                    || (!AvatarUsesAffectedOverlay(avatar, affectedOverlayNames)
                        && !AvatarUsesTexturePath(avatar, modifiedAlbedoPath, savedNormalPath)))
                {
                    continue;
                }

                DynamicCharacterAvatar dynamicAvatar =
                    avatar.gameObject.GetComponent<DynamicCharacterAvatar>();
                if (dynamicAvatar != null)
                {
                    // GenerateSingleUMA performs a complete editor build and reloads
                    // wardrobe/overlay data from the now-invalidated global library.
                    dynamicAvatar.GenerateSingleUMA(false, true);
                }
                else
                {
                    avatar.needsMaterialClear = true;
                    avatar.Dirty(false, true, false);
                }
                rebuiltCount++;
            }

            return rebuiltCount;
        }

        private static bool AvatarUsesAffectedOverlay(
            UMAData avatar,
            HashSet<string> affectedOverlayNames)
        {
            if (avatar == null
                || affectedOverlayNames == null
                || affectedOverlayNames.Count == 0
                || avatar.umaRecipe == null
                || avatar.umaRecipe.slotDataList == null)
            {
                return false;
            }

            for (int slotIndex = 0; slotIndex < avatar.umaRecipe.slotDataList.Length; slotIndex++)
            {
                SlotData slot = avatar.umaRecipe.slotDataList[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                List<OverlayData> overlays = slot.GetOverlayList();
                for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                {
                    OverlayData overlay = overlays[overlayIndex];
                    if (overlay != null
                        && overlay.asset != null
                        && affectedOverlayNames.Contains(overlay.asset.overlayName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsLoadedSceneAvatar(UMAData avatar)
        {
            if (avatar == null || EditorUtility.IsPersistent(avatar) || avatar.gameObject == null)
            {
                return false;
            }

            if ((avatar.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                return false;
            }

            return avatar.gameObject.scene.IsValid() && avatar.gameObject.scene.isLoaded;
        }

        private static bool AvatarUsesTexturePath(UMAData avatar, string firstPath, string secondPath)
        {
            UMAData.UMARecipe recipe = avatar.umaRecipe;
            if (recipe != null && recipe.slotDataList != null)
            {
                for (int slotIndex = 0; slotIndex < recipe.slotDataList.Length; slotIndex++)
                {
                    SlotData slot = recipe.slotDataList[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    var overlays = slot.GetOverlayList();
                    for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                    {
                        OverlayData overlay = overlays[overlayIndex];
                        if (overlay != null && OverlayAssetUsesTexturePath(overlay.asset, firstPath, secondPath))
                        {
                            return true;
                        }
                    }
                }
            }

            if (avatar.generatedMaterials == null || avatar.generatedMaterials.materials == null)
            {
                return false;
            }

            for (int materialIndex = 0; materialIndex < avatar.generatedMaterials.materials.Count; materialIndex++)
            {
                UMAData.GeneratedMaterial material = avatar.generatedMaterials.materials[materialIndex];
                if (material == null || material.materialFragments == null)
                {
                    continue;
                }

                for (int fragmentIndex = 0; fragmentIndex < material.materialFragments.Count; fragmentIndex++)
                {
                    UMAData.MaterialFragment fragment = material.materialFragments[fragmentIndex];
                    if (fragment == null)
                    {
                        continue;
                    }

                    if (TextureDataUsesTexturePath(fragment.baseOverlay, firstPath, secondPath))
                    {
                        return true;
                    }

                    if (fragment.AdditionalOverlays != null)
                    {
                        for (int overlayIndex = 0; overlayIndex < fragment.AdditionalOverlays.Length; overlayIndex++)
                        {
                            if (TextureDataUsesTexturePath(fragment.AdditionalOverlays[overlayIndex], firstPath, secondPath))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool OverlayAssetUsesTexturePath(OverlayDataAsset asset, string firstPath, string secondPath)
        {
            return asset != null
                && (TextureUsesPath(asset.alphaMask, firstPath, secondPath)
                    || TextureArrayUsesPath(asset.textureList, firstPath, secondPath));
        }

        private static bool TextureDataUsesTexturePath(UMAData.textureData data, string firstPath, string secondPath)
        {
            return data != null
                && (TextureUsesPath(data.alphaTexture, firstPath, secondPath)
                    || TextureArrayUsesPath(data.textureList, firstPath, secondPath));
        }

        private static bool TextureArrayUsesPath(Texture[] textures, string firstPath, string secondPath)
        {
            if (textures == null)
            {
                return false;
            }

            for (int i = 0; i < textures.Length; i++)
            {
                if (TextureUsesPath(textures[i], firstPath, secondPath))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TextureUsesPath(Texture texture, string firstPath, string secondPath)
        {
            if (texture == null)
            {
                return false;
            }

            string texturePath = NormalizeAssetPath(AssetDatabase.GetAssetPath(texture));
            return (!string.IsNullOrEmpty(firstPath)
                    && texturePath.Equals(firstPath, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(secondPath)
                    && texturePath.Equals(secondPath, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private string GetSaveDirectory()
        {
            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                string lastDirectory = Path.GetDirectoryName(lastSavedPath);
                if (!string.IsNullOrEmpty(lastDirectory) && Directory.Exists(lastDirectory))
                {
                    return lastDirectory;
                }
            }

            if (modifiedAlbedo != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(modifiedAlbedo);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                    string absolutePath = Path.Combine(projectRoot, assetPath);
                    string directory = Path.GetDirectoryName(absolutePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
            }

            return Application.dataPath;
        }

        private static void ImportNormalIfInProject(string absolutePath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string normalizedPath = absolutePath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string assetPath = "Assets" + normalizedPath.Substring(dataPath.Length);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }

            UnityEngine.Object savedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (savedAsset != null)
            {
                EditorGUIUtility.PingObject(savedAsset);
            }
        }

        private Material GetGeneratorMaterial()
        {
            if (generatorMaterial == null)
            {
                Shader shader = Shader.Find(GeneratorShaderName);
                if (shader != null)
                {
                    generatorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return generatorMaterial;
        }

        private Material GetPreviewMaterial()
        {
            if (previewMaterial == null)
            {
                Shader shader = Shader.Find(PreviewShaderName);
                if (shader != null)
                {
                    previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return previewMaterial;
        }

        private void SaveSettings()
        {
            PersistedSettings settings = new PersistedSettings
            {
                referenceAlbedoReference = SerializeObjectReference(referenceAlbedo),
                referenceNormalReference = SerializeObjectReference(referenceNormal),
                modifiedAlbedoReference = SerializeObjectReference(modifiedAlbedo),
                effectMaskReference = SerializeObjectReference(effectMask),
                maskRaceReference = SerializeObjectReference(maskRace),
                maskSlotReference = SerializeObjectReference(maskSlot),
                slotMaskEdgeSize = slotMaskEdgeSize,
                slotMaskBlurRadius = slotMaskBlurRadius,
                normalDecodeMode = (int)normalDecodeMode,
                heightSource = (int)heightSource,
                maskChannel = (int)maskChannel,
                bumpiness = bumpiness,
                differenceGain = differenceGain,
                differenceThreshold = differenceThreshold,
                smoothingRadius = smoothingRadius,
                invertHeightDirection = invertHeightDirection,
                invertMask = invertMask,
                previewLightContrast = previewLightContrast,
                previewLightDirection = previewLightDirection,
                previewMode = (int)previewMode,
                liveGeneration = liveGeneration,
                lastSavedPath = lastSavedPath,
            };

            EditorPrefs.SetString(SettingsKey, JsonUtility.ToJson(settings));
        }

        private void LoadSettings()
        {
            string json = EditorPrefs.GetString(SettingsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                PersistedSettings settings = JsonUtility.FromJson<PersistedSettings>(json);
                if (settings == null || settings.version != 1)
                {
                    return;
                }

                referenceAlbedo = DeserializeObjectReference<Texture2D>(settings.referenceAlbedoReference);
                referenceNormal = DeserializeObjectReference<Texture2D>(settings.referenceNormalReference);
                modifiedAlbedo = DeserializeObjectReference<Texture2D>(settings.modifiedAlbedoReference);
                effectMask = DeserializeObjectReference<Texture2D>(settings.effectMaskReference);
                maskRace = DeserializeObjectReference<RaceData>(settings.maskRaceReference);
                maskSlot = DeserializeObjectReference<SlotDataAsset>(settings.maskSlotReference);
                slotMaskEdgeSize = Mathf.Clamp(settings.slotMaskEdgeSize, 0, 256);
                slotMaskBlurRadius = Mathf.Clamp(settings.slotMaskBlurRadius, 0, 128);
                normalDecodeMode = RestoreEnum(settings.normalDecodeMode, normalDecodeMode);
                heightSource = RestoreEnum(settings.heightSource, heightSource);
                maskChannel = RestoreEnum(settings.maskChannel, maskChannel);
                bumpiness = Mathf.Clamp(settings.bumpiness, 0f, 20f);
                differenceGain = Mathf.Clamp(settings.differenceGain, 0f, 4f);
                differenceThreshold = Mathf.Clamp(settings.differenceThreshold, 0f, 0.1f);
                smoothingRadius = Mathf.Clamp(settings.smoothingRadius, 0f, 8f);
                invertHeightDirection = settings.invertHeightDirection;
                invertMask = settings.invertMask;
                previewLightContrast = Mathf.Clamp(settings.previewLightContrast, 0f, 3f);
                previewLightDirection = settings.previewLightDirection;
                previewMode = RestoreEnum(settings.previewMode, previewMode);
                liveGeneration = settings.liveGeneration;
                lastSavedPath = settings.lastSavedPath;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Unable to restore the UMA Albedo to Normal window settings: "
                    + exception.Message);
            }
        }

        private static string SerializeObjectReference(UnityEngine.Object value)
        {
            if (value == null || !EditorUtility.IsPersistent(value))
            {
                return string.Empty;
            }

            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(value);
            return globalId.identifierType != 0 ? globalId.ToString() : string.Empty;
        }

        private static T DeserializeObjectReference<T>(string serializedReference)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(serializedReference)
                || !GlobalObjectId.TryParse(serializedReference, out GlobalObjectId globalId))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as T;
        }

        private static T RestoreEnum<T>(int value, T fallback) where T : struct
        {
            return Enum.IsDefined(typeof(T), value)
                ? (T)Enum.ToObject(typeof(T), value)
                : fallback;
        }

        private static bool HasAspectMismatch(Texture texture, float targetAspect)
        {
            if (texture == null || texture.height <= 0)
            {
                return false;
            }
            return Mathf.Abs(((float)texture.width / texture.height) - targetAspect) > 0.001f;
        }

        private static Rect FitRect(Rect available, float textureWidth, float textureHeight)
        {
            if (textureWidth <= 0f || textureHeight <= 0f)
            {
                return available;
            }

            float textureAspect = textureWidth / textureHeight;
            float availableAspect = available.width / Mathf.Max(1f, available.height);
            if (textureAspect > availableAspect)
            {
                float height = available.width / textureAspect;
                return new Rect(available.x, available.center.y - height * 0.5f, available.width, height);
            }

            float width = available.height * textureAspect;
            return new Rect(available.center.x - width * 0.5f, available.y, width, available.height);
        }

        private static GUIStyle CenteredLabelStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 13,
            };
            return style;
        }

        private static void DestroyImmediateSafe<T>(ref T value) where T : UnityEngine.Object
        {
            if (value != null)
            {
                DestroyImmediate(value);
                value = null;
            }
        }
    }
}
