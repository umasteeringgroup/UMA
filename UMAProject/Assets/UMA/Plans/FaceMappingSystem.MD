# UMA Face Mapping System Plan

## 1. Product goal

Create a Unity 6.3+ UMA face-mapping system for both authoring and shipped applications. It accepts a single face photograph from a webcam, the file system, or a Unity `Texture`, detects and aligns the face, removes non-facial occlusions, maps the visible facial appearance into a standard UMA face texture layout, and produces a softly blended UMA overlay plus a recommended body skin color.

The system should produce a recognizable, natural-looking face texture quickly while remaining editable and honest about areas that cannot be recovered from one photograph.

Primary outputs:

- A mapped face albedo texture in the selected UMA face/UDIM layout.
- A feathered alpha mask so the mapped face blends over the existing skin texture.
- A runtime-created `OverlayDataAsset` that can be used immediately by an UMA recipe in play mode, with optional editor persistence and indexing.
- An optional wardrobe/face recipe containing the overlay.
- A suggested UMA shared skin color for bringing the body closer to the face.
- Confidence and validation reports for landmarks, pose, occlusions, skin color, and final coverage.

Runtime embedding is a release-critical requirement, not a future enhancement. A host application must be able to pass a `Texture` to a documented public API, await the deterministic mapping pipeline, receive a fully configured in-memory `OverlayDataAsset`, and apply it to an avatar without opening an editor stage, using `AssetDatabase`, or writing an asset into the Unity project.

The required runtime path must use **no AI locally and no AI service/provider**. It must ship without neural-network inference, trained model weights, model downloads, generative image processing, or cloud calls. Face localization, feature suggestions, masking, pose, warping, repair, and color matching must use deterministic image processing, hand-authored geometric templates, and explicit user guidance/correction. GPU shaders, compute shaders, Burst, Jobs, and mathematical computer-vision algorithms are allowed because they execute fixed authored algorithms rather than learned models. A CPU path must remain available when optional GPU acceleration is unsupported.

Initial non-goals:

- Identity lookup, identity matching, or generation of identity embeddings.
- Reconstructing a complete head from one image.
- Automatically changing UMA facial DNA in the first release.
- Removing beard, mustache, or stubble.
- Synthesizing authoritative normal, roughness, or subsurface maps from photographic lighting.
- Cloud processing as a required part of the workflow.

Texture mapping alone cannot reproduce facial shape when the avatar geometry differs substantially from the photographed face. Optional DNA suggestions may be explored later as a separate, explicitly reviewed feature.

## 2. Recommended user workflow

1. Open `FaceMappingStage` from a generated `DynamicCharacterAvatar`, a supported race, or an existing face-mapping document.
2. Choose **Webcam** or **Import Photo**.
3. Capture or load an image and normalize its orientation and color space.
4. Use the capture oval/crop and deterministic image analysis to locate the likely face region, then ask the user to confirm it when confidence is not high.
5. Suggest eyes, brows, nose, lips, jaw, cheek, temple, forehead, and face-side landmarks from geometry, gradients, symmetry, and edge cues.
6. Let the user confirm or correct the required landmark handles before mapping.
7. Build editable skin, feature, scalp-hair, glasses, facial-hair, accessory, background, and uncertainty masks using deterministic region growth, color/edge cues, protected facial zones, and user strokes.
8. Let the user paint **Keep**, **Remove**, **Skin**, and **Unknown** corrections.
9. Normalize broad lighting and white balance while preserving high-frequency features such as freckles, pores, beard, mustache, and stubble.
10. Estimate face pose, frontalize the usable pixels, and warp them into the selected UMA face template.
11. Reconstruct small glasses/hair gaps from trustworthy nearby skin; fall back to base UMA skin and reduced alpha for uncertain or large hidden areas.
12. Preview the source, landmarks, semantic masks, canonical face, UMA UV output, and final avatar.
13. Review the suggested body skin color and adjust its influence.
14. Validate and export the texture, alpha mask, overlay asset, optional recipe, and color result.

The best-case path should require only confirmation of the face guide/primary landmarks and final preview. Difficult photographs may require mask correction. Every automatic suggestion must remain inspectable and correctable, and low-confidence input must return **Needs Confirmation**, **Needs Corrections**, or **Recapture** instead of pretending that uncertain pixels are reliable.

### Embedded play-mode workflow

1. The host application obtains a `Texture2D`, `RenderTexture`, or stable frame copied from a `WebCamTexture`.
2. It passes that `Texture` plus a runtime profile/options object to `IFaceMappingService`.
3. The service runs deterministic quality checks, feature suggestions, mask building, repair, warping, alpha generation, and skin-color estimation asynchronously.
4. It either completes or returns a resumable correction request containing editable face bounds, primary landmarks, and masks; the host can display the supplied runtime correction controls or its own UI.
5. The completed result returns generated textures, diagnostics, body-color guidance, and a fully configured transient `OverlayDataAsset`.
6. The application applies the returned overlay directly to an UMA recipe/avatar and rebuilds the avatar.
7. The application retains the result while the avatar uses it, or explicitly releases it through the documented lifetime API.

The processing engine must be UI-framework-independent, but the product must include embeddable runtime confirmation/correction UI because deterministic analysis cannot safely resolve every photograph. Applications may use the supplied UI or build their own capture, consent, review, correction, progress, and failure UI around the same session API. In **Deterministic Only**, applications that choose a no-correction flow receive a conservative partial overlay or a typed recapture result and never silently switch to AI.

## 3. Fit with the current UMA project

The implementation should be a separate module rather than another mode inside `FaceEditorStage` or `TexturePaintStageWindow`. The complete deterministic pipeline belongs in runtime-safe assemblies; the editor stage is one client of that pipeline.

Existing systems provide useful patterns:

- `FaceEditorStage` and `VertexEditorStage` provide isolated `PreviewSceneStage` lifecycle and avatar setup.
- Overlay Painter provides document persistence, recovery, GPU compositing, layer masks, dilation, UMA material-channel handling, and recipe-ready overlay export.
- `DynamicCharacterAvatar.SetColor` already provides the integration point for previewing and applying a shared skin color.
- UMA 3 content contains base skin textures, skin masks, alternate face overlays, and multi-tile material layouts that can be used to build and validate target templates.

