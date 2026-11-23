using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Adds expression wrinkles to the selected UMA 3 avatar: the <see cref="UMAExpressionWrinkles"/> component, which
    /// rebuilds the avatar with the wrinkle maps in its head atlas, with the UMA 3 profile driving it from expressions.
    /// </summary>
    public static class UMAWrinklePreview
    {
        const string MenuPath = "UMA/Skin/Add Expression Wrinkles to Selected Avatar";

        [MenuItem(MenuPath, priority = 41)]
        static void AddMenu()
        {
            var dca = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<DynamicCharacterAvatar>() : null;
            if (dca == null)
            {
                EditorUtility.DisplayDialog("Expression Wrinkles", "Select an UMA 3 avatar (DynamicCharacterAvatar) in the scene first.", "OK");
                return;
            }
            string message;
            var component = Add(dca, out message);
            if (component == null) { EditorUtility.DisplayDialog("Expression Wrinkles", message, "OK"); return; }
            Selection.activeObject = component.gameObject;
            Debug.Log(message, component);
        }

        /// <summary>
        /// Adds (or enables) the wrinkle component on <paramref name="dca"/>, making the UMA 3 wrinkle assets first if
        /// they are missing. Returns null with the reason in <paramref name="message"/>.
        /// </summary>
        public static UMAExpressionWrinkles Add(DynamicCharacterAvatar dca, out string message)
        {
            var overlay = UMAWrinkleAssets.EnsureUMA3Assets(out message);
            if (overlay == null) return null;
            var component = dca.GetComponent<UMAExpressionWrinkles>();
            if (component == null) component = Undo.AddComponent<UMAExpressionWrinkles>(dca.gameObject);
            Undo.RecordObject(component, "Expression Wrinkles");
            component.wrinkleOverlay = overlay;
            if (component.profile == null) component.profile = UMAWrinkleAssets.EnsureUMA3Profile();
            if (!component.enabled) component.enabled = true;
            else if (!component.HasWrinkleMaterial(dca.umaData)) component.RebuildCharacter();
            message = "Expression wrinkles added to " + dca.name + ". They follow its expressions through the profile " + component.profile.name
                + "; clear the profile on the UMA Expression Wrinkles component to set the zone weights by hand.";
            return component;
        }
    }
}
