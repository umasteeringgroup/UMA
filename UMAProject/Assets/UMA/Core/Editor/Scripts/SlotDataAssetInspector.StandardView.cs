using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class SlotDataAssetInspector
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(SlotDataAsset));

        private void DrawStandardInspector()
        {
            serializedObject.Update();
            using (inspectorView.Section("Slot & organization",
                "Slot Name is the stable name used by recipes and LOD lookup. Slot Group and Tags organize the slot and drive tag-based wardrobe rules. Apply Overlays by Tag allows matching overlays to be selected by tag; Allowed Races limits that wildcard behavior to the listed races. Renaming a published slot can break existing recipes."))
            {
                EditorGUILayout.LabelField("Slot Name", slot.slotName);
                inspectorView.Field(serializedObject, "slotGroup", "Slot Group");
                inspectorView.Field(serializedObject, "tags", "Tags");
                inspectorView.Field(serializedObject, "isWildCardSlot", "Apply Overlays by Tag (Wildcard)");
                if (inspectorView.Property(serializedObject, "isWildCardSlot").boolValue)
                    inspectorView.Field(serializedObject, "Races", "Allowed Races");
            }
            if (!slot.isWildCardSlot)
            {
                using (inspectorView.Section("Rendering & detail",
                    "Renderer Settings supplies per-slot rendering defaults. Last Visible LOD is the final UMASimpleLOD level at which the slot remains present (-1 means all levels). Atlas Resolution Scale changes the texture space requested by this slot. Source UV Cropping controls whether automatic prepared UV bounds may crop unused texture regions, may not crop, or inherit the project policy."))
                {
                    inspectorView.Field(serializedObject, "_rendererAsset", "Renderer Settings");
                    inspectorView.Field(serializedObject, "maxLOD", "Last Visible LOD (-1 = All)");
                    inspectorView.Field(serializedObject, "overlayScale", "Atlas Resolution Scale");
                    inspectorView.Field(serializedObject, "sourceUVCropping", "Source UV Cropping");
                }
                using (inspectorView.Section("Animation",
                    "Bone Animators apply supported procedural bone motion associated with this slot. Keep Bones When Baking preserves listed bones even when normal usage analysis would remove them. The Advanced View contains the less common bone-selection, mesh-editing, and bake controls."))
                {
                    inspectorView.Field(serializedObject, "animatedBones", "Bone Animators");
                    inspectorView.Field(serializedObject, "UnbakedAnimatedBones", "Keep Bones When Baking");
                    EditorGUILayout.LabelField("Bone selection, mesh editing and baking tools are in Advanced View.", EditorStyles.wordWrappedMiniLabel);
                }
            }
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                foreach (Object item in targets)
                {
                    var edited = (SlotDataAsset)item;
                    EditorUtility.SetDirty(edited);
                    UMAUpdateProcessor.UpdateSlot(edited, false);
                }
            }
            using (inspectorView.Section("Validation & painting",
                "Validate Slot checks references and prepared mesh metadata without changing the source model. View Mesh Data opens the extracted UMA mesh information. Open in Overlay Painter uses this slot's UVs for texture or mask painting. UDIM information identifies prepared tile placement when the slot uses tiled UVs."))
            {
                if (GUILayout.Button("Validate Slot"))
                    foreach (Object item in targets) ((SlotDataAsset)item).ValidateMeshData();
                if (!string.IsNullOrEmpty(slot.Errors)) EditorGUILayout.HelpBox(slot.Errors, MessageType.Error);
                using (new EditorGUI.DisabledScope(targets.Length != 1 || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData)))
                {
                    if (GUILayout.Button("View Mesh Data")) MeshDataViewerWindow.Open(slot);
                    if (GUILayout.Button("Open in Overlay Painter"))
                        UMA.TexturePaint.Editor.TexturePaintStandaloneSetupWindow.ShowForSlot(slot);
                }
                if (slot.IsUdimMember)
                    EditorGUILayout.LabelField("UDIM", slot.udimGroupName + " / " + slot.udimTileNumber);
            }
        }
    }
}
