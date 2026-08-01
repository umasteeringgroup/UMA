using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class IconCreator : MonoBehaviour
{
    // RegionToCamera is a simple class that holds a region name and a camera reference.
    // This can be used to associate specific cameras with specific regions of the character for icon creation.
    [Serializable]
    public class CameraRegions 
    {
        public Camera camera;
        public List<string> regions = new List<string>();
        public CameraRegions(string regionName, Camera camera)
        {
            regions = new List<string> { regionName };
            this.camera = camera;
        }
    }

    public string rootFolder = string.Empty;

    public DynamicCharacterAvatar avatar;

    // A list of RegionToCamera instances, allowing the user to specify multiple region-camera pairs for icon creation.
    public List<CameraRegions> regionToCameraList = new List<CameraRegions>();

    // Setup.
    public Vector2 IconDimensions = new Vector2(128, 128);

    [Tooltip("Resize texture-derived thumbnails to Icon Dimensions instead of preserving the source crop resolution.")]
    public bool ResizeTextureDerivedThumbnails = false;

    public float PreviewSize = 128.0f;
    public float scrollAreaHeight = 90.0f;
    public int currentCameraIndex = 0;
    private Vector2 previewScrollPosition = Vector2.zero;
    private int selectedRegionIndex = 0;
    private bool showRegionDropdown = false;
    private GUISkin skin = null;
    private Texture2D flatNormal = null;
    private Texture2D flatHover = null;
    private Texture2D flatActive = null;
    private string currentStatus = "";
    [Range(-1.0f, 1.0f)]
    public float brightness = 0.0f;
    [Range(-1.0f, 1.0f)]
    public float contrast = 0.0f;

    Texture2D MakeTex(Color c)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
        return tex;
    }

    private void OnEnable()
    {

    }

    public void EnsureSkin()
    {
        if (skin == null)
        {
            skin = ScriptableObject.Instantiate(GUI.skin);

            flatNormal = MakeTex(new Color(0.25f, 0.25f, 0.25f)); // normal
            flatHover = MakeTex(new Color(0.30f, 0.30f, 0.30f)); // hover
            flatActive = MakeTex(new Color(0.20f, 0.20f, 0.20f)); // click

            skin.button.normal.background = flatNormal;
            skin.button.hover.background = flatHover;
            skin.button.active.background = flatActive;
            skin.button.focused.background = flatNormal; // optional

            skin.button.border = new RectOffset(0, 0, 0, 0);
            skin.button.padding = new RectOffset(8, 8, 4, 4);
        }
    }

    private void OnDisable()
    {
        if (skin != null)
        {
            DestroyImmediate(skin);
            skin = null;
        }
        if (flatNormal != null)
        {
            DestroyImmediate(flatNormal);
            flatNormal = null;
        }
        if (flatHover != null)
        {
            DestroyImmediate(flatHover);
            flatHover = null;
        }
        if (flatActive != null)
        {
            DestroyImmediate(flatActive);
            flatActive = null;
        }
    }



    private void OnGUI()
    {
        EnsureSkin();
        var oldSkin = GUI.skin; 
        GUI.skin = skin;
        //GUILayout.BeginArea(new Rect(10f, 10f, Mathf.Max(100f, Screen.width - 20f), Mathf.Max(100f, Screen.height - 20f)), GUI.skin.box);

        DrawCameraRegionPreviews();
        //GUILayout.Space(10f);
        DrawRegionControls();

        //GUILayout.EndArea();
        GUI.skin = oldSkin;
    }

    private void DrawCameraRegionPreviews()
    {
        float previewWidth = Mathf.Max(64f, PreviewSize); ;
        float previewHeight = Mathf.Max(64f, PreviewSize);
        float scrollHeight = previewHeight + scrollAreaHeight;

        previewScrollPosition = GUILayout.BeginScrollView(previewScrollPosition, GUILayout.Height(scrollHeight));
        GUILayout.BeginHorizontal();

        if (regionToCameraList == null || regionToCameraList.Count == 0)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(previewWidth + 20f));
            GUILayout.Label("No camera regions configured.");
            GUILayout.EndVertical();
        }
        else
        {
            for (int i = 0; i < regionToCameraList.Count; i++)
            {
                DrawCameraRegionPreview(regionToCameraList[i], i, previewWidth, previewHeight);
            }
        }

        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
    }

    private void DrawCameraRegionPreview(CameraRegions cameraRegions, int index, float previewWidth, float previewHeight)
    {
        bool isValid = ValidateCameraRegion(cameraRegions, out string validationMessage);
        string heading = cameraRegions != null && cameraRegions.camera != null ? cameraRegions.camera.name : $"Camera {index + 1}";

        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(previewWidth + 20f));
        GUILayout.Label(heading);

        Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
        if (isValid)
        {
            GUI.DrawTexture(previewRect, cameraRegions.camera.targetTexture, ScaleMode.ScaleToFit, false);
        }
        else
        {
            GUI.Box(previewRect, validationMessage);
        }

        if (!string.IsNullOrEmpty(validationMessage))
        {
            GUILayout.Label(validationMessage);
        }


        if (cameraRegions != null && cameraRegions.regions != null && cameraRegions.regions.Count > 0)
        {
            GUILayout.Label(GetCameraRegionLabel(cameraRegions));
        }
        else
        {
            GUILayout.Label("No regions assigned.");
        }

        GUILayout.EndVertical();
    }

    private string GetCameraRegionLabel(CameraRegions cameraRegions)
    {
        if (cameraRegions == null || cameraRegions.camera == null)
        {
            return "Unassigned Camera";
        }
        string regionNames = cameraRegions.regions != null && cameraRegions.regions.Count > 0
            ? string.Join(", ", cameraRegions.regions)
            : "No regions";
        if (regionNames.Length > 30)
        {
            regionNames = regionNames.Substring(0, 27) + "...";
        }
        return regionNames;
    }


    private void DrawRegionControls()
    {
        List<string> raceRegions = GetRaceRegionsFromAvatarRaceData();
        SyncSelectedRegion(raceRegions);

        GUILayout.Label("Icon Creation", GUI.skin.box);
        GUILayout.Label("Select the region to render, and click 'Render Now' to generate icons for the current wearable items in that region");
        GUILayout.Label("Click 'Generate All Icons' to render icons for all wearable items across all regions.");
        GUILayout.Label("Icons will be saved to: " + GetOutputBaseFolder());
        GUILayout.Label("(This can be changed by modifying the root folder field on the Icon Creator component)", GUILayout.ExpandWidth(false));
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Region to Render", GUILayout.Width(110f));

        bool hasRaceRegions = raceRegions != null && raceRegions.Count > 0;
        GUI.enabled = hasRaceRegions;

        string selectedRegionLabel = hasRaceRegions ? raceRegions[selectedRegionIndex] : "No regions available";
        if (GUILayout.Button(selectedRegionLabel, GUILayout.Width(220f)))
        {
            showRegionDropdown = !showRegionDropdown;
        }

        if (GUILayout.Button("Render Now", GUILayout.Width(100f)))
        {
            string region = hasRaceRegions ? raceRegions[selectedRegionIndex] : null;
            if (!string.IsNullOrEmpty(region))
            {
                StartCoroutine(RenderRegion(region));
            }
        }

        if (GUILayout.Button("Generate All Icons", GUILayout.Width(140f)))
        {
            StartCoroutine(RenderAllRegions());
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!hasRaceRegions)
        {
            GUILayout.Label("Assign an avatar with active race data to select a region.");
            showRegionDropdown = false;
            return;
        }

        if (showRegionDropdown)
        {
            GUILayout.BeginVertical(/*GUI.skin.box*/);
            for (int i = 0; i < raceRegions.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(110f));
                if (GUILayout.Button(raceRegions[i], GUILayout.Width(220f)))
                {
                    selectedRegionIndex = i;
                    showRegionDropdown = false;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
    }


    private IEnumerator RenderRegion(string region)
    {
        var ugb = UMAAssetIndexer.Instance.Generator;

        if (string.IsNullOrEmpty(region))
        {
            currentStatus = "No region selected for rendering.";
            yield break;
        }
        currentStatus = $"Rendering region: {region}";
        var recipes = avatar.AvailableRecipes;
        if (recipes != null && recipes.TryGetValue(region, out List<UMATextRecipe> regionRecipes))
        {
            foreach (var recipe in regionRecipes)
            {
                var uwr = recipe as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    // If thumbnailFromTexture is enabled, generate thumbnail from recipe texture instead of camera capture
                    if (uwr.thumbnailFromTexture)
                    {
                        string raceName = avatar.activeRace.racedata.raceName;
                        string textureOutputPath = GenerateThumbnailFromRecipeTexture(uwr, region, raceName);
                        if (!string.IsNullOrEmpty(textureOutputPath))
                        {
#if UNITY_EDITOR
                            UpdateWardrobeRecipeThumb(uwr, textureOutputPath);
#endif
                            currentStatus = $"Generated texture thumbnail for recipe: {uwr.name}";
                        }
                        else
                        {
                            currentStatus = $"Failed to generate texture thumbnail for recipe: {uwr.name} (no texture found in recipe)";
                        }
                        continue;
                    }

                    currentStatus = $"Rendering recipe: {uwr.name} for region: {region}";
                    avatar.ClearWearableItems(region);
                    avatar.SetWearableItem(uwr);
                    avatar.BuildCharacter();
                    ugb.GenerateSingleUMA(avatar, true);
                    // Wait until the avatar has finished updating before attempting to capture the icon.

                    // wait a frame to ensure character is rendered with the new wardrobe item before capturing the icon.
                    yield return null;
                    // ensure wearable items are cleared after rendering the icon so that the avatar is back to a clean state for the next recipe to be rendered.
                    // and that the last one doesn't affect the next region to be rendered.
                    avatar.ClearWearableItems(region);
                    CameraRegions cameraRegions = GetCameraRegionsForRegion(region);
                    if (cameraRegions == null)
                    {
                        currentStatus = $"No camera configured for region: {region}";
                        continue;
                    }

                    if (cameraRegions.camera == null)
                    {
                        currentStatus = $"Camera is missing for region: {region}";
                        continue;
                    }

                    if (cameraRegions.camera.targetTexture == null)
                    {
                        currentStatus = $"Camera target texture is missing for region: {region}";
                        continue;
                    }

                    string captureRaceName = avatar.activeRace.racedata.raceName;
                    string outputFolder = GetOutputFolder(region, captureRaceName);
                    string outputPath = GetThumbnailOutputPath(uwr, captureRaceName, outputFolder);
                    if (!CaptureRenderTextureToPng(cameraRegions.camera, outputPath))
                    {
                        currentStatus = $"Failed to capture icon for recipe: {uwr.name}";
                        continue;
                    }

#if UNITY_EDITOR
                    UpdateWardrobeRecipeThumb(uwr, outputPath);
#endif
                    // todo: update the manual progress bar here to show progress for each region and recipe being rendered.
                }
            }
        }
        currentStatus = $"Finished rendering region: {region}";
    }


    private IEnumerator RenderAllRegions()
    {
        Dictionary<string, List<UMATextRecipe>> recipes = avatar.AvailableRecipes;
        foreach(var kvp in recipes)
        {
            string region = kvp.Key;
            yield return StartCoroutine(RenderRegion(region));
        }
    }

    private string GenerateThumbnailFromRecipeTexture(UMAWardrobeRecipe uwr, string region, string raceName)
    {
        if (uwr == null)
        {
            return null;
        }

        // Unpack the recipe to access slot/overlay data
        UMAData.UMARecipe umaRecipe = uwr.GetCachedRecipe();
        if (umaRecipe == null || umaRecipe.slotDataList == null || umaRecipe.slotDataList.Length == 0)
        {
            Debug.LogWarning($"IconCreator: Recipe '{uwr.name}' has no slots.");
            return null;
        }

        SlotData firstSlot = umaRecipe.slotDataList[0];
        foreach (var slot in umaRecipe.slotDataList)
        {
            if (slot != null && slot.OverlayCount > 0)
            {
                firstSlot = slot;
                break;
            }
        }
        if (firstSlot == null || firstSlot.OverlayCount == 0)
        {
            Debug.LogWarning($"IconCreator: Recipe '{uwr.name}' first slot has no overlays.");
            return null;
        }

        OverlayData firstOverlay = firstSlot.GetOverlay(0);
        if (firstOverlay == null || firstOverlay.asset == null)
        {
            Debug.LogWarning($"IconCreator: Recipe '{uwr.name}' first overlay asset is null.");
            return null;
        }

        Texture[] textureList = firstOverlay.asset.textureList;
        if (textureList == null || textureList.Length == 0 || textureList[0] == null)
        {
            Debug.LogWarning($"IconCreator: Recipe '{uwr.name}' has no textures in first overlay.");
            return null;
        }

        Texture sourceTexture = textureList[0];
        Vector2Int iconDimensions = GetTextureThumbnailDimensions(sourceTexture, uwr.thumbnailRect);
        Texture2D outputTexture = GetReadableTexture2D(
            sourceTexture,
            uwr.thumbnailRect,
            iconDimensions.x,
            iconDimensions.y);
        if (outputTexture == null)
        {
            Debug.LogWarning($"IconCreator: Failed to get readable texture for recipe '{uwr.name}'.");
            return null;
        }

        try
        {
            Color[] outputPixels = outputTexture.GetPixels();
            for (int i = 0; i < outputPixels.Length; i++)
            {
                float gray = outputPixels[i].grayscale;
                gray = ApplyBrightnessContrast(gray, brightness, contrast);
                outputPixels[i] = new Color(gray, gray, gray, outputPixels[i].a);
            }
            outputTexture.SetPixels(outputPixels);
            outputTexture.Apply();

            string outputFolder = GetOutputFolder(region, raceName);
            string outputPath = GetThumbnailOutputPath(uwr, raceName, outputFolder);
            byte[] pngBytes = outputTexture.EncodeToPNG();
            File.WriteAllBytes(outputPath, pngBytes);

            return outputPath;
        }
        finally
        {
            DestroyTexture(outputTexture);
        }
    }

    private static Texture2D GetReadableTexture2D(Texture source, Rect sourceRect, int width, int height)
    {
        if (source == null || width < 1 || height < 1)
        {
            return null;
        }

        RectInt sourcePixelRect = GetSourcePixelRect(source, sourceRect);
        Vector2 scale = new Vector2(
            sourcePixelRect.width / (float)source.width,
            sourcePixelRect.height / (float)source.height);
        Vector2 offset = new Vector2(
            sourcePixelRect.x / (float)source.width,
            sourcePixelRect.y / (float)source.height);

        RenderTexture tmp = RenderTexture.GetTemporary(
            width, height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D result = null;
        try
        {
            Graphics.Blit(source, tmp, scale, offset);
            RenderTexture.active = tmp;

            result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            return result;
        }
        catch
        {
            if (result != null)
            {
                DestroyTexture(result);
            }
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);
        }
    }

    private Vector2Int GetTextureThumbnailDimensions(Texture source, Rect sourceRect)
    {
        if (ResizeTextureDerivedThumbnails)
        {
            return GetIconDimensions();
        }

        RectInt sourcePixelRect = GetSourcePixelRect(source, sourceRect);
        return new Vector2Int(sourcePixelRect.width, sourcePixelRect.height);
    }

    private static RectInt GetSourcePixelRect(Texture source, Rect sourceRect)
    {
        int sourceX = Mathf.Clamp(Mathf.RoundToInt(sourceRect.x * source.width), 0, source.width - 1);
        int sourceY = Mathf.Clamp(Mathf.RoundToInt(sourceRect.y * source.height), 0, source.height - 1);
        int sourceWidth = Mathf.Clamp(Mathf.RoundToInt(sourceRect.width * source.width), 1, source.width - sourceX);
        int sourceHeight = Mathf.Clamp(Mathf.RoundToInt(sourceRect.height * source.height), 1, source.height - sourceY);
        return new RectInt(sourceX, sourceY, sourceWidth, sourceHeight);
    }

    private static float ApplyBrightnessContrast(float gray, float brightnessValue, float contrastValue)
    {
        // Apply brightness (additive: -1 = black, 0 = no change, +1 = white)
        gray = Mathf.Clamp01(gray + brightnessValue);
        // Apply contrast (multiply around 0.5 midpoint: -1 = flat gray, 0 = no change, +1 = max contrast)
        gray = Mathf.Clamp01((gray - 0.5f) * (1f + contrastValue) + 0.5f);
        return gray;
    }

    private CameraRegions GetCameraRegionsForRegion(string region)
    {
        if (string.IsNullOrEmpty(region) || regionToCameraList == null)
        {
            return null;
        }

        for (int i = 0; i < regionToCameraList.Count; i++)
        {
            CameraRegions cameraRegions = regionToCameraList[i];
            if (cameraRegions == null || cameraRegions.regions == null)
            {
                continue;
            }

            for (int i1 = 0; i1 < cameraRegions.regions.Count; i1++)
            {
                if (cameraRegions.regions[i1] == region)
                {
                    return cameraRegions;
                }
            }
        }

        return null;
    }

    private string GetOutputFolder(string region, string race)
    {
        string baseFolder = GetOutputBaseFolder();
        string sanitizedRootFolder = NormalizeRootFolder(rootFolder);
        string sanitizedRegion = SanitizePathSegment(region);
        string sanitizedRace = SanitizePathSegment(race);
        string outputFolder = string.IsNullOrEmpty(sanitizedRootFolder)
            ? Path.Combine(baseFolder, sanitizedRegion, sanitizedRace)
            : Path.Combine(baseFolder, sanitizedRootFolder, sanitizedRegion, sanitizedRace);

        Directory.CreateDirectory(outputFolder);
        return outputFolder;
    }

    private string GetOutputBaseFolder()
    {
#if UNITY_EDITOR
        return Application.dataPath;
#else
        return Application.persistentDataPath;
#endif
    }

    private string NormalizeRootFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return string.Empty;
        }

        string normalizedFolder = folder.Replace('\\', '/').Trim();
        string normalizedAssetsPath = Application.dataPath.Replace('\\', '/');

        if (normalizedFolder.StartsWith(normalizedAssetsPath, StringComparison.OrdinalIgnoreCase))
        {
            normalizedFolder = normalizedFolder.Substring(normalizedAssetsPath.Length);
        }
        else if (normalizedFolder.Equals("Assets", StringComparison.OrdinalIgnoreCase))
        {
            normalizedFolder = string.Empty;
        }
        else if (normalizedFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedFolder = normalizedFolder.Substring("Assets/".Length);
        }

        normalizedFolder = normalizedFolder.TrimStart('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedFolder;
    }

    private string SanitizePathSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Unnamed";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            value = value.Replace(invalidChars[i], '_');
        }

        return value;
    }

    private Vector2Int GetIconDimensions()
    {
        return new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(IconDimensions.x)),
            Mathf.Max(1, Mathf.RoundToInt(IconDimensions.y)));
    }

    private string GetThumbnailOutputPath(
        UMAWardrobeRecipe uwr,
        string raceName,
        string outputFolder)
    {
#if UNITY_EDITOR
        string existingAssetPath = GetExistingThumbnailAssetPath(uwr, raceName);
        string existingAbsolutePath = GetAbsoluteAssetPath(existingAssetPath);
        if (IsFileInFolder(existingAbsolutePath, outputFolder))
        {
            return existingAbsolutePath;
        }
#endif
        return Path.Combine(outputFolder, GetNewThumbnailFileName(uwr));
    }

    private string GetNewThumbnailFileName(UMAWardrobeRecipe uwr)
    {
        string identifier = string.Empty;
#if UNITY_EDITOR
        string recipePath = AssetDatabase.GetAssetPath(uwr);
        string recipeGuid = AssetDatabase.AssetPathToGUID(recipePath);
        if (!string.IsNullOrEmpty(recipeGuid))
        {
            identifier = "_" + recipeGuid.Substring(0, 8);
        }
#endif
        return SanitizePathSegment(uwr.name + identifier) + ".png";
    }