Recommended source layout:

```text
Assets/UMA/FaceMapper/
    Core/
        Detection/
        Landmarks/
        Pose/
        ImageProcessing/
        Masks/
        Warping/
        Color/
        Validation/
    Runtime/
    Editor/
        Stage/
        Workspace/
        Capture/
        Export/
    Shaders/
    Tests/
    QA/
    Samples/
```

Recommended assemblies:

- `UMA.FaceMapper.Core`
- `UMA.FaceMapper.Runtime`
- `UMA.FaceMapper.Editor`
- `UMA.FaceMapper.Tests`
- `UMA.FaceMapper.Editor.Tests`

The face mapper should reuse neutral Overlay Painter export/compositing services where practical, but it should not depend on the painter's stage UI or mutable document internals. `UMA.FaceMapper.Core` and `UMA.FaceMapper.Runtime` must not reference `UnityEditor`, `AssetDatabase`, editor windows, or editor-only assembly definitions. The editor stage must call the same public runtime API used by shipped applications so the two paths cannot drift.

## 4. System architecture

```text
FaceMappingStage (Editor UI) -----------+
                                        |
Host Application (Play Mode/Player) ---+-> FaceMappingRuntimeService
                                             -> FaceMappingController
                                                 -> PhotoInputService
                                                 -> CaptureQualityAnalyzer
                                                 -> DeterministicFaceRegionFinder
                                                 -> GeometricLandmarkEstimator
                                                 -> FacePoseEstimator
                                                 -> ClassicalMaskBuilder
                                                 -> RuleBasedOcclusionPolicy
                                                 -> LightingNormalizer
                                                 -> FaceFrontalizer
                                                 -> FaceTextureWarper
                                                 -> OcclusionRepairService
                                                 -> AlphaMaskBuilder
                                                 -> BodySkinColorEstimator
                                                 -> FaceMappingValidator
                                                 -> RuntimeOverlayDataAssetFactory

Optional IFaceMappingEnhancer (separate package)
    -> Supplies suggestions to FaceMappingRuntimeService only when explicitly enabled

FaceMappingStage (Editor only)
    -> FaceMappingAssetExporter
        -> Persist textures/assets
        -> Update UMA index
```

The runtime service owns workflow state, resumable correction sessions, progress, cancellation, and resource lifetime. Face-region analysis, landmark suggestion, mask building, warping, color analysis, runtime overlay construction, and editor persistence should remain separate, deterministic services with testable inputs and outputs.

Interfaces should keep deterministic algorithms replaceable and testable without permitting a hidden AI dependency:

- `IFaceRegionFinder`
- `IGeometricLandmarkEstimator`
- `IFaceMaskBuilder`
- `IOcclusionRepairService`
- `IFaceMappingTemplateCatalog`
- `IRuntimeOverlayDataAssetFactory`

The required face-mapper assemblies and default package must not reference an AI inference package, ship ONNX or other learned-weight files, contact an inference endpoint, or dynamically download an analysis component. Build validation must fail if one enters the deterministic runtime package.

### Optional AI enhancement layer

AI may be offered as a separate optional integration, but never as a dependency of the core product. Define a narrow `IFaceMappingEnhancer` contract in the runtime assembly; optional local or provider-specific packages may implement it to suggest face bounds, landmarks, masks, confidence, or repair candidates. The deterministic pipeline must validate and consume those suggestions through the same correction/session data used for human edits.

Required modes:

- **Deterministic Only (default):** never loads an AI package, model, or provider and never makes a network request.
- **Use Optional Enhancer:** uses a specifically configured enhancer when installed, available, and explicitly enabled; otherwise falls back to the complete deterministic workflow.

There must be no automatic provider discovery, silent model download, or silent network fallback. Optional enhancer packages live in separate assemblies/packages, declare their size, platform support, licenses, privacy behavior, and costs, and can be removed without breaking compilation or the deterministic sample application. AI-originated landmarks or masks remain editable suggestions and must obey the same facial-hair protection, confidence, and validation rules.

## 5. Data model

### Runtime options and result

The runtime API must take the source `Texture` explicitly and use public, versioned options/result types rather than editor documents or loosely related parameters.

`FaceMappingOptions` should contain:

- `FaceMappingRuntimeProfile` containing deterministic algorithm settings, shaders, UMA material, and template references that are valid in a player build.
- Processing mode, defaulting to **Deterministic Only**, plus an explicit optional enhancer identifier when the host has enabled one.
- Target race/template identifier and output resolution.
- Quality thresholds and pose limits.
- Lighting, symmetry, repair, alpha, and body-color options.
- A stable caller-supplied operation/correlation GUID when the host needs one.
- Optional progress and diagnostic policy; never the host application's UI objects.

`FaceMappingRuntimeResult` should contain:

- Completion status and structured error/recapture reason.
- A resumable `FaceMappingSession` and `FaceMappingCorrectionRequest` when bounds, landmarks, skin samples, or masks need confirmation.
- Generated face albedo and alpha textures.
- A fully initialized transient `OverlayDataAsset` referencing those textures and the selected `UMAMaterial`.
- An optional ready-to-attach `OverlayData` wrapper.
- `BodySkinColorResult`.
- Quality, confidence, pose, coverage, and version information.
- Processing provenance stating whether the result was deterministic-only, human-corrected, or assisted by a named optional enhancer.
- Explicit ownership/lifetime information and a release or `Dispose` operation for generated textures and transient objects.

No public runtime request or result may expose an editor-only type.

### `FaceMappingDocument`

Store:

- Schema and migration version.
- Stable document UUID.
- Selected UMA race, face slot, material, surface, and target template.
- Input orientation and crop.
- Face bounding box and face-selection record.
- Landmark points, confidence, and manual overrides.
- Pose estimate and visibility map.
- Semantic masks and user corrections.
- Lighting/color normalization settings.
- Warp controls and manual pins.
- Alpha-mask settings.
- Body-color result and influence.
- Export records and validation report.
- Exact template and deterministic algorithm versions, plus optional enhancer identity/version only when one was actually used.

