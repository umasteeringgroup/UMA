using UnityEngine;
using UnityEditor;
using UnityEngine.Experimental.Rendering;

public class TextureArrayCreator
{
    private static string GetWritableOutputFolder(string sourceAssetPath)
    {
        string folder = System.IO.Path.GetDirectoryName(sourceAssetPath)?.Replace('\\', '/');
        if (UMA.UMAPathUtility.IsWritableProjectAssetPath(folder)) return folder;

        string fallback = UMA.UMAPathUtility.GeneratedRoot + "/TextureArrays";
        UMA.UMAPathUtility.EnsureAssetFolder(fallback);
        Debug.LogWarning(
            $"The source textures are in read-only package content. The texture array will be saved in '{fallback}'.");
        return fallback;
    }

    private static Texture2D[] GetSelectedTextures()
    {
        Object[] selectedObjects = Selection.objects;
        int textureCount = 0;
        for (int objectIndex = 0; objectIndex < selectedObjects.Length; objectIndex++)
        {
            if (selectedObjects[objectIndex] is Texture2D)
            {
                textureCount++;
            }
        }

        Texture2D[] textures = new Texture2D[textureCount];
        int textureIndex = 0;
        for (int objectIndex = 0; objectIndex < selectedObjects.Length; objectIndex++)
        {
            Texture2D texture = selectedObjects[objectIndex] as Texture2D;
            if (texture != null)
            {
                textures[textureIndex++] = texture;
            }
        }

        return textures;
    }

    [MenuItem("Assets/Create/Texture2DArray From Selection")]
    static void CreateTextureArray()
    {
        var textures = Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets);
        if (textures.Length == 0) return;

        // Sort textures by UDIM number to ensure correct order
        System.Array.Sort(textures, (a, b) => GetUDIMNumber(a.name).CompareTo(GetUDIMNumber(b.name)));

        int width = textures[0].width;
        int height = textures[0].height;
        TextureFormat format = textures[0].format;


        Texture2DArray texArray = new Texture2DArray(width, height, textures.Length, TextureFormat.ARGB32, true);
        texArray.wrapMode = TextureWrapMode.Repeat;

        /* for (int i = 0; i < textures.Length; i++)
         {
             Graphics.CopyTexture(textures[i], 0, 0, texArray, i, 0);
         }*/

        for (int i = 0; i < textures.Length; i++)
        {
            Texture2D tex = textures[i];
            texArray.SetPixels(tex.GetPixels(), i);
        }
        texArray.Apply();

        // Determine base name and folder from the first texture (assuming UDIM naming)
        string firstPath = AssetDatabase.GetAssetPath(textures[0]);
        string folder = GetWritableOutputFolder(firstPath);
        string baseName = GetBaseNameWithoutUDIM(textures[0].name);
        string assetPath = System.IO.Path.Combine(folder, baseName + ".asset").Replace('\\', '/');