#if UNITY_EDITOR
    private static string GetExistingThumbnailAssetPath(UMAWardrobeRecipe uwr, string raceName)
    {
        if (uwr == null || uwr.wardrobeRecipeThumbs == null)
        {
            return null;
        }

        for (int i = 0; i < uwr.wardrobeRecipeThumbs.Count; i++)
        {
            WardrobeRecipeThumb thumbnail = uwr.wardrobeRecipeThumbs[i];
            if (thumbnail == null || thumbnail.race != raceName)
            {
                continue;
            }

            string spritePath = AssetDatabase.GetAssetPath(thumbnail.thumb);
            if (!string.IsNullOrEmpty(spritePath))
            {
                return spritePath;
            }
            if (!string.IsNullOrEmpty(thumbnail.filename))
            {
                return thumbnail.filename;
            }
        }
        return null;
    }

    private static string GetAbsoluteAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        string normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            !normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relativePath = normalizedPath.Length == "Assets".Length
            ? string.Empty
            : normalizedPath.Substring("Assets/".Length);
        return Path.GetFullPath(Path.Combine(Application.dataPath, relativePath));
    }

    private static bool IsFileInFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(folderPath) || !File.Exists(filePath))
        {
            return false;
        }

        string fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        string expectedDirectory = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fileDirectory != null &&
            fileDirectory.Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase);
    }