Internal document IDs must use serialized GUIDs that remain stable across editor sessions and asset reloads.

### Source-photo retention

The document must make retention explicit:

- **Session only (default):** hold the photograph in memory and temporary GPU resources; save only generated outputs. Re-editing from source requires importing it again.
- **Keep editable source:** copy the image into a clearly identified project-owned private source folder after explicit confirmation.
- **External reference:** remember an external path only when the user requests it, and report a missing source without silently searching elsewhere.

Recovery files must follow the same retention choice. Raw photographs must never be included in diagnostics, telemetry, test captures, or bug reports by default.

## 6. Input and capture

### Runtime texture input

The primary embeddable API accepts Unity's base `Texture` type. It must support at least `Texture2D`, `RenderTexture`, and a captured/copy-stable frame from `WebCamTexture`. A live webcam texture can change while processing, so the service must snapshot it at request start unless the caller explicitly supplies an already frozen frame.

Runtime texture ingestion must:

- Work when the source texture is not CPU-readable by using a render-texture copy and asynchronous readback where required.
- Preserve the source texture unchanged and never assume ownership of it.
- Validate dimensions, format, color space, orientation options, and whether the Unity object is still valid.
- Avoid synchronous full-resolution readbacks on the main thread.
- Document whether a texture is interpreted as sRGB or linear and provide explicit override options.
- Bound input and working resolutions to prevent excessive memory use.
- Return a structured unsupported-input or resource-limit result rather than failing silently.

File import and webcam capture are adapters that produce a texture for this same API; they must not be required by the mapping pipeline itself.

### Webcam