        AssetDatabase.CreateAsset(texArray, assetPath);
        AssetDatabase.SaveAssets();
    }
    // Add this helper method
    private static bool IsNormalMap(Texture2D texture)
    {
        if (texture == null) return false;

        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path)) return false;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        return importer != null && importer.textureType == TextureImporterType.NormalMap;
    }

    private static int GetUDIMNumber(string textureName)
    {
        // Extract UDIM number from name (e.g., "texture.1001" -> 1001)
        int lastDot = textureName.LastIndexOf('.');
        if (lastDot > 0 && int.TryParse(textureName.Substring(lastDot + 1), out int udim))
        {
            return udim;
        }
        return 0; // Fallback for non-UDIM textures
    }

    private static string GetBaseNameWithoutUDIM(string textureName)
    {
        // Assume UDIM format: name.1001, strip the .number part
        int lastDot = textureName.LastIndexOf('.');
        if (lastDot > 0 && int.TryParse(textureName.Substring(lastDot + 1), out _))
        {
            return textureName.Substring(0, lastDot);
        }
        return textureName; // Fallback if no UDIM number
    }


    [MenuItem("Assets/Create/Build Normal Texture2DArray From Selection")]
    public static void BuildFromSelection()
    {
        var textures = GetSelectedTextures();
        if (textures.Length == 0) { Debug.LogError("Select one or more Texture2D normal maps."); return; }

        System.Array.Sort(textures, (a, b) => GetUDIMNumber(a.name).CompareTo(GetUDIMNumber(b.name)));

        foreach (var t in textures)
        {
            var path = AssetDatabase.GetAssetPath(t);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null || importer.textureType != TextureImporterType.NormalMap)
            {
                Debug.LogError($"Texture is not marked as Normal map: {path}");
                return;
            }
            if (importer.sRGBTexture)
            {
                Debug.LogError($"Normal maps must be linear (sRGB disabled): {path}");
                return;
            }
            if (importer.crunchedCompression)
            {
                Debug.LogError($"Disable Crunch compression for: {path}");
                return;
            }
        }

        var w = textures[0].width;
        var h = textures[0].height;
        var mipCount = textures[0].mipmapCount;
        var format = textures[0].graphicsFormat;
        foreach (var t in textures)
        {
            if (t.width != w || t.height != h) { Debug.LogError("All normals must have identical width/height."); return; }
            if (t.mipmapCount != mipCount) { Debug.LogError("All normals must have identical mip count."); return; }
            if (t.graphicsFormat != format) { Debug.LogError("All normals must have identical GraphicsFormat."); return; }
        }

        var flags = mipCount > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None;

        var array = new Texture2DArray(w, h, textures.Length, format, flags);
        array.wrapMode = TextureWrapMode.Repeat;
        array.filterMode = FilterMode.Trilinear;
        array.anisoLevel = 4;

        string firstPath = AssetDatabase.GetAssetPath(textures[0]);
        string folder = GetWritableOutputFolder(firstPath);
        string baseName = GetBaseNameWithoutUDIM(textures[0].name);
        array.name = baseName;

        for (int i = 0; i < textures.Length; i++)
        {
            for (int mip = 0; mip < mipCount; mip++)
            {
                Graphics.CopyTexture(textures[i], 0, mip, array, i, mip);
            }
        }

        string assetPath = System.IO.Path.Combine(folder, baseName + ".asset").Replace('\\', '/');
        AssetDatabase.CreateAsset(array, assetPath);
        AssetDatabase.SaveAssets();

        // Hint for shader encoding selection
        string fmt = format.ToString();
        // BC5 (RG) is typical for normal maps on most platforms; BC3/DXT5 may be used for DXT5nm
        int suggested = (fmt.Contains("BC5") || fmt.Contains("RG")) ? 0 : 1;
        string mode = suggested == 0 ? "RG_BC5 (0)" : "AG_DXT5nm (1)";
        Debug.Log($"Created Normal Texture2DArray at: {assetPath}\nFormat: {format}, Mips: {mipCount}\nSet material property _NormalArrayEncoding to {mode}, or Auto (2).");
    }

    public static void OldBuildFromSelection()
    {
        var textures = GetSelectedTextures();
        if (textures.Length == 0) { Debug.LogError("Select one or more Texture2D normal maps."); return; }

        // Sort by UDIM number to ensure correct array slice order
        System.Array.Sort(textures, (a, b) => GetUDIMNumber(a.name).CompareTo(GetUDIMNumber(b.name)));

        // Validate import settings
        foreach (var t in textures)
        {
            var path = AssetDatabase.GetAssetPath(t);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null || importer.textureType != TextureImporterType.NormalMap)
            {
                Debug.LogError($"Texture is not marked as Normal map: {path}");
                return;
            }
            if (importer.crunchedCompression)
            {
                Debug.LogError($"Disable Crunch compression for: {path}");
                return;
            }
        }

        // Validate dimensions/format/mips
        var w = textures[0].width;
        var h = textures[0].height;
        var mipCount = textures[0].mipmapCount;
        var format = textures[0].graphicsFormat; // Use GraphicsFormat to match GPU format
        foreach (var t in textures)
        {
            if (t.width != w || t.height != h) { Debug.LogError("All normals must have identical width/height."); return; }
            if (t.mipmapCount != mipCount) { Debug.LogError("All normals must have identical mip count."); return; }
            if (t.graphicsFormat != format) { Debug.LogError("All normals must have identical GraphicsFormat."); return; }
        }

        // Create a linear Texture2DArray that matches source GPU format
        var array = new Texture2DArray(w, h, textures.Length, format, TextureCreationFlags.MipChain | TextureCreationFlags.None);
        array.wrapMode = TextureWrapMode.Repeat;
        array.filterMode = FilterMode.Trilinear;
        array.anisoLevel = 4;

        // Name based on first UDIM (strip UDIM number)
        string firstPath = AssetDatabase.GetAssetPath(textures[0]);
        string folder = GetWritableOutputFolder(firstPath);
        string baseName = GetBaseNameWithoutUDIM(textures[0].name);
        array.name = baseName;

        // Copy GPU data slice-by-slice for every mip
        for (int i = 0; i < textures.Length; i++)
        {
            for (int mip = 0; mip < mipCount; mip++)
            {
                Graphics.CopyTexture(textures[i], 0, mip, array, i, mip);
            }
        }

        // Save asset next to the first UDIM texture, with base name (no UDIM number)
        string assetPath = System.IO.Path.Combine(folder, baseName + ".asset").Replace('\\', '/');
        AssetDatabase.CreateAsset(array, assetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created Texture2DArray at: {assetPath}\nFormat: {array.graphicsFormat}, Mips: {mipCount}");
    }
}
