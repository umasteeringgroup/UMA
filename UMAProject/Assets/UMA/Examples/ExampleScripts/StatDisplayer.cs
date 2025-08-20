using UMA;
using UnityEngine;

public class StatDisplayer : MonoBehaviour
{
    private UMAGenerator umaGenerator = null;

    // Styles for bold text and shadow
    private GUIStyle _boldStyle;
    private GUIStyle _shadowStyle;
    private int _lastBaseSize = -1;

    private void EnsureStyles()
    {
        // Derive from current skin and enlarge by 20%
        int baseSize = (GUI.skin != null && GUI.skin.label != null && GUI.skin.label.fontSize > 0)
            ? GUI.skin.label.fontSize
            : 12;

        if (_boldStyle == null || _lastBaseSize != baseSize)
        {
            int targetSize = Mathf.RoundToInt(baseSize * 1.2f);
            _lastBaseSize = baseSize;

            _boldStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = targetSize
            };
            _boldStyle.normal.textColor = Color.white;

            _shadowStyle = new GUIStyle(_boldStyle);
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            _shadowStyle.contentOffset = Vector2.zero; // offset handled via rect
        }
    }

    private void ShadowLabel(string text)
    {
        // Draw shadow using layout to allocate a line
        GUILayout.Label(text, _shadowStyle);
        Rect r = GUILayoutUtility.GetLastRect();

        // Draw the main text 2px to the right and 2px up
        r.x += 2f;
        r.y -= 2f;
        GUI.Label(r, text, _boldStyle);
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (umaGenerator == null)
        {
            umaGenerator = (UMAAssetIndexer.Instance != null) ? UMAAssetIndexer.Instance.Generator : null;
        }

        if (umaGenerator != null)
        {
            ShadowLabel("Generation Metrics");
            long elapsedMs = umaGenerator.ElapsedTicks / 10000; // ticks -> ms
            ShadowLabel($"Elapsed Time: {elapsedMs} ms");
            ShadowLabel($"Pending UMAs: {umaGenerator.pendingUmas}");
            ShadowLabel($"Shape Dirty: {umaGenerator.DnaChanged}");
            ShadowLabel($"Texture Dirty: {umaGenerator.TextureChanged}");
            ShadowLabel($"Mesh Dirty: {umaGenerator.SlotsChanged}");

            if (umaGenerator.convertRenderTexture)
            {
                GUILayout.Space(8);
                ShadowLabel("Texture Metrics");
                ShadowLabel($"Textures Processed: {umaGenerator.TexturesProcessed}");
                ShadowLabel($"Copies Enqueued: {RenderTexToCPU.copiesEnqueued}");
                ShadowLabel($"Copies Dequeued: {RenderTexToCPU.copiesDequeued}");
                ShadowLabel($"Unable to Queue: {RenderTexToCPU.unableToQueue}");
                ShadowLabel($"Missed Uploads: {RenderTexToCPU.misseduploads}");
                ShadowLabel($"Error Uploads: {RenderTexToCPU.errorUploads}");
                ShadowLabel($"Textures Uploaded: {RenderTexToCPU.texturesUploaded}");

                GUILayout.Space(8);
                ShadowLabel("RenderTextures Cleaned");
                ShadowLabel($"UMAData Cleanup: {RenderTexToCPU.renderTexturesCleanedUMAData}");
                ShadowLabel($"Applied Cleanup: {RenderTexToCPU.renderTexturesCleanedApplied}");
                ShadowLabel($"Not Applied Cleanup: {RenderTexToCPU.renderTexturesCleanedMissed}");
                int totalCleanup = RenderTexToCPU.renderTexturesCleanedUMAData
                                 + RenderTexToCPU.renderTexturesCleanedApplied
                                 + RenderTexToCPU.renderTexturesCleanedMissed;
                ShadowLabel($"Total Cleanup: {totalCleanup}");
            }
        }
        else
        {
            ShadowLabel("UMA Generator not found.");
        }
    }
}