#endif

    private bool CaptureRenderTextureToPng(Camera captureCamera, string outputPath)
    {
        if (captureCamera == null || captureCamera.targetTexture == null || string.IsNullOrEmpty(outputPath))
        {
            return false;
        }

        RenderTexture originalTarget = captureCamera.targetTexture;
        Vector2Int iconDimensions = GetIconDimensions();
        RenderTexture captureTexture = RenderTexture.GetTemporary(
            iconDimensions.x,
            iconDimensions.y,
            originalTarget.depth,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previousActive = RenderTexture.active;
        Texture2D capturedTexture = null;
        try
        {
            captureCamera.targetTexture = captureTexture;
            captureCamera.Render();
            RenderTexture.active = captureTexture;

            capturedTexture = new Texture2D(
                iconDimensions.x,
                iconDimensions.y,
                TextureFormat.RGBA32,
                false);
            capturedTexture.ReadPixels(
                new Rect(0f, 0f, iconDimensions.x, iconDimensions.y),
                0,
                0);
            capturedTexture.Apply();

            byte[] pngBytes = capturedTexture.EncodeToPNG();
            File.WriteAllBytes(outputPath, pngBytes);
        }
        finally
        {
            captureCamera.targetTexture = originalTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(captureTexture);
            if (capturedTexture != null)
            {
                DestroyTexture(capturedTexture);
            }
        }

        return true;
    }

    private static void DestroyTexture(Texture2D texture)
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }
#else
        Destroy(texture);