Use `WebCamTexture` behind a dedicated capture service. Unity documents webcam textures as live video textures and requires appropriate camera authorization on applicable platforms. [Unity WebCamTexture API](https://docs.unity.cn/ScriptReference/WebCamTexture.html)

Capture UI should include:

- Device selector.
- Resolution and orientation display.
- Mirrored preview for user comfort, while storing the correctly oriented unmirrored frame.
- Face-position oval and eye line.
- Live yaw, pitch, roll, distance, sharpness, exposure, and shadow guidance.
- Countdown and retake.
- Clear active-camera indicator.
- Immediate stop/release on cancel, stage close, compilation, play-mode transition, or device loss.

Camera permission should be requested only after the user chooses webcam capture. No microphone permission is needed.

### File import

Support PNG, JPEG, and other explicitly tested formats. Import must:

- Read bytes without modifying the original file.
- Apply EXIF orientation.
- Normalize mirrored images.
- Preserve correct sRGB interpretation.
- Reject unsupported, corrupt, or unreasonably large images safely.
- Strip metadata from generated outputs.
- Keep the decoded source immutable during processing.

### Capture-quality gate

Measure:

- Exactly one selected face.
- Face pixel size and inter-eye distance.
- Blur and motion blur.
- Underexposure, overexposure, and clipped channels.
- Strong color cast.
- Uneven left/right illumination.
- Yaw, pitch, and roll.
- Eye openness and mouth expression.
- Landmark confidence.
- Percentage of the face obscured by hair, glasses, hands, or accessories.

Initial supported-pose target should be approximately +/-30 degrees yaw and +/-20 degrees pitch, with arbitrary roll corrected in preprocessing. Final thresholds must be set from the deterministic prototype and validation corpus. Images outside the reliable range should produce a recapture recommendation rather than a low-quality silent result.

An optional second photograph from the opposite side can be added after the initial release for better side-face coverage, but the core workflow must work from one photograph.

## 7. Deterministic face localization, landmarks, and pose

The deterministic baseline begins from a capture oval or user crop. Within that bounded region it can suggest face sides and primary features using fixed image operations: luminance/chroma maps, integral-image statistics, gradients, edge and ridge filters, connected components, bilateral symmetry, geometric ratios, and multi-scale hand-authored template correlation. These are initializers, not trained classifiers.

The minimum confirmation set should be easy to place on a phone or desktop: face top/bottom/sides, both eye centers or corners, nose base/tip, and mouth corners/centerline. From those anchors, constrained splines and fixed facial proportions generate denser brow, eye, nose, lip, jaw, cheek, temple, and forehead contours. Local edge snapping may refine a contour only within a small user-visible radius and must be reversible.

The estimator returns:

- Suggested face bounds and dense normalized landmarks.
- Per-point evidence/confidence based on local contrast, symmetry, geometry, and edge agreement.
- Semantic indices for every generated contour.
- A list of primary points requiring confirmation.
- Sufficient points for pose estimation.

Pose estimation should use a hand-authored canonical 3D facial template and a perspective solve rather than inferring pose only from the 2D bounding box. The result provides:

- Yaw, pitch, and roll.
- Approximate camera-relative transform.
- Left/right visibility confidence.
- Self-occlusion boundary.
- Pose quality score.

Critical landmarks must be editable with constrained handles in both editor and runtime correction UI. Moving the left eye corner, for example, should update its paired contour and local warp region without destabilizing the jaw or mouth. If the fixed algorithms cannot find adequate evidence, they leave the point unconfirmed rather than fabricating a high-confidence result.

## 8. Deterministic masks and occlusion policy

The semantic mask should distinguish:

- Skin.
- Left/right eyes.
- Left/right eyebrows.
- Nose.
- Upper/lower lips.
- Inner mouth.
- Left/right ears where visible.
- Scalp hair.
- Glasses.
- Hat and face accessories.
- Beard.
- Mustache.
- Stubble.
- Background.
- Unknown or low-confidence occlusion.

The deterministic baseline builds masks in layers:

1. Landmark polygons protect eyes, brows, nostrils, lips, inner mouth, and the likely beard/mustache region.
2. The user confirms several unobscured cheek/forehead skin samples.
3. A robust color-distance likelihood map is created in normalized luminance/chroma and perceptual color spaces.
4. Edge-aware region growth is constrained to the confirmed face outline and stopped at strong color/gradient boundaries.
5. Line, reflection, and repeated-edge cues suggest possible glasses; upper-face color/texture discontinuities suggest possible scalp hair.
6. Geometry zones distinguish likely scalp hair from the protected beard/mustache region, but ambiguous dark texture remains **Unknown**.
7. User strokes resolve uncertain regions and take precedence over every automatic suggestion.

Deterministic cues cannot reliably distinguish all hair, shadows, makeup, tinted lenses, and skin tones. Facial-hair preservation is therefore a conservative release rule: no heuristic or optional enhancer may remove a possible beard, mustache, or stubble region unless the user confirms removal. In a no-correction workflow, ambiguous pixels fall back to the UMA base skin through reduced overlay alpha.

Mask priority:

```text
User Keep
-> Beard / mustache / stubble preserve mask
-> Brows / lashes / lips preserve mask
-> User Remove
-> Glasses / scalp hair / accessory removal mask
-> Skin
-> Unknown
```

A removal mask must never override a confident facial-hair preserve mask without an explicit user stroke.

Manual correction tools:

- Keep.
- Remove.
- Mark as skin.
- Mark as facial hair.
- Unknown/fade.
- Grow/shrink.
- Feather.
- Lasso and connected fill.
- Before/after mask comparison.

## 9. Lighting and color normalization

A photograph contains both skin appearance and environmental lighting. Broad lighting should be reduced before mapping, while identity-bearing high-frequency detail is preserved.

Pipeline:

1. Convert sRGB input into a controlled linear working space.
2. Select high-confidence skin samples away from hair, facial hair, eyes, lips, nostrils, specular highlights, and deep shadows.
3. Estimate exposure, channel balance, and large-scale left/right illumination.
4. Apply bounded white-balance and exposure correction.
5. Separate low-frequency shading from mid/high-frequency color detail using an edge-aware method.
6. Suppress strong cast shadows and highlights without flattening freckles, pores, wrinkles, moles, or facial hair.
7. Convert to the target color space only at export.

The system should prefer a slightly imperfect but photographic result over aggressive color correction that changes skin tone or destroys real facial detail.

## 10. Frontalization and texture warping

### `FaceMappingTemplate`

Each supported UMA face layout needs a versioned template containing:

- Compatible race, slot, material, surface, and UV/UDIM fingerprint.
- Output resolution and color space.
- Canonical face landmarks.
- Corresponding target UV positions.
- Fixed semantic triangle topology.
- Face-validity mask.
- Hairline, temple, ear, jaw, and neck boundary anchors.
- Protected eye, nostril, lip, and inner-mouth regions.
- Default feather/coverage mask.
- Reference base-skin texture and robust reference color samples.
- Template validation images and expected checksums.

Templates must be explicit assets; the mapper must not assume that all UMA races share identical face UVs.

### Warp algorithm

Recommended approach:

1. Correct roll and crop the detected face with safe padding.
2. Use pose and the hand-authored canonical 3D landmark template to project visible pixels toward a frontal canonical domain.
3. Build a fixed semantic triangle mesh from corrected landmarks.
4. Piecewise-affine warp each source triangle to the corresponding target UV triangle.
5. Use smooth local deformation around eyes, nostrils, lips, and the jaw to avoid hard triangle discontinuities.
6. Track a per-pixel confidence/visibility atlas throughout the warp.
7. Dilate valid pixels into padding while preserving semantic boundaries.
8. Blend seams with a multi-resolution or edge-aware compositor.

The fixed topology must be deterministic and must never generate folded or inverted target triangles. Manual pin adjustments should be local and constrained.

### Side-facing photographs

For mild yaw:

- Use the geometric pose solution to correct feature placement.
- Give the visible side higher confidence.
- Reconstruct the partially hidden side from symmetry only where the matching visible area is trustworthy.
- Preserve asymmetric visible details rather than blindly mirroring the whole face.
- Use the underlying UMA base skin and lower overlay alpha where no trustworthy source exists.

This confidence-based fallback is preferable to inventing detailed skin hidden by pose or occlusion.

## 11. Hair and glasses repair

Hair and glasses removal is an occlusion-repair problem: the original skin is not present in those pixels. The output must distinguish repair confidence rather than treating reconstruction as fact.

Repair tiers:

1. **Small skin gaps:** edge-aware diffusion or patch matching from adjacent high-confidence skin.
2. **Narrow glasses frames:** use nearby eyelid, cheek, bridge, and temple samples with bilateral symmetry where appropriate.
3. **Lens tint/reflection:** normalize the affected eye/skin region only when underlying landmarks and deterministic masks remain reliable.
4. **Large forehead or side-face hair coverage:** use target base-skin texture, corrected toward the source skin color, with reduced alpha.
5. **Unknown/ambiguous areas:** retain base UMA skin by setting overlay alpha near zero.

Automatic repair must not erase:

- Beard.
- Mustache.
- Stubble.
- Eyebrows.
- Eyelashes.
- Freckles, moles, scars, or wrinkles that are classified as genuine facial detail.

The deterministic release should include clone/heal and restore-source brushes plus fixed inpainting methods such as directional interpolation, edge-aware diffusion, patch copying with a deterministic seed, and base-skin synthesis. Optional AI repair may be supplied only through the separate enhancer layer, must be visibly disclosed, and must never be required for a trustworthy result.

## 12. Alpha mask and composition

The generated face is an overlay, not a replacement base texture.

Alpha should be derived from:

- Target face-validity mask.
- Source visibility/confidence.
- Occlusion repair confidence.
- Landmark confidence.
- Boundary distance fields.
- User Keep/Remove/Unknown corrections.

Recommended behavior:

- High alpha around the central cheeks, nose, lips, chin, and reliably captured forehead.
- Controlled feathering into temples, hairline, ears, jaw, and neck.
- Reduced alpha on self-occluded or heavily repaired regions.
- Semantic protection around eyeball, inner-mouth, and nostril boundaries.
- Mip-safe dilation outside visible coverage to prevent dark seams.

The user should be able to paint alpha directly and compare the overlay over neutral gray, the original UMA skin, and several shared skin-color previews.

## 13. Body skin-color estimation

Return a structured result rather than an unexplained single swatch:

```text
BodySkinColorResult
    suggestedSharedColor
    relativeColorMultiplier
    confidence
    usableSkinCoverage
    sampleVariance
    shadowAsymmetry
    recommendedInfluence
```

Estimator process:

1. Sample only high-confidence, visible, non-occluded skin from both cheeks, forehead, nose sides, and jaw where available.
2. Exclude facial hair, lips, eyes, nostrils, specular pixels, deep shadows, and repaired pixels.
3. Work in linear RGB and a perceptual comparison space.
4. Use robust medians/trimmed statistics rather than a plain average.
5. Compare normalized face samples with the template's reference base skin.
6. Produce both an absolute suggested color and a relative multiplier against the existing UMA shared skin color.
7. Blend toward the existing body color using a confidence-based influence rather than replacing it unconditionally.

The stage should preview this through `DynamicCharacterAvatar.SetColor`, under Undo, without permanently changing the recipe until the user applies the result.

Skin-color accuracy must be evaluated across a broad range of skin tones, cameras, exposures, and lighting. Related face-analysis technologies have shown that image quality and demographic coverage can materially affect error rates, so cohort-level evaluation and capture-quality controls are required. [NIST face technology evaluations](https://www.nist.gov/programs-projects/face-projects)

## 14. Stage user interface

### Workspace

- Left: input, capture, target template, source-retention choice.
- Center: large editable image/UV/avatar viewport.
- Right: workflow steps, confidence, tools, settings, validation.
- Bottom: before/after, mask channels, history, and output assets.

### View tabs

- Source.
- Quality.
- Landmarks.
- Pose/visibility.
- Masks.
- Occlusion repair.
- Canonical frontal face.
- UMA UV.
- Alpha.
- Avatar preview.

### Essential controls

- Retake/reimport.
- Face selection when multiple faces are detected.
- Landmark handles with reset per feature.
- Mask correction brushes.
- Clone/heal/restore.
- Lighting normalization amount.
- Symmetry and hidden-side fallback amount.
- Alpha feather and coverage.
- Body-color influence.
- Neutral-expression and pose warnings.
- Validate and export.

Every automatic result should display confidence using clear language such as Good, Review, or Recapture rather than exposing raw model scores alone.

## 15. Runtime Face Mapping API

The runtime API is part of the product's minimum viable architecture and release gate. Its deterministic mode must work in play mode in the editor and in supported standalone/player builds with no AI component installed or configured. The editor stage should be implemented on top of this API, not as the owner of the face-mapping algorithms.

### Public service contract

An illustrative contract is:

```csharp
public interface IFaceMappingService
{
    FaceMappingCapabilities GetCapabilities(FaceMappingRuntimeProfile profile);

    Task<FaceMappingRuntimeResult> GenerateAsync(
        Texture source,
        FaceMappingOptions options,
        CancellationToken cancellationToken = default,
        IProgress<FaceMappingProgress> progress = null);

    Task<FaceMappingRuntimeResult> ContinueAsync(
        FaceMappingSession session,
        FaceMappingCorrections corrections,
        CancellationToken cancellationToken = default,
        IProgress<FaceMappingProgress> progress = null);
}
```

The final API may use Unity's supported async primitive where that improves platform compatibility, but it must retain the same semantics: non-blocking operation, progress, cancellation, structured completion, and predictable cleanup. A coroutine adapter may be supplied for applications that do not use `async`/`await`.

Provide a straightforward documented construction path, such as a factory that takes `FaceMappingRuntimeProfile`, plus an optional scene component for inspector-driven applications. The service must not require a hidden singleton, an editor-created document, or a particular UI framework. Public contracts require semantic versioning and migration notes once released.

Required API behavior:

- Accept the source as a Unity `Texture`; do not require a file path, imported asset, readable import setting, or editor selection.
- Perform the complete deterministic pipeline without editor APIs, pausing with a resumable correction request only when confirmation is required for a trustworthy result.
- Be callable from game code after dependencies have been initialized or preloaded.
- Report CPU/GPU implementation availability and separately report whether the specifically configured optional enhancer is available.
- Report coarse progress stages suitable for an application's loading UI.
- Treat cancellation as a normal outcome and release partial GPU/CPU resources promptly.
- Return typed quality/recapture/correction states separately from configuration, resource, optional-enhancer, and internal failures.
- Accept corrected bounds, landmark handles, protected/removal masks, and skin samples through a public continuation API so a host can resume the same operation without restarting.
- Marshal Unity object creation, mutation, and destruction to the main thread while allowing fixed image processing and pure CPU work to run asynchronously where safe.
- Support multiple sequential requests and define whether concurrent requests are queued, rejected, or isolated according to the selected execution path.
- Never log, transmit, or persist the source texture unless the host explicitly opts into a documented feature that does so.
- Guarantee that **Deterministic Only** never instantiates an enhancer, loads learned weights, or performs network I/O.
- In **Use Optional Enhancer**, fall back to the deterministic flow when the enhancer is absent or fails, unless the host explicitly requests that the operation pause and report the enhancer failure.

### Automated runtime `OverlayDataAsset` construction

On successful mapping, `IRuntimeOverlayDataAssetFactory` must automatically create and configure an in-memory `OverlayDataAsset` using supported runtime APIs such as `ScriptableObject.CreateInstance<OverlayDataAsset>()`. The caller must not have to understand UMA material channel layout or manually populate the asset.

The factory must set and validate:

- A deterministic or caller-correlated runtime name that avoids collisions within the application session.
- The target `UMAMaterial` from the validated runtime profile/template.
- `OverlayType.Normal`, the correct destination rectangle, and any UV/tile contract required by the template.
- `textureList` in the exact material-channel order, including generated albedo and any required safe fallback textures for other channels.
- `alphaMask` or the validated albedo-alpha arrangement required by the material.
- Per-channel blend modes, texture names where applicable, tags, and duplicate-merge behavior.
- Texture dimensions, graphics formats, mip state, wrap/filter modes, and linear/sRGB expectations.

The result should also be able to return a ready-to-attach `OverlayData` instance so common consumers do not need an extra construction step. The runtime overlay is used by direct object reference and does not need to be registered with the editor UMA asset index.

### Avatar application helper

Provide a small runtime helper that can:

1. Validate that the result's template/material is compatible with the target avatar and face slot.
2. Add or replace the generated face overlay in the avatar's live recipe by direct reference.
3. Apply the recommended shared skin color at the requested influence.
4. Trigger the appropriate UMA rebuild.
5. Restore or remove the prior generated overlay cleanly.

The lower-level result remains usable without this helper for applications with custom recipe management.

### Player-build packaging

`FaceMappingRuntimeProfile` must explicitly reference every shader, compute shader, target template, UMA material, lookup texture, and fallback texture needed by deterministic mode so Unity build stripping does not remove them. Addressables or another asynchronous content system may be supported, but an offline/default package must remain available and initialization failures must be visible through `GetCapabilities` or a typed load result.

The deterministic runtime assembly requires validation on each supported graphics backend, scripting backend, and target platform. IL2CPP/AOT compatibility, shader variants, compute support, CPU fallback, memory limits, and camera permissions are release criteria rather than application integrator responsibilities. Optional enhancer packages own their additional build resources and compatibility declarations and cannot weaken the deterministic build matrix.

### Ownership, caching, and persistence

- The input texture remains owned by the caller.
- Generated textures, transient `OverlayDataAsset`, and optional `OverlayData` are owned by the result/lifetime handle until ownership is explicitly transferred.
- Disposing a result must not destroy resources still attached to an avatar; the helper must provide detach/replace semantics or reference-counted ownership.
- Repeated requests may use a bounded cache keyed by source content hash, template version, deterministic algorithm version, optional enhancer provenance when applicable, and material options. Raw source pixels must not be retained as a hidden cache key payload.
- Runtime persistence, when an application needs it, should serialize encoded generated textures plus a versioned reconstruction record into an application-owned data location. On a later session, the API reconstructs the transient `OverlayDataAsset` from that record.
- Runtime code must never call `AssetDatabase`, create `.asset` files, update the editor UMA index, or assume a writable project directory.

### Runtime integration example and samples

Ship a deterministic play-mode sample that obtains a `Texture`, calls `GenerateAsync`, handles a correction request with the supplied face-guide controls, applies the returned overlay to a `DynamicCharacterAvatar`, handles cancellation, and releases it safely. The sample must compile and run with every optional enhancer package removed. Also provide API XML documentation and a minimal integration guide covering initialization, processing mode, profile selection, corrections, permissions, privacy, platform capabilities, errors, and lifetime management.

## 16. Editor persistence and UMA export

Editor export is an optional persistence layer over a successful runtime result. It must not be required to generate, preview, or equip the overlay during play mode.

Recommended project-owned paths:

```text
Assets/UMAProjectData/FaceMapper/
    Documents/
    Sources/       # only when the user opts in
    Generated/
    Recovery/
```

Export should create:

- Face albedo PNG with controlled sRGB import settings.
- Alpha mask PNG or a validated alpha channel according to the UMA material contract.
- `OverlayDataAsset` with the correct material, overlay type, texture list, and alpha mask.
- Optional `UMAWardrobeRecipe` or configured face recipe.
- `FaceMappingResult` asset containing the body-color recommendation, confidence, algorithm/template versions, optional enhancer provenance, and processing mode.
- Export record with deterministic content hashes.

The editor exporter should accept `FaceMappingRuntimeResult`, then follow Overlay Painter's transaction pattern: write temporary results, import and validate them, create/update Unity assets, update the UMA index, and commit only when every required output succeeds. Cancellation or failure must leave the previous valid assets intact.

Original textures and Unity assets must never be modified as text. All asset creation and mutation must use supported Unity serialization and `AssetDatabase` APIs.

## 17. Privacy and security requirements

The system processes a photograph of a person and must be privacy-first even though it performs no identity matching.

Requirements:

- Local processing by default.
- Deterministic mode performs no network requests for localization, masking, repair, warping, or export.
- No identity database, search, or persistent identity embedding.
- Camera starts only after an explicit user action and visibly stops after capture or cancellation.
- Raw source persistence requires an explicit choice.
- Clear delete-source and delete-document actions.
- Metadata stripped from generated textures.
- Raw images excluded from logs, analytics, crash attachments, screenshots, golden-test updates, and diagnostic bundles.
- Temporary CPU and GPU image resources disposed when no longer needed.
- Optional AI is off by default and requires explicit host configuration; a remote provider requires an additional explicit end-user disclosure/consent step before any image leaves the device.
- Optional enhancer packages must document retention, telemetry, subprocesses, model/training-data licenses, provider terms, geographic processing, costs, and deletion controls before distribution.
- Runtime API documentation must make the host application responsible for capture consent, retention choices, and platform privacy disclosures while enforcing safe no-persistence/no-network defaults in the mapper.
- Complete legal/privacy review before shipping webcam capture in a runtime product.

NIST describes local-device processing and data minimization as useful privacy-risk controls. [NIST Privacy Framework Core](https://www.nist.gov/system/files/documents/2021/05/05/NIST-Privacy-Framework-V1.0-Core-PDF.pdf)

## 18. Performance targets

Reference input: one 12-megapixel image, one detected face, 2048 or 4096 output, desktop player or editor with supported compute GPU.

Provisional targets:

- Live deterministic capture guidance: 15-30 FPS, with expensive image analysis throttled to the rate needed for stable feedback.
- Still-image face-region, landmark-suggestion, and pose analysis: under 250 ms GPU, under 1 second CPU reference path.
- Deterministic mask construction: under 500 ms GPU, under 2 seconds CPU reference path.
- Warp, masks, repair, and color analysis: under 500 ms at 2048 output.
- Complete deterministic suggestion/preview: under 2 seconds GPU, under 5 seconds CPU, excluding time spent waiting for user confirmation.
- Runtime service initialization and shader/buffer warm-up must be asynchronous, measurable separately from per-face processing, and available as an explicit preload step.
- Manual landmark/mask preview: under 33 ms after warm-up.
- No recurring managed allocations during steady webcam preview.
- Cancellable processing with no editor UI or player main-thread stalls longer than 100 ms; normal progress polling and application frames should remain responsive.

Use asynchronous fixed image processing and GPU work where supported. `AsyncGPUReadback` should be used when CPU access is needed without forcing synchronous render-texture reads. [Unity AsyncGPUReadback API](https://docs.unity3d.com/jp/current/ScriptReference/Rendering.AsyncGPUReadback.Request.html)

## 19. Validation and testing

### Algorithm metrics

- Landmark normalized mean error, normalized by inter-eye distance.
- Pose angular error.
- Deterministic/edit-corrected mask intersection-over-union/F1 per class.
- Glasses and scalp-hair removal precision/recall.
- Beard/mustache/stubble preservation recall.
- Percentage of repaired pixels and their confidence.
- Folded/inverted warp triangle count, required to be zero.
- UMA target coverage and seam error.
- Skin-color error in a perceptual color space under standardized reference captures.
- Deterministic output hash for the same input and settings.

### Unit tests

- Runtime request validation and capability reporting.
- Deterministic-only dependency guard and zero-network enforcement.
- Optional-enhancer unavailable/failure fallback to a resumable deterministic session.
- Non-readable `Texture2D`, `RenderTexture`, and frozen `WebCamTexture` ingestion.
- Image orientation and mirrored-webcam correction.
- sRGB/linear conversions.
- Landmark transforms and manual overrides.
- Pose and visibility math.
- Fixed triangle topology and fold prevention.
- Mask priority, especially facial-hair preservation.
- Confidence propagation.
- Symmetry mapping.
- Alpha distance fields and semantic protection.
- Robust skin-color statistics.
- Runtime `OverlayDataAsset` channel, material, alpha, blend, tag, and rectangle construction.
- Runtime result ownership, cancellation cleanup, detach/replace, and cache eviction.
- Export naming, hashing, rollback, and source-retention rules.

### Integration tests

- Play-mode API invocation with no editor stage open.
- Complete texture-to-overlay-to-avatar flow without `AssetDatabase` or UMA index registration.
- Repeated generation, cancellation at every progress phase, scene unload, avatar destruction, and application shutdown.
- Runtime profile initialization with stripped shaders/resources, unsupported compute, CPU fallback, and low-memory conditions.
- Camera permission denied.
- Camera unplugged or unavailable.
- Import canceled, corrupt file, unsupported format, or oversized image.
- No face, multiple faces, very small face.
- Frontal and mildly turned faces in both directions.
- Roll, mild pitch, smile, open mouth, and closed eyes.
- Clear glasses, tinted glasses, thick frames, and reflections.
- Hair over forehead, temple, eye, and cheek.
- Beard, mustache, stubble, eyebrows, freckles, scars, and makeup.
- Broad range of skin tones, ages, hair types, and camera qualities.
- Multiple supported UMA races, face slots, materials, UV layouts, and UDIM configurations.
- Save/reopen, recovery, deterministic algorithm upgrade, optional-enhancer provenance changes, and template migration.
- Apply/revert body shared color.
- Export and equip overlay/recipe on a rebuilt avatar.

### Player-build tests

- Automated smoke builds for every supported target platform and graphics backend.
- Mono and IL2CPP/AOT coverage where supported by the target.
- Source supplied from a runtime-created `Texture2D` and from a `RenderTexture`.
- Generated transient `OverlayDataAsset` applied to an UMA avatar, rebuilt, replaced, and removed.
- Build-stripping validation for shaders, deterministic lookup resources, templates, materials, and fallback textures.
- Offline execution with network access disabled.
- Deterministic sample compilation and execution with all optional enhancer packages removed.
- Optional enhancer enabled, declined, unavailable, timed out, canceled, and removed between builds; every case retains deterministic functionality.
- Frame responsiveness, peak CPU/GPU memory, cleanup, and repeated-run leak checks.
- A sample application test that exercises only public APIs and contains no editor assembly reference.

### Test data

Use only properly licensed, consented, or synthetic images. Maintain a protected evaluation corpus that is not shipped with the package. Golden images should contain generated textures or authorized synthetic subjects, not unapproved personal photographs.

## 20. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Hidden skin cannot be recovered from one photo | Confidence atlas, base-skin fallback, reduced alpha, manual repair, optional second photo |
| Glasses overlap eyes and brows | Semantic protection, local repair, user correction, conservative fallback |
| Facial hair is confused with scalp hair or shadow | Protected geometry zones, preserve-mask priority, conservative Unknown state, release metrics, manual Keep tool |
| Strong lighting becomes part of the texture | Quality gate, bounded low-frequency normalization, confidence warning |
| Side pose distorts the hidden half | 3D pose-aware frontalization, visibility weighting, limited symmetry, recapture threshold |
| Face texture does not match avatar geometry | Clear scope, template preview, later optional DNA-fitting project |
| UVs differ between races or content packs | Versioned `FaceMappingTemplate` per verified layout |
| Deterministic cues cannot resolve a difficult face | Guided primary landmarks and masks, typed correction/recapture state, conservative alpha, no fabricated confidence |
| Optional enhancer is unavailable, costly, or unsuitable | Deterministic default, separate removable package, explicit configuration and consent, declared licenses/privacy/costs, automatic deterministic fallback |
| Raw photos leak into project/build/logs | Session-only default, explicit retention, build validation, diagnostic redaction |
| Accuracy varies across users or capture conditions | Diverse evaluation matrix, cohort reporting, capture-quality assistance, manual correction |
| Editor-only dependencies make the feature unusable in a player | Runtime-first assembly boundary, automated forbidden-reference checks, player-build tests, editor stage consuming the public runtime API |
| Runtime overlay resources leak or are destroyed while equipped | Explicit result ownership, attach/detach helper, bounded cache, lifecycle and repeated-run tests |
| Build stripping removes shaders or lookup resources | Explicit runtime profile references, resource build validation, capability check, platform smoke builds |
| A host blocks the main thread while mapping | Async contract, throttled progress, main-thread work budgets, GPU readback, responsiveness tests |
| An optional enhancer becomes a hidden dependency | Separate assembly/package, dependency scan, deterministic-only CI build, zero-enhancer sample, fallback tests |

## 21. Implementation roadmap

| Phase | Scope | Exit criterion |
|---|---|---|
| 0. Deterministic CV/graphics spike | Face-guide workflow, fixed image operations, geometric landmark/pose prototype, mask heuristics, runtime assembly proof, fixed UMA warp | A play-mode call maps one texture with no AI package, model file, provider, network, or editor dependency |
| 1. Public runtime/session API | Texture ingestion, request/result/correction contract, async progress/cancellation, capability checks, lifetime management | A sample passes a texture, completes or corrects a resumable session, and receives a disposable mapped result |
| 2. Runtime UMA integration | Runtime profile, transient `OverlayDataAsset` factory, `OverlayData`, avatar apply/replace/remove helper, build resources | A player build generates and equips a face overlay without `AssetDatabase` or UMA index registration |
| 3. Stage and input | Stage lifecycle over the runtime API, file import, webcam, permissions, quality UI, source-retention policy | Safe capture/import and cleanup work through domain reload and cancellation |
| 4. Landmarks and pose | Dense landmarks, pose, visibility, correction handles, confidence | Frontal and mildly turned test faces align reliably |
| 5. UMA templates and warping | Template asset, fixed topology, GPU warp, UV/UDIM validation, manual pins | Mapped facial features land correctly on a generated avatar |
| 6. Deterministic masks and repair | Skin samples, feature protection, hair/glasses cues, correction UI, fixed repair tiers | Glasses and scalp hair can be corrected while facial hair is conservatively retained |
| 7. Color and composition | Lighting normalization, confidence atlas, alpha builder, body-color estimator | Overlay blends cleanly and returns a defensible shared skin color |
| 8. Editor persistence/export | Transactional texture import, persistent overlay, optional recipe, index update, provenance | Runtime results can optionally become validated project assets |
| 9. Production UX and hardening | Clone/heal, history, recovery, diverse QA, performance/privacy tests, deterministic runtime docs and samples | Artist tooling and zero-AI embedded player use pass every release gate |
| 10. Optional enhancer SDK | `IFaceMappingEnhancer`, separate package boundary, provenance, consent, fallback, compatibility tests | Enhancers improve suggestions when opted in but can be removed without affecting deterministic builds |

Suggested team: two tools/graphics/computer-vision engineers, one technical artist focused on face guides and masks, and part-time QA/privacy review. An AI integration specialist is needed only for the optional enhancer package, not the core schedule.

Estimated schedule:

- Runtime texture-to-overlay vertical slice: 6-8 weeks.
- Artist-usable alpha: 3-4 months.
- Production beta with occlusion handling, runtime application, and editor export: 5-6 months.
- Hardened release with diverse QA and correction tools: 7-9 months.

## 22. Recommended first vertical slice

1. From play mode, pass an upright runtime-created `Texture2D` to the public async service; defer webcam until the same texture contract is proven.
2. Require or suggest the minimal face guide and primary landmarks using only fixed image processing and hand-authored geometry.
3. Return a resumable correction request when those points cannot be trusted; apply corrections through the public session API.
4. Estimate pose and accept frontal or mildly turned faces using the geometric template.
5. Map to one versioned standard UMA 3 face template with a fixed triangle warp.
6. Generate a conservative confidence-weighted alpha mask from confirmed face bounds, feature zones, and skin samples.
7. Estimate a body shared skin color from confirmed cheek and forehead samples.
8. Automatically create a transient, fully configured `OverlayDataAsset` and `OverlayData` from the generated textures.
9. Apply the returned overlay to a generated UMA avatar using only runtime APIs, rebuild it, then replace/remove and dispose it safely.
10. Exercise progress, correction, cancellation, typed failure, CPU fallback, and unsupported-graphics behavior.
11. Run the same flow in at least one IL2CPP player build with network access disabled and no optional enhancer package present.
12. Add the editor-stage preview and persistence adapter on top of the exact same runtime result.
13. Save algorithm/template versions, processing provenance, and deterministic output hashes without retaining the source image.
14. Add golden tests for frontal, left/right mild yaw, light/dark skin, beard, glasses, and forehead hair.

The next slice adds richer deterministic masks, protected facial hair, glasses/hair repair, webcam capture, manual brushes, recipe export, and additional player platforms. The optional enhancer SDK follows only after the deterministic texture-to-overlay workflow meets its own quality and player-build gates.

## Definition of done

The system is complete only when both authoring and embedded runtime workflows pass in **Deterministic Only** mode with all optional enhancer packages removed and network access disabled. A user must be able to safely capture or import a face photo, confirm or correct deterministic facial landmarks for frontal and mildly turned poses, correct glasses and scalp-hair occlusions without removing beard or mustache, map the trusted facial appearance into every supported UMA face template, preview a confidence-weighted overlay on an avatar, obtain and optionally apply a body skin-color recommendation, and export validated recipe-ready assets. Independently, a shipped application must be able to pass a Unity `Texture` to the public play-mode API, run or resume the deterministic pipeline asynchronously, receive a correctly configured transient `OverlayDataAsset`, equip/rebuild/replace/remove it on an UMA avatar without editor code or asset indexing, and release all generated resources safely. Neither path may retain or transmit the raw photograph unless the user explicitly chooses to do so. An optional AI enhancer may improve suggestions after explicit configuration/consent, but its absence, failure, or removal must never block or degrade the guaranteed deterministic workflow.
