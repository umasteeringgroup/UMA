using UnityEditor;

namespace UMA.HairCards.Editor
{
    internal static class HairExamplesMenu
    {
        [MenuItem("UMA/Hair Cards/Examples/Braided Bun/Classic", priority = 155)]
        private static void OpenBun() => Open("BraidedBun/BraidedBun_HairGroom.asset");
        [MenuItem("UMA/Hair Cards/Examples/Braided Bun/Loose", priority = 156)]
        private static void OpenBunLoose() => Open("BraidedBun/BraidedBun_Loose_HairGroom.asset");
        [MenuItem("UMA/Hair Cards/Examples/Braided Bun/Compact Copper", priority = 157)]
        private static void OpenBunCompact() => Open("BraidedBun/BraidedBun_Compact_HairGroom.asset");
        [MenuItem("UMA/Hair Cards/Examples/Short Hair Part/Classic", priority = 152)]
        private static void OpenRegular() => Open("ShortHairPart/ShortHairPart_HairGroom.asset");

        [MenuItem("UMA/Hair Cards/Examples/Short Hair Part/Relaxed", priority = 153)]
        private static void OpenRegularRelaxed() => Open("ShortHairPart/ShortHairPart_Relaxed_HairGroom.asset");

        [MenuItem("UMA/Hair Cards/Examples/Short Hair Part/Close Cut", priority = 154)]
        private static void OpenRegularClose() => Open("ShortHairPart/ShortHairPart_CloseCut_HairGroom.asset");

        [MenuItem("UMA/Hair Cards/Examples/Short Hair Part/Natural", priority = 155)]
        private static void OpenNatural() => Open("ShortHairPart/ShortHairPart_Natural_HairGroom.asset");

        [MenuItem("UMA/Hair Cards/Examples/Short Hair Part/Front Flip Natural", priority = 156)]
        private static void OpenFrontFlip() => Open("ShortHairPart/ShortHairPart_FrontFlipNatural_HairGroom.asset");

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
