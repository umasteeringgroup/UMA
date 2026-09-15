using UnityEditor;

namespace UMA.HairCards.Editor
{
    internal static class HairExamplesMenu
    {
        [MenuItem("UMA/Hair Cards/Examples/Curly Volume", priority = 150)]
        private static void OpenCurly() => Open("CurlyVolume/Curly_HairGroom.asset");

        [MenuItem("UMA/Hair Cards/Examples/Pointy Swept", priority = 151)]
        private static void OpenPointy() => Open("PointySwept/Pointy_HairGroom.asset");

        private static void Open(string relativePath)
        {
            string path = "Assets/UMAProjectData/HairCards/Examples/" + relativePath;
            var groom = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(path);
            if (groom == null)
            {
                EditorUtility.DisplayDialog("Hair Example Not Installed", "This optional example is not included in this project. Expected asset:\n" + path +
                    "\n\nYou can still apply the generation preset to your own groom from Hair Nodes.", "OK");
                return;
            }
            Selection.activeObject = groom;
            var stage = HairCardStage.ShowStage(groom);
            if (stage != null) stage.FocusCurrentArea();
        }
    }
}