#endif
    }

#if UNITY_EDITOR
    private void UpdateWardrobeRecipeThumb(UMAWardrobeRecipe uwr, string outputPath)
    {
        if (uwr == null || avatar == null || avatar.activeRace.data == null || string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        string relativePath = GetAssetRelativePath(outputPath);
        if (string.IsNullOrEmpty(relativePath))
        {
            return;
        }

        AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
        TextureImporter textureImporter = AssetImporter.GetAtPath(relativePath) as TextureImporter;
        if (textureImporter != null)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.mipmapEnabled = false;
            textureImporter.alphaIsTransparency = true;
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.textureCompression = TextureImporterCompression.Compressed;
            textureImporter.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(relativePath);
        if (sprite == null)
        {
            return;
        }

        string raceName = avatar.activeRace.data.raceName;
        Undo.RecordObject(uwr, "Update Wardrobe Recipe Thumbnail");

        if (uwr.wardrobeRecipeThumbs == null)
        {
            uwr.wardrobeRecipeThumbs = new List<WardrobeRecipeThumb>();
        }

        WardrobeRecipeThumb wardrobeRecipeThumb = null;
        for (int i = 0; i < uwr.wardrobeRecipeThumbs.Count; i++)
        {
            if (uwr.wardrobeRecipeThumbs[i] != null && uwr.wardrobeRecipeThumbs[i].race == raceName)
            {
                wardrobeRecipeThumb = uwr.wardrobeRecipeThumbs[i];
                break;
            }
        }

        if (wardrobeRecipeThumb == null)
        {
            wardrobeRecipeThumb = new WardrobeRecipeThumb();
            uwr.wardrobeRecipeThumbs.Add(wardrobeRecipeThumb);
        }

        wardrobeRecipeThumb.race = raceName;
        wardrobeRecipeThumb.filename = relativePath;
        wardrobeRecipeThumb.thumb = sprite;

        EditorUtility.SetDirty(uwr);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private string GetAssetRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return null;
        }

        string normalizedDataPath = Application.dataPath.Replace('\\', '/');
        string normalizedAbsolutePath = absolutePath.Replace('\\', '/');
        if (!normalizedAbsolutePath.StartsWith(normalizedDataPath))
        {
            return null;
        }

        return "Assets" + normalizedAbsolutePath.Substring(normalizedDataPath.Length);
    }
