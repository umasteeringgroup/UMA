using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class OverlayDataAssetInspector
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(OverlayDataAsset));
        private bool showStandardBlending;

        private void DrawStandardInspector()
        {
            serializedObject.Update();
            var overlay = (OverlayDataAsset)target;
            using (inspectorView.Section("Overlay & organization",
                "Overlay Name is the stable name recipes use to find this asset. Overlay Group and Tags organize it for authoring and searches. Overlay Type selects the layer's role and how it participates in compositing. Renaming a published overlay can break recipes that refer to the old name."))
            {
                EditorGUILayout.LabelField("Overlay Name", overlay.overlayName);
                inspectorView.Field(serializedObject, "overlayGroup", "Overlay Group");
                inspectorView.Field(serializedObject, "tags", "Tags");
                inspectorView.Field(serializedObject, "overlayType", "Overlay Type");
            }
            using (inspectorView.Section("Material & textures",
                "UMA Material defines the shader and its texture channels. The texture fields supply those channels in material order; Base Map is normally the visible color texture. Optional Alpha Mask controls transparency separately when supported. Texture blending chooses how this layer combines with layers below it. Repair controls appear when the stored channel list no longer matches the material."))
            {
                inspectorView.Field(serializedObject, "material", "UMA Material");
                var materialProperty = inspectorView.Property(serializedObject, "material");
                var material = materialProperty.objectReferenceValue as UMAMaterial;
                var textures = inspectorView.Property(serializedObject, "_textureList");
                if (materialProperty.hasMultipleDifferentValues)
                    EditorGUILayout.HelpBox("Select overlays with the same UMA Material to edit named texture channels. Advanced View exposes the raw channels.", MessageType.Info);
                else if (material == null)
                    EditorGUILayout.HelpBox("Assign a UMA Material to define the texture channels.", MessageType.Info);
                else if (textures != null)
                {
                    int count = material.channels == null ? 0 : material.channels.Length;
                    if (textures.arraySize != count)
                    {
                        EditorGUILayout.HelpBox("Texture channel count differs from the material. Advanced View provides channel insertion, removal and texture reload tools.", MessageType.Warning);
                    }
                    for (int i = 0; i < textures.arraySize; i++)
                    {
                        string channelName = i < count ? material.channels[i].materialPropertyName : "Texture " + i;
                        EditorGUILayout.PropertyField(textures.GetArrayElementAtIndex(i), UMAInspectorView.Label(channelName));
                    }
                    if (textures.arraySize < count && GUILayout.Button("Add Missing Material Channels"))
                    {
                        int previousCount = textures.arraySize;
                        textures.arraySize = count;
                        var names = inspectorView.Property(serializedObject, "textureNames");
                        var blends = inspectorView.Property(serializedObject, "overlayBlend");
                        int previousNames = names.arraySize;
                        int previousBlends = blends.arraySize;
                        names.arraySize = Mathf.Max(names.arraySize, count);
                        blends.arraySize = Mathf.Max(blends.arraySize, count);
                        for (int i = previousCount; i < count; i++)
                            textures.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        // Preserve names of stripped textures so Advanced View can reload them.
                        for (int i = previousNames; i < count; i++)
                            names.GetArrayElementAtIndex(i).stringValue = string.Empty;
                        for (int i = previousBlends; i < count; i++)
                            blends.GetArrayElementAtIndex(i).enumValueIndex = 0;
                    }
                }
                inspectorView.Field(serializedObject, "alphaMask", "Optional Alpha Mask");
                showStandardBlending = EditorGUILayout.Foldout(showStandardBlending, "Texture blending", true);
                if (showStandardBlending && !materialProperty.hasMultipleDifferentValues)
                {
                    var blends = inspectorView.Property(serializedObject, "overlayBlend");
                    for (int i = 0; i < blends.arraySize; i++)
                    {
                        string label = material != null && material.channels != null && i < material.channels.Length
                            ? material.channels[i].materialPropertyName : "Channel " + i;
                        EditorGUILayout.PropertyField(blends.GetArrayElementAtIndex(i), UMAInspectorView.Label(label));
                    }
                }
            }
            using (inspectorView.Section("Placement & atlas",
                "Overlay UV Rectangle places this overlay within its slot's UV space. Zero values retain the overlay's normal placement. Allow Source UV Cropping permits prepared slot UV bounds to crop unused source texture area for tighter atlases; disable it when this overlay is shared by slots whose UV layouts require the full source image."))
            {
                inspectorView.Field(serializedObject, "rect", "Overlay UV Rectangle");
                inspectorView.Field(serializedObject, "allowSourceUVCropping", "Allow Source UV Cropping");
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (Object item in targets)
                {
                    var edited = (OverlayDataAsset)item;
                    EditorUtility.SetDirty(edited);
                    UMAUpdateProcessor.UpdateOverlay(edited);
                }
            }
        }
    }
}
