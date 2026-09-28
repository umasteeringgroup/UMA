using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class RaceInspector
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(RaceData));

        private void DrawStandardInspector()
        {
            serializedObject.UpdateIfRequiredOrScript();
            using (inspectorView.Section("Race & base body",
                "Race Name is the stable identifier used by recipes and avatars. Friendly Name is the player-facing label used by user interfaces; leave it empty to use the asset name. Use Source FBX Body builds the base directly from the configured source renderer and its mesh-hide bindings; otherwise Base Race Recipe supplies the starting slots, overlays, and colors. Tags organize the race and support tag-driven content rules."))
            {
                EditorGUILayout.LabelField("Race Name", race.raceName);
                inspectorView.Field(serializedObject, "_friendlyName", "Friendly Name");
                inspectorView.Field(serializedObject, "useFbxRoute", "Use Source FBX Body");
                if (inspectorView.Property(serializedObject, "useFbxRoute").boolValue)
                {
                    inspectorView.Field(serializedObject, "baseFbxRenderer", "Base Body Renderer");
                    inspectorView.Field(serializedObject, "fbxBaseMeshHideBindings", "Body Hiding");
                }
                else inspectorView.Field(serializedObject, "baseRaceRecipe", "Base Race Recipe");
                inspectorView.Field(serializedObject, "tags", "Tags");
            }
            using (inspectorView.Section("Rig & expressions",
                "Animation Rig selects Humanoid or Generic avatar generation. Generic rigs require a Root Motion Bone; Humanoid rigs use the T-Pose asset for Mecanim mapping. Expression Group (UMA 3) supplies the facial poses and expression controls available to generated avatars."))
            {
                inspectorView.Field(serializedObject, "umaTarget", "Animation Rig");
                if (race.umaTarget == RaceData.UMATarget.Generic)
                    inspectorView.Field(serializedObject, "genericRootMotionTransformName", "Root Motion Bone");
                inspectorView.Field(serializedObject, "TPose", "T-Pose");
                inspectorView.Field(serializedObject, "expressionGroup", "Expression Group (UMA 3)");
                if (race.expressionGroup == null && race.expressionSet != null)
                    EditorGUILayout.HelpBox("This race uses legacy expressions. They remain unchanged and are available in Advanced View.", MessageType.Info);
            }
            using (inspectorView.Section("DNA & body shape",
                "DNA Groups define the artist-facing body and face controls available for this race. Height, Radius, and Mass provide the race's physical defaults for systems that consume them. Legacy DNA remains readable in Advanced View, but new races should use UMA 3 DNA groups."))
            {
                if (race.useNewDNA) inspectorView.Field(serializedObject, "DNACollection", "DNA Groups");
                else EditorGUILayout.HelpBox("This race uses legacy DNA. Use Advanced View to manage or migrate its DNA system. Switching inspector views does not convert the race.", MessageType.Info);
                inspectorView.Field(serializedObject, "raceHeight", "Height");
                inspectorView.Field(serializedObject, "raceRadius", "Radius");
                inspectorView.Field(serializedObject, "raceMass", "Mass");
            }
            using (inspectorView.Section("Wardrobe & presentation",
                "Wardrobe Regions define the named equipment areas recipes can occupy or suppress. Thumbnails provide full-body, face, and per-region artwork used by character-creation interfaces. Keep region names stable after publishing wardrobe content."))
            {
                inspectorView.Field(serializedObject, "Regions", "Wardrobe Regions");
                inspectorView.Field(serializedObject, "raceThumbnails", "Thumbnails");
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                _bsCacheValid = false;
                race.ValidateWardrobeSlots();
                _needsUpdate = true;
                DoUpdate();
            }
            using (inspectorView.Section("Validation",
                "Validate Race checks the base content, rig, DNA, expression, and wardrobe configuration and reports errors or warnings below. Validation is read-only; fix reported source assets or settings, then run it again."))
            {
                if (GUILayout.Button("Validate Race"))
                {
                    ValidationMessages.Clear();
                    ValidationMessages.AddRange(UMARaceValidation.GetInspectorMessages(race));
                }
                foreach (string message in ValidationMessages)
                    EditorGUILayout.HelpBox(message, message.StartsWith("Error:") ? MessageType.Error :
                        message.StartsWith("Warning:") ? MessageType.Warning : MessageType.Info);
            }
        }
    }
}