#endif

    private void OnValidate()
    {
        Vector2Int iconDimensions = GetIconDimensions();
        IconDimensions = new Vector2(iconDimensions.x, iconDimensions.y);
    }

    private bool ValidateCameraRegion(CameraRegions cameraRegions, out string validationMessage)
    {
        validationMessage = string.Empty;

        if (cameraRegions == null)
        {
            validationMessage = "Camera region entry is null.";
            return false;
        }

        if (cameraRegions.camera == null)
        {
            validationMessage = "Camera is not assigned.";
            return false;
        }

        if (!cameraRegions.camera.gameObject.activeInHierarchy)
        {
            validationMessage = "Camera GameObject is inactive.";
            return false;
        }

        //RefreshCameraPreview(cameraRegions);
        return true;
    }

    private void RefreshCameraPreview(CameraRegions cameraRegions)
    {
        if (cameraRegions == null || cameraRegions.camera == null )
        {
            return;
        }

        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        cameraRegions.camera.Render();
    }

    private List<string> GetRaceRegionsFromAvatarRaceData()
    {
        if (avatar == null)
        {
            return null;
        }

        if (avatar.activeRace.data == null)
        {
            return null;
        }

        if (avatar.activeRace.data.Regions == null)
        {
            return null;
        }

        return new List<string>(avatar.activeRace.data.Regions);
    }

    private void SyncSelectedRegion(List<string> raceRegions)
    {
        if (raceRegions == null || raceRegions.Count == 0)
        {
            selectedRegionIndex = 0;
            return;
        }

        selectedRegionIndex = Mathf.Clamp(selectedRegionIndex, 0, raceRegions.Count - 1);
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
