using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private static readonly string[] ProjectionSceneHints =
        {
            "Projection: click model or drag Move to position + align",
            "X / Y: size along the projection plane",
            "Z: depth on either side of the surface",
            "Ring: rotate around the surface normal",
            "Esc: cancel the current handle drag"
        };
        private const string ProjectionGizmoScalePreference = "UMA.OverlayPainter.ProjectionGizmoScale";
        private const float DefaultProjectionGizmoScale = 2f;
        private static float projectionGizmoScale = LoadProjectionGizmoScale();
        private int projectionHandleControl;

        private static float LoadProjectionGizmoScale()
        {
            float value = EditorPrefs.GetFloat(ProjectionGizmoScalePreference, DefaultProjectionGizmoScale);
            return float.IsFinite(value) ? Mathf.Clamp(value, .25f, 5f) : DefaultProjectionGizmoScale;
        }

        private static void DrawProjectionGizmoSize()
        {
            // Handle display is a personal editor preference, not an edit to layer content.
            bool wasChanged = GUI.changed;
            EditorGUI.BeginChangeCheck();
            float scale;
            using (new EditorGUILayout.HorizontalScope())
            {
                scale = EditorGUILayout.Slider(new GUIContent("Gizmo Size (%)",
                    "Size of the projection's scene handles and their hit areas. 100 is the original size; 200 is the default. Does not resize the projection."),
                    projectionGizmoScale * 100f, 25f, 500f) * .01f;
                if (GUILayout.Button(new GUIContent("Reset", "Restore 200% handle size."), GUILayout.Width(48f)))
                { scale = DefaultProjectionGizmoScale; GUI.changed = true; }
            }
            if (EditorGUI.EndChangeCheck())
            {
                projectionGizmoScale = float.IsFinite(scale) ? Mathf.Clamp(scale, .25f, 5f) : DefaultProjectionGizmoScale;
                EditorPrefs.SetFloat(ProjectionGizmoScalePreference, projectionGizmoScale);
                SceneView.RepaintAll();
            }
            GUI.changed = wasChanged;
        }

        private sealed class ProjectionRingDrag
        {
            public int control;
            public Vector3 center, normal, previousDirection;
            public Quaternion rotation;
            public float angle;
        }
        private ProjectionRingDrag projectionRingDrag;
        private bool editProjectionWarp;
        private TexturePaintProjectionGeometry projectionWarpGeometry;
        private TexturePaintProjectionGeometry projectionMoveGeometry;
        private sealed class ProjectionEdit
        {
            public string id;
            public List<LayerLocation> before, after;
            public List<TextureSet> sets;
            public List<TexturePaintLayer> layers;
            public TexturePaintProjectionRenderer renderer;
        }
        private ProjectionEdit pendingProjectionEdit;
        // Consecutive commands share the same boundary snapshot. Releasing a redo branch or
        // an expired command must not destroy a layer still retained by another projection edit.
        private static readonly Dictionary<TexturePaintLayer, int> ProjectionSnapshotReferences =
            new Dictionary<TexturePaintLayer, int>();

        private void AddProjectionLayer(TextureSet set) => AddProjectionLayerCore(set, null);
        private void AddProjectionLayerCore(TextureSet set, TexturePaintGarmentSettings garment)
        {
            if (set == null) return;
            FinishProjectionEdit();
            ExitRegionTool();
            geometryFillMode = 0;
            int previousIndex = set.activeLayerIndex;
            BeginLayerCreationUndo("Add Projection Layer");
            TexturePaintLayer layer = set.AddProjectionLayer();
            layer.projectionSettings.source = paintSource == TexturePaintBrushSource.Overlay
                ? TexturePaintBrushSource.Overlay : TexturePaintBrushSource.Texture;
            layer.projectionSettings.texture = paintSourceSprite == null ? paintSourceTexture : null;
            layer.projectionSettings.sprite = paintSourceSprite;
            layer.projectionSettings.overlay = paintSourceOverlay;
            layer.projectionSettings.channel = selectedChannel;
            layer.projectionSettings.normalConvention = normalConvention;
            layer.projectionSettings.UpgradeChannelSources();
            MatchProjectionAspect(layer.projectionSettings);
            layer.projectionSettings.lockAspect = false;
            if (garment != null)
            {
                layer.projectionSettings.garment = garment.Clone();
                layer.name = ObjectNames.NicifyVariableName(garment.preset.ToString());
                layer.projectionSettings.width = garment.kind == TexturePaintGarmentKind.Hardware ? .03f : .16f;
                layer.projectionSettings.height = garment.kind == TexturePaintGarmentKind.Labels ? .08f : .2f;
                layer.projectionSettings.fade = TexturePaintProjectionFade.Rectangle;
                layer.projectionSettings.edgeWidth = .02f;
            }
            var target = controller?.LogicalLayers?.FindTarget(set);
            var targets = target != null ? controller.LogicalLayers.GetTextureSets(target) : new List<TextureSet> { set };
            TextureSet hitSet = hasHover && hoverSurface != null ? controller?.Textures?.FindSet(hoverSurface.index) : null;
            Vector3 point = hoverHit.point, normal = hoverHit.normal; int triangle = hoverHit.triangleIndex;
            if (hitSet == null || !targets.Contains(hitSet) || normal.sqrMagnitude < .5f)
            {
                var placement = new TexturePaintProjectionGeometry(targets);
                placement.InitialPlacement(SceneView.lastActiveSceneView?.camera, out hitSet, out triangle, out point, out normal);
            }
            if (hitSet != null)
            {
                layer.projectionSettings.PlaceOnSurface(point, normal, hitSet.persistentId, triangle);
                using var renderer = new TexturePaintProjectionRenderer();
                if (!renderer.Generate(controller?.Textures?.Sets ?? targets, new[] { set }, new[] { layer }, layer.projectionSettings, out string error))
                {
                    set.layers.Remove(layer); layer.Dispose(); set.activeLayerIndex = previousIndex;
                    pendingLayerCreationLabel = null; ShowWorkspaceStatus(error); return;
                }
            }
            CompleteLayerCreationUndo(layer);
            if (!set.layers.Contains(layer)) return;
            SyncActiveLayerSelection(set);
            ShowWorkspaceStatus(hitSet != null ? "Click the model or drag Move to reposition. Drag X/Y for size, Z for depth, or the ring to rotate."
                : "No surface was available. Choose a source and click the model to place the projection.");
            RepaintAll();
        }

        private static void MatchProjectionAspect(TexturePaintProjectionSettings settings)
        {
            var source = settings.PreferredSource();
            Texture texture = settings.ResolveCoverage();
            float aspect = source?.source == TexturePaintBrushSource.Texture && source.sprite != null &&
                settings.source != TexturePaintBrushSource.Overlay
                ? source.sprite.rect.width / Mathf.Max(1, source.sprite.rect.height)
                : texture != null ? (float)texture.width / Mathf.Max(1, texture.height) : 1f;
            settings.height = settings.width / Mathf.Max(0.0001f, aspect);
        }

        private void DrawProjectionProperties(TextureSet set, TexturePaintLayer layer)
        {
            layer.NormalizeKindPayload();
            TexturePaintProjectionSettings settings = layer.projectionSettings.Clone();
            DrawProjectionGizmoSize();
            EditorGUILayout.HelpBox("Click the model or drag the center Move handle to position and align this projection to the surface normal. Drag X/Y to resize along the surface, Z for depth on either side of the surface, and the ring to rotate around the normal. Fit to Surface wraps the patch explicitly; placement stays fixed in world space.", MessageType.None);
            if (settings.garment?.enabled != true) EditorGUILayout.LabelField("Assign textures, sprites, overlays, or colors in Layer Channels & Settings below. Albedo alpha supplies the shared silhouette; without albedo, the first assigned map supplies it.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            settings.garment ??= new TexturePaintGarmentSettings();
            using (new EditorGUI.DisabledScope(layer.links?.instance?.IsSet == true))
                DrawGarmentSettings(set, layer, settings.garment, false);
            EditorGUILayout.LabelField("Texture Mirroring (All Channels)", EditorStyles.boldLabel);
            settings.flipX = EditorGUILayout.Toggle(new GUIContent("Flip X (Left/Right)",
                "Mirror the projected images left to right along the projection's local X axis. All channel images, shared source alpha, and normal-map directions flip together."), settings.flipX);
            settings.flipY = EditorGUILayout.Toggle(new GUIContent("Flip Y (Up/Down)",
                "Mirror the projected images top to bottom along the projection's local Y axis. All channel images, shared source alpha, and normal-map directions flip together."), settings.flipY);
            EditorGUILayout.LabelField("Use Flip X to turn a right-hand pocket image into a left-hand pocket. Every channel stays aligned.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
            settings.mode = (TexturePaintProjectionMode)EditorGUILayout.EnumPopup("Projection", settings.mode);
            settings.lockAspect = EditorGUILayout.Toggle(new GUIContent("Lock Numeric Aspect",
                "Keep the ratio when editing Width/Height below. Scene X/Y handles always resize their own axis."), settings.lockAspect);
            if (settings.mode == TexturePaintProjectionMode.Cylindrical)
            {
                settings.cylinderAngle = EditorGUILayout.Slider("Cylinder Arc", settings.cylinderAngle, 10f, 360f);
                EditorGUILayout.HelpBox("Width is arc length. The cylinder axis runs along Y; its front touches the clicked surface. Set width to the limb circumference for a full wrap.", MessageType.None);
            }
            float width = EditorGUILayout.FloatField(settings.mode == TexturePaintProjectionMode.Cylindrical ? "Arc Length (X)" : "Width (X)", settings.width);
            float height = EditorGUILayout.FloatField("Height (Y)", settings.height);
            if (settings.lockAspect)
            {
                if (width != settings.width) height = settings.height * Mathf.Max(0.0001f, width) / settings.width;
                else if (height != settings.height) width = settings.width * Mathf.Max(0.0001f, height) / settings.height;
            }
            settings.width = width; settings.height = height;
            settings.depth = EditorGUILayout.FloatField(new GUIContent("Depth (Z)", "Maximum projection distance in front of and behind the placement surface. Nearby raised polygons are included; geometry outside the volume is clipped."), settings.depth);
            using(new EditorGUI.DisabledScope(layer.links?.instance?.IsSet == true))
            {
            settings.fade = (TexturePaintProjectionFade)EditorGUILayout.EnumPopup("Edge Fade", settings.fade);
            if (settings.fade != TexturePaintProjectionFade.None)
                settings.edgeWidth = EditorGUILayout.Slider("Fade Width", settings.edgeWidth, 0f, 0.5f);
            settings.falloff = EditorGUILayout.CurveField("Fade Curve", settings.falloff);
            settings.depthFade = EditorGUILayout.Slider("Depth Fade", settings.depthFade, 0f, 1f);
            settings.frontFacesOnly = EditorGUILayout.Toggle("Front Faces Only", settings.frontFacesOnly);
            settings.angleStart = EditorGUILayout.Slider("Angle Fade Starts", settings.angleStart, 0f, 90f);
            settings.angleEnd = EditorGUILayout.Slider("Angle Fade Ends", settings.angleEnd, settings.angleStart, 90f);
            settings.firstSurfaceOnly = !EditorGUILayout.Toggle(new GUIContent("Project Through", "Include hidden surfaces within the depth volume."), !settings.firstSurfaceOnly);
            settings.connectedSurfaceOnly = EditorGUILayout.Toggle("Connected Surface Only", settings.connectedSurfaceOnly);
            if (settings.firstSurfaceOnly)
            {
                settings.visibilityResolution = EditorGUILayout.IntPopup("Visibility Quality", settings.visibilityResolution,
                    new[] { "Low (64)", "Medium (128)", "High (256)", "Very High (512)" }, new[] { 64, 128, 256, 512 });
                settings.visibilityTolerance = EditorGUILayout.FloatField(new GUIContent("Surface Tolerance", "World-space tolerance for first-surface visibility."), settings.visibilityTolerance);
            }
            }
            if (EditorGUI.EndChangeCheck()) ChangeProjectionWithHistory(set, layer, settings, GUIUtility.hotControl != 0);
            if (settings.garment?.enabled != true && GUILayout.Button(new GUIContent("Match Source Aspect", "Use the albedo image aspect, or the first assigned map when no albedo is assigned.")))
            { MatchProjectionAspect(settings); ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings, false); }
            using (new EditorGUI.DisabledScope(!settings.placed))
            {
                if (settings.mode != TexturePaintProjectionMode.Cylindrical && GUILayout.Button("Fit to Surface / Refit"))
                {
                    int hits = new TexturePaintProjectionGeometry(controller.Textures.Sets).Fit(settings);
                    if (hits > 0) ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings, false);
                    ShowWorkspaceStatus($"Fitted or retained {hits}/{settings.points.Length} points. Pinned points stay fixed.");
                }
                if (settings.mode == TexturePaintProjectionMode.Planar && GUILayout.Button("Edit Warp"))
                {
                    settings.mode = TexturePaintProjectionMode.Wrapped;
                    new TexturePaintProjectionGeometry(controller.Textures.Sets).Fit(settings);
                    editProjectionWarp = true;
                    ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings);
                }
                if (settings.mode == TexturePaintProjectionMode.Wrapped)
                {
                    editProjectionWarp = GUILayout.Toggle(editProjectionWarp, "Edit Warp", "Button");
                    if (editProjectionWarp)
                    {
                        EditorGUILayout.HelpBox("Drag a point along the model. Shift-click a point to pin/unpin it (orange). Refit and Reset retain pins. Esc cancels a drag. Turn Edit Warp off to reposition or resize the whole projection.", MessageType.None);
                        EditorGUILayout.LabelField("Reducing the grid removes intermediate points and pins. Undo restores them.", EditorStyles.wordWrappedMiniLabel);
                        int size = EditorGUILayout.IntPopup("Control Grid", settings.gridSize,
                            new[] { "3 x 3", "5 x 5", "9 x 9" }, new[] { 3, 5, 9 });
                        if (size != settings.gridSize)
                        { settings.ResizeGrid(size); ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings); }
                        if (GUILayout.Button("Unpin All"))
                        { settings.pinned = new bool[settings.points.Length]; ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings); }
                    }
                    if (GUILayout.Button("Reset Unpinned Points"))
                    {
                        var reset = TexturePaintProjectionSettings.DefaultPoints(settings.gridSize);
                        for (int i = 0; i < reset.Length; i++) if (!settings.pinned[i]) settings.points[i] = reset[i];
                        ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings, false);
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!ResolveLayerSymmetry(layer).enabled || !settings.placed))
                if (GUILayout.Button("Create Symmetry Instances")) CreateProjectionSymmetry(set,layer);
            if (GUILayout.Button("Regenerate Projection"))
                ChangeProjectionWithHistory(set, CurrentProjection(set, layer.id), settings, false);
        }

        // Keep old overlay-wide definitions intact until the artist first edits their channels.
        // The migrated maps then use the same source controls as Paint and Fill layers.
        private static TexturePaintProjectionSettings EditableProjectionChannels(TexturePaintLayer layer)
        {
            var settings = layer.projectionSettings.Clone();
            if (settings.source == TexturePaintBrushSource.Overlay)
            {
                settings.channelSources.Clear();
                foreach (var channel in layer.channels.Keys)
                    settings.channelSources.Add(new TexturePaintProjectionChannelSource
                    { channel = channel, source = TexturePaintBrushSource.Overlay, overlay = settings.overlay,
                        normalConvention = settings.normalConvention });
                settings.channelSourcesVersion = 1;
                settings.source = TexturePaintBrushSource.Texture;
            }
            else settings.UpgradeChannelSources();
            return settings;
        }

        private bool ChangeProjectionChannelSources(TextureSet set, TexturePaintLayer layer,
            IReadOnlyDictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> sources)
        {
            if (layer.links?.instance?.IsSet == true) return false;
            if (!TryResolveLogicalPeers(set, layer, out var peers, out string error))
            { ShowWorkspaceStatus(error); return false; }
            foreach (var peer in peers)
                foreach (var pair in sources)
                    if (!peer.layer.channels.ContainsKey(pair.Key))
                    { ShowWorkspaceStatus($"{pair.Key} was not added to every logical layer member."); return false; }
            var settings = EditableProjectionChannels(layer);
            foreach (var pair in sources)
                settings.GetChannelSource(pair.Key, true).ApplyChannelSettings(
                    pair.Value ?? new TexturePaintChannelSourceSettings());
            return ChangeProjectionWithHistory(set, layer, settings, GUIUtility.hotControl != 0);
        }

        private bool RemoveProjectionChannelWithHistory(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel)
        {
            if (layer.links?.instance?.IsSet == true) return false;
            if (!TryResolveLogicalPeers(set, layer, out var peers, out string error))
            { ShowWorkspaceStatus(error); return false; }
            foreach (var peer in peers)
                if (!peer.layer.channels.ContainsKey(channel))
                { ShowWorkspaceStatus($"{channel} is not present on every logical layer member."); return false; }
            var settings = EditableProjectionChannels(layer);
            settings.channelSources.RemoveAll(item => item.channel == channel);
            return ChangeProjectionWithHistory(set, layer, settings);
        }

        private static TexturePaintLayer CurrentProjection(TextureSet set, string id)
        {
            if (set != null)
                foreach (var layer in set.layers) if (layer.id == id) return layer;
            return null;
        }

        private bool ChangeProjectionWithHistory(TextureSet set, TexturePaintLayer layer,
            TexturePaintProjectionSettings settings, bool gesture = false)
        {
            if (layer?.kind != TexturePaintLayerKind.Projection) return false;
            if (pendingProjectionEdit != null && (!gesture || pendingProjectionEdit.id != layer.id)) FinishProjectionEdit();
            if (pendingProjectionEdit != null)
            {
                ProjectionEdit edit = pendingProjectionEdit;
                var previous = edit.layers[0].projectionSettings.Clone();
                if (!edit.renderer.Generate(controller?.Textures?.Sets ?? edit.sets, edit.sets, edit.layers, settings, out string error))
                {
                    // Keep the last valid drag result if generation fails. If it cannot be
                    // regenerated, cancel the whole gesture back to its untouched snapshot.
                    if (!edit.renderer.Generate(controller?.Textures?.Sets ?? edit.sets, edit.sets, edit.layers, previous, out _))
                        CancelProjectionEdit();
                    ShowWorkspaceStatus(error); return false;
                }
                foreach (var target in edit.sets) target.BindPreviewTextures();
                MarkDocumentDirty(); RepaintAll(); return true;
            }
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string resolveError))
            { ShowWorkspaceStatus(resolveError); return false; }
            var before = new List<LayerLocation>(); var after = new List<LayerLocation>();
            var sets = new List<TextureSet>(); var layers = new List<TexturePaintLayer>();
            foreach (TexturePaintLogicalLayerMember peer in peers)
            {
                int index = peer.textureSet.layers.IndexOf(peer.layer);
                TexturePaintLayer copy = peer.textureSet.CloneLayer(peer.layer, peer.layer.name, true, false);
                before.Add(new LayerLocation { set = peer.textureSet, layer = peer.layer, index = index });
                after.Add(new LayerLocation { set = peer.textureSet, layer = copy, index = index });
                sets.Add(peer.textureSet); layers.Add(copy);
            }
            var renderer = new TexturePaintProjectionRenderer(stableGeometry: controller?.Textures?.Sets ?? sets);
            if (!renderer.Generate(controller?.Textures?.Sets ?? sets, sets, layers, settings, out string generationError))
            {
                renderer.Dispose();
                foreach (TexturePaintLayer copy in layers) copy?.Dispose();
                ShowWorkspaceStatus(generationError); return false;
            }
            // Retain one immutable before snapshot per gesture. The working after snapshot
            // owns reusable channel targets; history never sees intermediate drag snapshots.
            RetainProjectionSnapshots(before);
            RetainProjectionSnapshots(after);
            pendingProjectionEdit = new ProjectionEdit
                { id = layer.id, before = before, after = after, sets = sets, layers = layers, renderer = renderer };
            for (int i = 0; i < before.Count; i++) SwapLayerSnapshot(before[i].set, before[i].layer, after[i].layer, before[i].index);
            if (!gesture) FinishProjectionEdit();
            MarkDocumentDirty(); RepaintAll(); return true;
        }

        private void FinishProjectionEdit()
        {
            ProjectionEdit edit = pendingProjectionEdit;
            if (edit == null) return;
            pendingProjectionEdit = null;
            edit.renderer?.Dispose(); edit.renderer = null;
            PushLightweightCommand("Edit Projection",
                () => { for (int i = 0; i < edit.before.Count; i++) SwapLayerSnapshot(edit.before[i].set, edit.after[i].layer, edit.before[i].layer, edit.before[i].index); },
                () => { for (int i = 0; i < edit.before.Count; i++) SwapLayerSnapshot(edit.after[i].set, edit.before[i].layer, edit.after[i].layer, edit.after[i].index); },
                () => DisposeProjectionEdit(edit));
        }
        private static void RetainProjectionSnapshots(List<LayerLocation> snapshots)
        {
            foreach (var item in snapshots)
            {
                ProjectionSnapshotReferences.TryGetValue(item.layer, out int count);
                ProjectionSnapshotReferences[item.layer] = count + 1;
            }
        }
        private static void ReleaseProjectionSnapshots(List<LayerLocation> snapshots)
        {
            foreach (var item in snapshots)
            {
                if (ProjectionSnapshotReferences.TryGetValue(item.layer, out int count) && count > 1)
                    ProjectionSnapshotReferences[item.layer] = count - 1;
                else ProjectionSnapshotReferences.Remove(item.layer);
                DisposeLayerIfDetached(item.set, item.layer);
            }
        }
        private static void DisposeProjectionEdit(ProjectionEdit edit)
        {
            edit.renderer?.Dispose(); edit.renderer = null;
            ReleaseProjectionSnapshots(edit.before);
            ReleaseProjectionSnapshots(edit.after);
        }
        private void CancelProjectionEdit()
        {
            ProjectionEdit edit = pendingProjectionEdit;
            if (edit == null) return;
            pendingProjectionEdit = null;
            for (int i = 0; i < edit.before.Count; i++) SwapLayerSnapshot(edit.before[i].set, edit.after[i].layer, edit.before[i].layer, edit.before[i].index);
            DisposeProjectionEdit(edit); MarkDocumentDirty(); RepaintAll();
        }

        private void ReleaseProjectionHandle()
        {
            if (projectionHandleControl != 0 && GUIUtility.hotControl == projectionHandleControl) GUIUtility.hotControl = 0;
            projectionHandleControl = 0;
            projectionRingDrag = null;
            projectionWarpGeometry = null;
            projectionMoveGeometry = null;
        }
        private bool TryHandleProjectionShortcut(Event current)
        {
            TextureSet set = ActiveTextureSet;
            if (set == null || IsLayerMaskMode(set) || (uint)set.activeLayerIndex >= (uint)set.layers.Count ||
                set.layers[set.activeLayerIndex].kind != TexturePaintLayerKind.Projection || current.alt || current.control || current.command || Tools.viewToolActive) return false;
            if (current.keyCode == KeyCode.Escape) { CancelProjectionEdit(); ReleaseProjectionHandle(); }
            else if (current.keyCode == KeyCode.W || current.keyCode == KeyCode.E || current.keyCode == KeyCode.R)
                ShowWorkspaceStatus("Click the model or drag Move to position and align. Drag X/Y/Z to resize, or the ring to rotate.");
            else return false;
            current.Use(); RepaintAll(); return true;
        }
        private bool PlaceProjectionAtSurface(TextureSet set, TexturePaintLayer layer, TextureSet hitSet,
            Vector3 point, Vector3 normal, int triangle)
        {
            if (!TryResolveLogicalPeers(set, layer, out var peers, out string error))
            { ShowWorkspaceStatus(error); return false; }
            if (hitSet == null || !peers.Exists(peer => ReferenceEquals(peer.textureSet, hitSet)))
            { ShowWorkspaceStatus("Click a surface belonging to this layer's paint target."); return false; }
            var settings = layer.projectionSettings.Clone();
            settings.PlaceOnSurface(point, normal, hitSet.persistentId, triangle);
            if (settings.mode == TexturePaintProjectionMode.Wrapped)
                new TexturePaintProjectionGeometry(controller?.Textures?.Sets ?? new[] { set }).Fit(settings);
            return ChangeProjectionWithHistory(set, layer, settings);
        }
        private bool HandleProjectionScene(SceneView sceneView, Event current, bool targetHover)
        {
            TextureSet set = ActiveTextureSet;
            TexturePaintLayer layer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count ? set.layers[set.activeLayerIndex] : null;
            if (layer?.kind != TexturePaintLayerKind.Projection || IsLayerMaskMode(set))
            { FinishProjectionEdit(); ReleaseProjectionHandle(); return false; }
            layer.NormalizeKindPayload();
            var settings = layer.projectionSettings.Clone();
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            { CancelProjectionEdit(); ReleaseProjectionHandle(); current.Use(); return true; }
            if (current.alt || current.button == 1 || current.button == 2 || Tools.viewToolActive)
            { FinishProjectionEdit(); ReleaseProjectionHandle(); return true; }
            if (current.type == EventType.Ignore || current.type == EventType.MouseLeaveWindow)
            { FinishProjectionEdit(); ReleaseProjectionHandle(); return true; }
            int placementControl = GUIUtility.GetControlID("OverlayPainterProjectionPlacement".GetHashCode(), FocusType.Passive);
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(placementControl);
            bool mouseUp = current.rawType == EventType.MouseUp;
            int previousControl = GUIUtility.hotControl;
            if (settings.placed)
            {
                using (new Handles.DrawingScope(Color.cyan))
                {
                    DrawProjectionFrame(settings);
                    EditorGUI.BeginChangeCheck();
                    if (editProjectionWarp && settings.mode == TexturePaintProjectionMode.Wrapped) DrawProjectionWarpHandles(settings, set, layer);
                    else DrawProjectionSurfaceHandles(settings, set, layer);
                    if (EditorGUI.EndChangeCheck()) ChangeProjectionWithHistory(set, layer, settings, true);
                }
            }
            if (previousControl == 0 && GUIUtility.hotControl != 0 && current.rawType == EventType.MouseDown)
                projectionHandleControl = GUIUtility.hotControl;
            // Handles get first refusal. Clicking elsewhere on the model always repositions.
            if (current.type == EventType.MouseDown && current.button == 0 && GUIUtility.hotControl == 0 &&
                HandleUtility.nearestControl == placementControl && targetHover &&
                !(editProjectionWarp && settings.mode == TexturePaintProjectionMode.Wrapped))
            {
                FinishProjectionEdit();
                PlaceProjectionAtSurface(set, CurrentProjection(set, layer.id), controller.Textures.FindSet(hoverSurface.index),
                    hoverHit.point, hoverHit.normal, hoverHit.triangleIndex);
                current.Use(); sceneView.Repaint();
            }
            if (mouseUp || GUIUtility.hotControl == 0)
            { FinishProjectionEdit(); ReleaseProjectionHandle(); }
            return true;
        }
        private void DrawProjectionWarpHandles(TexturePaintProjectionSettings settings, TextureSet set, TexturePaintLayer layer)
        {
            Event current = Event.current;
            for (int i = 0; i < settings.points.Length; i++)
            {
                int control = GUIUtility.GetControlID(734100 + i, FocusType.Passive);
                Vector3 point = settings.PointToWorld(settings.points[i]);
                float size = HandleUtility.GetHandleSize(point) * .025f * projectionGizmoScale;
                switch (current.GetTypeForControl(control))
                {
                    case EventType.Layout:
                        HandleUtility.AddControl(control, HandleUtility.DistanceToCircle(point, size)); break;
                    case EventType.MouseDown:
                        if (current.button != 0 || GUIUtility.hotControl != 0 || HandleUtility.nearestControl != control) break;
                        if (current.shift)
                        { settings.pinned[i] = !settings.pinned[i]; GUI.changed = true; }
                        else if (!settings.pinned[i])
                        {
                            if (!TryResolveLogicalPeers(set, layer, out var peers, out _)) break;
                            var targets = new List<TextureSet>(); foreach (var peer in peers) targets.Add(peer.textureSet);
                            projectionWarpGeometry = new TexturePaintProjectionGeometry(targets);
                            GUIUtility.hotControl = projectionHandleControl = control;
                        }
                        current.Use(); break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl != control) break;
                        if (projectionWarpGeometry != null && projectionWarpGeometry.PickSurface(
                            HandleUtility.GUIPointToWorldRay(current.mousePosition), out _, out _, out Vector3 hit, out _))
                        { settings.points[i] = settings.WorldToPoint(hit); GUI.changed = true; }
                        current.Use(); break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl != control || current.button != 0) break;
                        GUIUtility.hotControl = 0; projectionWarpGeometry = null; current.Use(); break;
                    case EventType.Repaint:
                        using (new Handles.DrawingScope(settings.pinned[i] ? new Color(1f, .55f, .1f) :
                            GUIUtility.hotControl == control ? Handles.selectedColor : Color.cyan))
                            Handles.SphereHandleCap(control, point, Quaternion.identity, size * 2f, EventType.Repaint);
                        break;
                }
            }
        }

        private static void ResizeProjection(TexturePaintProjectionSettings settings, float width, float height, float depth)
        {
            settings.width = Mathf.Max(.0001f, width); settings.height = Mathf.Max(.0001f, height);
            settings.depth = Mathf.Max(.0001f, depth);
        }
        private void DrawProjectionSurfaceHandles(TexturePaintProjectionSettings settings, TextureSet set, TexturePaintLayer layer)
        {
            Vector3 right = settings.rotation * Vector3.right, up = settings.rotation * Vector3.up, normal = settings.rotation * Vector3.forward;
            float size = HandleUtility.GetHandleSize(settings.position) * .055f * projectionGizmoScale;
            Vector3 widthPoint = settings.position + right * settings.width * .5f;
            Vector3 heightPoint = settings.position + up * settings.height * .5f;
            Vector3 depthPoint = settings.position + normal * settings.BackDepth;
            float width = settings.width, height = settings.height, depth = settings.depth;
            using (new Handles.DrawingScope(Handles.xAxisColor))
            {
                Handles.DrawLine(settings.position, widthPoint);
                EditorGUI.BeginChangeCheck();
                Vector3 w = Handles.Slider(widthPoint, right, size, Handles.CubeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck()) width = Vector3.Dot(w - settings.position, right) * 2f;
                if (Event.current.type == EventType.Repaint) Handles.Label(widthPoint + right * size, "X");
            }
            using (new Handles.DrawingScope(Handles.yAxisColor))
            {
                Handles.DrawLine(settings.position, heightPoint);
                EditorGUI.BeginChangeCheck();
                Vector3 h = Handles.Slider(heightPoint, up, size, Handles.CubeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck()) height = Vector3.Dot(h - settings.position, up) * 2f;
                if (Event.current.type == EventType.Repaint) Handles.Label(heightPoint + up * size, "Y");
            }
            using (new Handles.DrawingScope(Handles.zAxisColor))
            {
                Handles.DrawLine(settings.position, depthPoint);
                EditorGUI.BeginChangeCheck();
                Vector3 d = Handles.Slider(depthPoint, -normal, size, Handles.ConeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck()) depth = Vector3.Dot(d - settings.position, -normal) * (settings.depthFromSurface ? 1f : 2f);
                if (Event.current.type == EventType.Repaint) Handles.Label(depthPoint - normal * size, "Z Depth");
            }
            ResizeProjection(settings, width, height, depth);
            float ringRadius = Mathf.Sqrt(settings.width * settings.width + settings.height * settings.height) * .5f + size * 2f;
            DrawProjectionRotationRing(settings, normal, ringRadius);
            DrawProjectionMoveHandle(settings, set, layer);
        }

        private void DrawProjectionMoveHandle(TexturePaintProjectionSettings settings, TextureSet set, TexturePaintLayer layer)
        {
            int control = GUIUtility.GetControlID("OverlayPainterProjectionMove".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            float radius = HandleUtility.GetHandleSize(settings.position) * .035f * projectionGizmoScale;
            switch (current.GetTypeForControl(control))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(control, HandleUtility.DistanceToCircle(settings.position, radius));
                    break;
                case EventType.MouseDown:
                    if (current.button != 0 || GUIUtility.hotControl != 0 || HandleUtility.nearestControl != control) break;
                    if (!TryResolveLogicalPeers(set, layer, out var peers, out string error))
                    { ShowWorkspaceStatus(error); break; }
                    var targets = new List<TextureSet>();
                    foreach (var peer in peers) targets.Add(peer.textureSet);
                    // Restrict picking to this logical target, including every UDIM member. Keep
                    // its ray hierarchy for the entire drag, including wrapped-patch refits.
                    projectionMoveGeometry = new TexturePaintProjectionGeometry(targets);
                    GUIUtility.hotControl = projectionHandleControl = control;
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control) break;
                    if (projectionMoveGeometry != null && projectionMoveGeometry.PickSurface(
                        HandleUtility.GUIPointToWorldRay(current.mousePosition), out var hitSet,
                        out int triangle, out Vector3 point, out Vector3 normal))
                    {
                        // Use the same placement semantics as a model click: transport spin,
                        // preserve dimensions, reset pins and refit a Wrapped projection.
                        settings.PlaceOnSurface(point, normal, hitSet.persistentId, triangle);
                        if (settings.mode == TexturePaintProjectionMode.Wrapped) projectionMoveGeometry.Fit(settings);
                        GUI.changed = true;
                    }
                    // A miss leaves the last valid placement intact and keeps mouse capture
                    // so dragging back onto the model continues the same undoable gesture.
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (current.button != 0 || GUIUtility.hotControl != control) break;
                    GUIUtility.hotControl = 0;
                    projectionMoveGeometry = null;
                    current.Use();
                    break;
                case EventType.Repaint:
                    using (new Handles.DrawingScope(GUIUtility.hotControl == control ? Handles.selectedColor :
                        HandleUtility.nearestControl == control ? Handles.preselectionColor : Color.white))
                    {
                        Handles.SphereHandleCap(control, settings.position, Quaternion.identity, radius * 2f, EventType.Repaint);
                        Handles.Label(settings.position + settings.rotation * new Vector3(radius * 1.5f, -radius * 1.5f, 0), "Move");
                    }
                    break;
            }
        }

        private static bool TryProjectionRingDirection(Vector2 mouse, Vector3 center, Vector3 normal,
            out Vector3 direction)
        {
            direction = Vector3.zero;
            // Angle is undefined at the center or when the ring is viewed exactly edge-on.
            // Ignore those samples instead of introducing a sudden spin or invalid quaternion.
            if ((mouse - HandleUtility.WorldToGUIPoint(center)).sqrMagnitude < 16f) return false;
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (Mathf.Abs(Vector3.Dot(ray.direction, normal)) < .0001f ||
                !new Plane(normal, center).Raycast(ray, out float distance)) return false;
            Vector3 radial = Vector3.ProjectOnPlane(ray.GetPoint(distance) - center, normal);
            if (radial.sqrMagnitude < 1e-12f) return false;
            direction = radial.normalized;
            return true;
        }

        private void DrawProjectionRotationRing(TexturePaintProjectionSettings settings, Vector3 normal, float radius)
        {
            int control = GUIUtility.GetControlID("OverlayPainterProjectionRotation".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            switch (current.GetTypeForControl(control))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(control, HandleUtility.DistanceToDisc(settings.position, normal, radius) / projectionGizmoScale);
                    break;
                case EventType.MouseDown:
                    if (current.button != 0 || GUIUtility.hotControl != 0 || HandleUtility.nearestControl != control ||
                        !TryProjectionRingDirection(current.mousePosition, settings.position, normal, out Vector3 start)) break;
                    projectionRingDrag = new ProjectionRingDrag
                    {
                        control = control, center = settings.position, normal = normal,
                        previousDirection = start, rotation = settings.rotation
                    };
                    GUIUtility.hotControl = control;
                    projectionHandleControl = control;
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control || projectionRingDrag?.control != control) break;
                    var drag = projectionRingDrag;
                    if (TryProjectionRingDirection(current.mousePosition, drag.center, drag.normal, out Vector3 next))
                    {
                        // Accumulate signed steps so crossing the +/-180 degree boundary and
                        // tracing multiple revolutions stay continuous. The initial grab never snaps.
                        // atan2 stays precise for tiny drags and purely radial movement;
                        // acos-based angles amplify rounding noise near zero degrees.
                        float delta = Mathf.Atan2(Vector3.Dot(drag.normal, Vector3.Cross(drag.previousDirection, next)),
                            Vector3.Dot(drag.previousDirection, next)) * Mathf.Rad2Deg;
                        drag.previousDirection = next;
                        if (Mathf.Abs(delta) > .00001f)
                        {
                            drag.angle += delta;
                            settings.rotation = Quaternion.AngleAxis(drag.angle, drag.normal) * drag.rotation;
                            GUI.changed = true;
                        }
                    }
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (current.button != 0 || GUIUtility.hotControl != control) break;
                    GUIUtility.hotControl = 0;
                    projectionRingDrag = null;
                    current.Use();
                    break;
                case EventType.Repaint:
                    bool active = GUIUtility.hotControl == control;
                    using (new Handles.DrawingScope(active ? Handles.selectedColor :
                        HandleUtility.nearestControl == control ? Handles.preselectionColor : Color.yellow))
                    {
                        Handles.DrawWireDisc(settings.position, normal, radius, projectionGizmoScale);
                        if (active && projectionRingDrag != null)
                            Handles.DrawLine(settings.position, settings.position + projectionRingDrag.previousDirection * radius);
                    }
                    break;
            }
        }

        private static void DrawProjectionFrame(TexturePaintProjectionSettings settings)
        {
            int grid = settings.mode == TexturePaintProjectionMode.Wrapped ? settings.gridSize : 3;
            for (int line = 0; line < grid; line++)
            {
                var u = new Vector3[17]; var v = new Vector3[17];
                for (int i = 0; i <= 16; i++) { u[i] = settings.Evaluate(i / 16f, (float)line / (grid - 1)); v[i] = settings.Evaluate((float)line / (grid - 1), i / 16f); }
                Handles.DrawAAPolyLine(u); Handles.DrawAAPolyLine(v);
            }
            // Front and back outlines make the complete depth volume visible, including a curved patch.
            for (int edge = 0; edge < 4; edge++)
            {
                var front = new Vector3[17]; var back = new Vector3[17];
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f, u = edge < 2 ? t : edge - 2, v = edge < 2 ? edge : t;
                    Vector3 p = settings.Evaluate(u, v), n = settings.Normal(u, v);
                    front[i] = p + n * settings.FrontDepth; back[i] = p + n * settings.BackDepth;
                }
                Handles.DrawAAPolyLine(front); Handles.DrawAAPolyLine(back);
            }
            foreach (Vector2 corner in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
            {
                Vector3 p = settings.Evaluate(corner.x, corner.y), n = settings.Normal(corner.x, corner.y);
                Handles.DrawDottedLine(p + n * settings.BackDepth, p + n * settings.FrontDepth, 3f);
            }
            Vector3 normal = settings.rotation * Vector3.forward;
            Handles.ArrowHandleCap(0, settings.position + normal * settings.FrontDepth,
                Quaternion.LookRotation(-normal), settings.depth, EventType.Repaint);
        }
    }
}
