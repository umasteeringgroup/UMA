using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    public sealed class UMAPresetEditorWindow : EditorWindow
    {
        private DynamicCharacterAvatar source;
        private UMAPreset editing;
        private AvatarDefinition snapshot;
        private readonly HashSet<string> dna = new HashSet<string>();
        private readonly HashSet<string> colors = new HashSet<string>();
        private readonly HashSet<string> wardrobe = new HashSet<string>();
        private readonly Dictionary<string, UMATextRecipe> recipeReferences = new Dictionary<string, UMATextRecipe>();
        private Texture2D capturedIcon;
        private Image preview;
        private Label status;
        private SceneView captureView;
        private bool capturing, drawing;
        private Vector2 start, end;
        private Rect captureViewport;
        private bool awaitingRender;
        private double captureDeadline;
        private VisualElement retakeControls;
        private DynamicCharacterAvatar.PresetScreenshotFraming retakeFraming;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshRetakeButton;
        }

        private void RefreshRetakeButton()
        {
            if (retakeControls == null) return;
            retakeControls.Clear();
            if (source != null && source.lastPresetScreenshot != null && source.lastPresetScreenshot.IsValid)
                retakeControls.Add(new Button(RetakeFromLastPosition) { text = "Retake from last position" });
        }

        public static void Open(DynamicCharacterAvatar avatar, UMAPreset preset = null)
        {
            if (avatar == null || avatar.activeRace == null || avatar.activeRace.data == null)
            {
                EditorUtility.DisplayDialog("UMA preset", "Select an initialized DynamicCharacterAvatar first.", "OK");
                return;
            }
            var window = CreateInstance<UMAPresetEditorWindow>();
            window.titleContent = new GUIContent("Create / Edit UMA Preset");
            window.minSize = new Vector2(420, 450);
            window.source = avatar;
            window.editing = preset;
            window.snapshot = avatar.GetAvatarDefinition(false, false);
            if (preset != null && !string.IsNullOrEmpty(preset.Definition.RaceName) && preset.Definition.RaceName != window.snapshot.RaceName)
            {
                DestroyImmediate(window);
                EditorUtility.DisplayDialog("UMA preset", "Use a source avatar of the same race as the preset.", "OK");
                return;
            }
            foreach (var recipe in avatar.WardrobeRecipes.Values)
                if (recipe != null) window.recipeReferences[recipe.name] = recipe;
            foreach (var list in avatar.AdditiveRecipes.Values)
                foreach (var recipe in list)
                    if (recipe != null) window.recipeReferences[recipe.name] = recipe;
            foreach (var value in (preset != null ? preset.Definition : window.snapshot).Dna ?? Array.Empty<DnaDef>()) window.dna.Add(value.Name);
            foreach (var value in (preset != null ? preset.Definition : window.snapshot).Colors ?? Array.Empty<SharedColorDef>()) window.colors.Add(value.name);
            foreach (var value in (preset != null ? preset.Definition : window.snapshot).Wardrobe ?? Array.Empty<string>()) window.wardrobe.Add(value);
            window.ShowUtility();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UMAEditorUtilities.FindUMAFullPath() + "/Core/Editor/Scripts/UMAPresetEditorWindow.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("uma-preset-root");
            if (source == null) { rootVisualElement.Add(new Label("Reopen this window from the avatar's Presets section.")); return; }
            rootVisualElement.Add(new HelpBox("Choose the values to include. Values come from the source avatar when this popup opens. Omitted DNA and colors remain unchanged; the selected wardrobe recipes replace the avatar's complete wardrobe when applied. Presets apply to the same race.", HelpBoxMessageType.Info));
            rootVisualElement.Add(new Label(source.name + " / " + snapshot.RaceName));
            var scroll = new ScrollView();
            scroll.AddToClassList("uma-preset-options");
            rootVisualElement.Add(scroll);
            AddChoices(scroll, "DNA", snapshot.Dna?.Select(value => value.Name), dna);
            AddChoices(scroll, "Colors (including shader parameters)", snapshot.Colors?.Select(value => value.name), colors);
            AddChoices(scroll, "Wardrobe recipes", snapshot.Wardrobe, wardrobe);
            preview = new Image { image = editing != null ? editing.Icon : null, scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("uma-preset-icon");
            rootVisualElement.Add(preview);
            rootVisualElement.Add(new Button(BeginCapture) { text = "Draw face icon in Scene View..." });
            retakeControls = new VisualElement();
            rootVisualElement.Add(retakeControls);
            RefreshRetakeButton();
            status = new Label("An icon is required. Frame the face, then drag a square around it.");
            status.AddToClassList("uma-preset-status");
            rootVisualElement.Add(status);
            rootVisualElement.Add(new Button(Save) { text = editing == null ? "Save preset..." : "Save preset changes" });
        }

        private static void AddChoices(VisualElement parent, string title, IEnumerable<string> names, HashSet<string> selection)
        {
            var foldout = new Foldout { text = title, value = true };
            parent.Add(foldout);
            var toggles = new List<Toggle>();
            var commands = new VisualElement();
            commands.AddToClassList("uma-preset-commands");
            commands.Add(new Button(() => { foreach (var toggle in toggles) toggle.value = true; }) { text = "All" });
            commands.Add(new Button(() => { foreach (var toggle in toggles) toggle.value = false; }) { text = "None" });
            foldout.Add(commands);
            foreach (string name in (names ?? Enumerable.Empty<string>()).Distinct().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var toggle = new Toggle(name) { value = selection.Contains(name) };
                toggle.RegisterValueChangedCallback(evt => { if (evt.newValue) selection.Add(name); else selection.Remove(name); });
                toggles.Add(toggle);
                foldout.Add(toggle);
            }
        }

        private static float ScreenshotScale(Transform avatar)
        {
            return Mathf.Max(0.0001f, avatar.lossyScale.magnitude / Mathf.Sqrt(3f));
        }

        private static DynamicCharacterAvatar.PresetScreenshotFraming RememberFraming(
            Transform avatar, Camera camera, float sceneSize, Rect viewport)
        {
            float scale = ScreenshotScale(avatar);
            float halfHeight = camera.orthographic
                ? camera.orthographicSize / scale
                : Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * camera.aspect;
            return new DynamicCharacterAvatar.PresetScreenshotFraming
            {
                cameraPosition = avatar.InverseTransformPoint(camera.transform.position),
                cameraRotation = Quaternion.Inverse(avatar.rotation) * camera.transform.rotation,
                orthographic = camera.orthographic,
                sceneSize = sceneSize / scale,
                projectionCrop = new Rect((viewport.x * 2f - 1f) * halfWidth,
                    (viewport.y * 2f - 1f) * halfHeight,
                    viewport.width * 2f * halfWidth, viewport.height * 2f * halfHeight)
            };
        }

        private static void RestoreCaptureView(SceneView view, Transform avatar,
            DynamicCharacterAvatar.PresetScreenshotFraming framing)
        {
            Rect crop = framing.projectionCrop;
            float extent = Mathf.Max(Mathf.Abs(crop.xMin), Mathf.Abs(crop.xMax),
                Mathf.Abs(crop.yMin), Mathf.Abs(crop.yMax)) * 1.05f;
            float scale = ScreenshotScale(avatar);
            float size = framing.orthographic ? extent * scale : framing.sceneSize * scale;
            float fov = Mathf.Clamp(2f * Mathf.Atan(extent) * Mathf.Rad2Deg, 4f, 140f);
            view.in2DMode = false;
            view.cameraSettings.fieldOfView = fov;
            Quaternion rotation = avatar.rotation * framing.cameraRotation;
            Vector3 position = avatar.TransformPoint(framing.cameraPosition);
            // Scene View's size/FOV refer to the shorter viewport dimension.
            float distance = framing.orthographic ? size * 2f : size / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad);
            view.LookAt(position + rotation * Vector3.forward * distance, rotation, size, framing.orthographic, true);
        }

        private static Rect RetakeViewport(Camera camera, Transform avatar,
            DynamicCharacterAvatar.PresetScreenshotFraming framing)
        {
            float halfHeight = camera.orthographic
                ? camera.orthographicSize / ScreenshotScale(avatar)
                : Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * camera.aspect;
            Rect crop = framing.projectionCrop;
            var viewport = new Rect(0.5f + crop.x / (2f * halfWidth),
                0.5f + crop.y / (2f * halfHeight),
                crop.width / (2f * halfWidth), crop.height / (2f * halfHeight));
            if (viewport.xMin < -0.0001f || viewport.yMin < -0.0001f ||
                viewport.xMax > 1.0001f || viewport.yMax > 1.0001f)
                throw new InvalidOperationException("The saved framing is outside this Scene View's camera range.");
            return viewport;
        }

        private void RetakeFromLastPosition()
        {
            StopCapture();
            if (source == null || source.lastPresetScreenshot == null || !source.lastPresetScreenshot.IsValid) return;
            captureView = SceneView.lastActiveSceneView;
            if (captureView == null) captureView = GetWindow<SceneView>();
            retakeFraming = source.lastPresetScreenshot;
            RestoreCaptureView(captureView, source.transform, retakeFraming);
            capturing = true;
            SceneView.duringSceneGui += CaptureGUI;
            AwaitCapture();
            status.text = "Restoring the saved head framing and taking the screenshot...";
            captureView.Focus();
        }

        private void AwaitCapture()
        {
            awaitingRender = true;
            captureDeadline = EditorApplication.timeSinceStartup + 5;
            RenderPipelineManager.endCameraRendering += CaptureRenderedCamera;
            Camera.onPostRender += CaptureBuiltInCamera;
            EditorApplication.update += CheckCaptureTimeout;
            captureView.Repaint();
        }

        private void BeginCapture()
        {
            StopCapture();
            captureView = SceneView.lastActiveSceneView;
            if (captureView == null) captureView = GetWindow<SceneView>();
            capturing = true;
            SceneView.duringSceneGui += CaptureGUI;
            status.text = "In Scene View, drag a square around the face. Escape cancels. Alt-drag still navigates.";
            captureView.Focus();
            captureView.Repaint();
        }

        private void CaptureGUI(SceneView view)
        {
            if (!capturing || view != captureView) return;
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                StopCapture(); evt.Use(); return;
            }
            if (awaitingRender) return;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                start = end = evt.mousePosition; drawing = true; GUIUtility.hotControl = control; evt.Use();
            }
            else if (drawing && evt.type == EventType.MouseDrag && evt.button == 0)
            {
                end = evt.mousePosition; evt.Use(); view.Repaint();
            }
            else if (drawing && evt.type == EventType.MouseUp && evt.button == 0)
            {
                end = evt.mousePosition;
                var rect = SquareSelection(start, end);
                GUIUtility.hotControl = 0;
                drawing = false;
                try
                {
                    if (rect.width < 8 || rect.height < 8) throw new InvalidOperationException("Draw a larger square around the face.");
                    captureViewport = GetCaptureViewport(view.camera, rect);
                    AwaitCapture();
                    status.text = "Capturing the next rendered Scene View frame...";
                    view.Repaint();
                }
                catch (Exception ex) { status.text = ex.Message; }
                evt.Use();
                return;
            }
            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, 420, 26), "UMA preset: drag around the face; Escape cancels.", EditorStyles.helpBox);
            if (drawing)
            {
                var rect = SquareSelection(start, end);
                GUI.Box(rect, GUIContent.none, EditorStyles.selectionRect);
            }
            Handles.EndGUI();
        }

        private static Rect SquareSelection(Vector2 anchor, Vector2 cursor)
        {
            Vector2 delta = cursor - anchor;
            float size = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            return new Rect(delta.x < 0 ? anchor.x - size : anchor.x,
                delta.y < 0 ? anchor.y - size : anchor.y, size, size);
        }

        private static Rect GetCaptureViewport(Camera camera, Rect selection)
        {
            // Ray conversion accounts for the Scene View toolbar and the editor's display scaling.
            var a = camera.WorldToViewportPoint(HandleUtility.GUIPointToWorldRay(selection.min).GetPoint(camera.nearClipPlane + 1));
            var b = camera.WorldToViewportPoint(HandleUtility.GUIPointToWorldRay(selection.max).GetPoint(camera.nearClipPlane + 1));
            // Do not clip axes independently: that would distort a square crop at the view edges.
            if (Mathf.Min(a.x, b.x) < 0 || Mathf.Min(a.y, b.y) < 0 ||
                Mathf.Max(a.x, b.x) > 1 || Mathf.Max(a.y, b.y) > 1)
                throw new InvalidOperationException("Keep the entire square inside the Scene View and try again.");
            float x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
            float width = Mathf.Max(a.x, b.x) - x, height = Mathf.Max(a.y, b.y) - y;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("The selection is outside the camera view.");
            return new Rect(x, y, width, height);
        }

        private void CaptureBuiltInCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) CaptureRenderedCamera(default, camera);
        }

        private void CaptureRenderedCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!awaitingRender || captureView == null || camera != captureView.camera) return;
            if (retakeFraming != null)
            {
                if (source == null) { StopCapture(); return; }
                Vector3 position = source.transform.TransformPoint(retakeFraming.cameraPosition);
                Quaternion rotation = source.transform.rotation * retakeFraming.cameraRotation;
                // Wait for Scene View to apply the restored pose before reading its render texture.
                if (camera.orthographic != retakeFraming.orthographic ||
                    Vector3.Distance(camera.transform.position, position) > Mathf.Max(0.001f * ScreenshotScale(source.transform), position.magnitude * 0.000001f) ||
                    Quaternion.Angle(camera.transform.rotation, rotation) > 0.1f)
                {
                    captureView.Repaint();
                    return;
                }
            }
            try
            {
                if (retakeFraming != null)
                    captureViewport = RetakeViewport(camera, source.transform, retakeFraming);
                // Scene View clears and reuses this texture for GUI after rendering the camera.
                // Copy now, never from duringSceneGui or a later editor update.
                CaptureIcon(camera.targetTexture, captureViewport);
                if (source != null && retakeFraming == null)
                {
                    Undo.RecordObject(source, "Remember preset screenshot framing");
                    source.lastPresetScreenshot = RememberFraming(source.transform, camera, captureView.size, captureViewport);
                    EditorUtility.SetDirty(source);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    RefreshRetakeButton();
                }
                status.text = "Icon captured. Save the preset to write its PNG and asset.";
            }
            catch (Exception ex) { status.text = "Capture failed: " + ex.Message; }
            finally { StopCapture(); }
            EditorApplication.delayCall += FocusAfterCapture;
        }

        private void FocusAfterCapture()
        {
            if (this != null) Focus();
        }

        private void CheckCaptureTimeout()
        {
            if (awaitingRender && (captureView == null || EditorApplication.timeSinceStartup > captureDeadline))
            {
                status.text = "Scene View did not render. Keep it visible and try again.";
                StopCapture();
            }
        }

        private void CaptureIcon(RenderTexture scene, Rect viewport)
        {
            if (scene == null || !scene.IsCreated()) throw new InvalidOperationException("The Scene View has no rendered image.");
            var previous = RenderTexture.active;
            var cropped = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D result = null;
            try
            {
                Graphics.Blit(scene, cropped, viewport.size, viewport.position);
                RenderTexture.active = cropped;
                result = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); result.Apply();
                if (capturedIcon != null) DestroyImmediate(capturedIcon);
                capturedIcon = result;
                preview.image = capturedIcon;
            }
            catch { if (result != null) DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(cropped); }
        }

        private void Save()
        {
            if (source == null) { status.text = "The source avatar no longer exists."; return; }
            if (capturedIcon == null && (editing == null || editing.Icon == null)) { BeginCapture(); return; }
            string path = editing != null ? AssetDatabase.GetAssetPath(editing) : EditorUtility.SaveFilePanelInProject("Save UMA preset", source.name + " Preset", "asset", "Choose a location for the preset and face icon.", UMAPresetAssetUtility.LastSaveFolder);
            if (string.IsNullOrEmpty(path)) return;
            if (editing == null) path = AssetDatabase.GenerateUniqueAssetPath(path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) { status.text = "Save presets under Assets."; return; }
            try
            {
                var definition = new AvatarDefinition
                {
                    RaceName = snapshot.RaceName,
                    Dna = (snapshot.Dna ?? Array.Empty<DnaDef>()).Where(value => dna.Contains(value.Name)).ToArray(),
                    Colors = (snapshot.Colors ?? Array.Empty<SharedColorDef>()).Where(value => colors.Contains(value.name)).ToArray(),
                    Wardrobe = (snapshot.Wardrobe ?? Array.Empty<string>()).Where(wardrobe.Contains).ToArray()
                };
                var references = definition.Wardrobe.Select(name => recipeReferences.TryGetValue(name, out var recipe) ? recipe : null).ToArray();
                if (references.Any(recipe => recipe == null)) throw new InvalidOperationException("A selected wardrobe recipe is no longer available. Reopen the popup.");
                Texture2D icon = editing != null ? editing.Icon : null;
                if (capturedIcon != null)
                {
                    string iconPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, null) + "_Icon.png");
                    File.WriteAllBytes(iconPath, capturedIcon.EncodeToPNG());
                    AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                    importer.textureType = TextureImporterType.Default;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                }
                var preset = editing != null ? editing : CreateInstance<UMAPreset>();
                if (editing != null) Undo.RecordObject(preset, "Edit UMA preset");
                preset.Definition = definition;
                preset.WardrobeRecipes = references;
                preset.Icon = icon;
                if (editing == null) AssetDatabase.CreateAsset(preset, path);
                EditorUtility.SetDirty(preset); AssetDatabase.SaveAssetIfDirty(preset);
                UMAPresetAssetUtility.RememberSaveFolder(path);
                EditorGUIUtility.PingObject(preset);
                Close();
            }
            catch (Exception ex) { status.text = ex.Message; Debug.LogException(ex); }
        }

        private void StopCapture()
        {
            RenderPipelineManager.endCameraRendering -= CaptureRenderedCamera;
            Camera.onPostRender -= CaptureBuiltInCamera;
            EditorApplication.update -= CheckCaptureTimeout;
            EditorApplication.delayCall -= FocusAfterCapture;
            awaitingRender = false;
            retakeFraming = null;
            SceneView.duringSceneGui -= CaptureGUI;
            if (drawing) GUIUtility.hotControl = 0;
            capturing = drawing = false;
            if (captureView != null) captureView.Repaint();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshRetakeButton;
            StopCapture();
            if (capturedIcon != null) DestroyImmediate(capturedIcon);
        }
    }
}
