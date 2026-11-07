using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private static readonly string[] SelectionToolLabels = { "Select...", "Rectangle", "Lasso", "Material", "UV Island" };

        private bool CanConvertRegionToMask(TextureSet set)
        {
            if(set==null || (uint)set.activeLayerIndex>=(uint)set.layers.Count)return false;
            if(!TryResolveLogicalPeers(set,set.layers[set.activeLayerIndex],out var peers,out _))return false;
            bool selected=false;
            foreach(var peer in peers)
            {
                if(peer.textureSet.activeRegion==null)continue;
                if(peer.textureSet.RegionTexture==null || !string.IsNullOrEmpty(peer.textureSet.RegionError))return false;
                selected=true;
            }
            return selected;
        }

        private void InvertRegions()
        {
            var inverted=new Dictionary<TextureSet,TexturePaintRegion>();
            foreach(var set in controller.Textures.Sets)
                if(set.activeRegion?.IsValid==true)
                {
                    _=set.RegionTexture;
                    if(!string.IsNullOrEmpty(set.RegionError)){ShowWorkspaceStatus(set.RegionError);return;}
                    inverted[set]=set.activeRegion.Adjust(0,0,true);
                }
            if(inverted.Count>0)SetRegions(inverted,false);
        }

        internal void DrawSelectionToolbar(bool scene)
        {
            if(!SelectionToolbarVisible)return;
            var set=ActiveTextureSet;
            bool supported=CanEditRegion(set);
            SuspendRegionToolForNonPaintLayer(set);
            using(new EditorGUI.DisabledScope(!SceneToolbarToolsEnabled))
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if(supported)
                {
                    GUILayout.Label(new GUIContent("Selection",RegionRestrictsPainting(set)
                        ? "Restricts new paint. Existing paint is unchanged."
                        : "Defines a region for mask creation. Use Convert Selection to Mask to restrict generated output."),EditorStyles.miniLabel,GUILayout.Width(55));
                    var tool=(RegionTool)EditorGUILayout.Popup((int)regionTool,SelectionToolLabels,
                        EditorStyles.toolbarPopup,GUILayout.Width(85));
                    if(tool!=regionTool)SetRegionTool(tool);
                    var combine=(TexturePaintRegionCombine)EditorGUILayout.EnumPopup(regionCombine,
                        EditorStyles.toolbarPopup,GUILayout.Width(76));
                    if(combine!=regionCombine){regionCombine=combine;RepaintAll();}
                    if(regionTool!=RegionTool.Paint && GUILayout.Button(
                        new GUIContent("Finish Selection","Return to layer editing. Esc also finishes selection."),
                        EditorStyles.toolbarButton))ExitRegionTool();
                    if(scene && (regionTool==RegionTool.Rectangle || regionTool==RegionTool.Lasso))
                        regionThrough=GUILayout.Toggle(regionThrough,new GUIContent("Through","Select through occluded surfaces."),EditorStyles.toolbarButton);
                }
                else GUILayout.Label(new GUIContent("Selection stored; not applied here",
                    "This layer's generated content does not use the painting selection. Convert it to a layer mask to constrain this layer."),EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(new GUIContent("Clear","Clear selections on all texture tiles."),EditorStyles.toolbarButton,GUILayout.Width(42)))ClearAllRegions();
                GUILayout.EndHorizontal();

                // Keep conversion accessible even on Fill, Projection and plugin layers.
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if(supported)
                {
                    using(new EditorGUI.DisabledScope(CountSelectedRegions()==0))
                        if(GUILayout.Button(new GUIContent("Invert","Invert all active selections."),EditorStyles.toolbarButton,GUILayout.Width(45)))InvertRegions();
                    bool visible=GUILayout.Toggle(showSelection,new GUIContent("Show Selection","Show coverage in the Scene and UV views."),EditorStyles.toolbarButton);
                    if(visible!=showSelection){showSelection=visible;RepaintAll();}
                }
                using(new EditorGUI.DisabledScope(!CanConvertRegionToMask(set)))
                    if(GUILayout.Button(new GUIContent("Convert Selection to Mask",
                        "Replace the active layer's masks on selected tiles with selection coverage. Existing masks can be restored with Undo. The selection is retained."),EditorStyles.toolbarButton))RegionToMask(set);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if(supported && !RegionRestrictsPainting(set))
                    GUILayout.Label("For mask creation: generated output is unchanged until converted.",EditorStyles.wordWrappedMiniLabel);
            }
        }
    }

    [Overlay(typeof(SceneView), "Overlay Painter Selection", false, defaultDockZone = DockZone.Floating)]
    public sealed class TexturePaintSelectionToolbarOverlay : Overlay
    {
        private static readonly HashSet<TexturePaintSelectionToolbarOverlay> Instances = new();
        private bool contextWasActive;
        public override VisualElement CreatePanelContent()
        {
            var panel=new IMGUIContainer(()=>TexturePaintStageWindow.ActiveStage?.DrawSelectionToolbar(true));
            panel.style.width=450;
            return panel;
        }
        public override void OnCreated()
        {
            base.OnCreated();Instances.Add(this);
            UpdateContextVisibility();
            EditorApplication.update+=UpdateContextVisibility;
        }
        public override void OnWillBeDestroyed()
        {
            EditorApplication.update-=UpdateContextVisibility;
            Instances.Remove(this);base.OnWillBeDestroyed();
        }
        private void UpdateContextVisibility()
        {
            var stage=TexturePaintStageWindow.ActiveStage;
            bool active=stage?.SelectionToolbarVisible==true && ReferenceEquals(StageUtility.GetCurrentStage(),stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
            if(!TexturePaintSceneOverlayVisibility.NeedsDisplayRefresh(active,contextWasActive,displayed))return;
            contextWasActive=active;displayed=active;
        }
        internal static void RefreshAllInstances(){foreach(var overlay in Instances)overlay.UpdateContextVisibility();}
        internal static void HideAllInstances()
        {foreach(var overlay in Instances){overlay.contextWasActive=false;overlay.displayed=false;}}
    }
}
