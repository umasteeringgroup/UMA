using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UMA.TexturePaint;
using UMA.TexturePaint.Editor;

public static class ArmorStampSpriteSetExample
{
    public const string Folder = "Assets/UMA/OverlayPainterExamples/Armor Stamps";
    public const string AssetPath = Folder + "/OPS Armor Stamps Sprite Set.asset";

    [MenuItem("UMA/Overlay Painter/Examples/Create Armor Stamp Sprite Set")]
    public static void Create()
    {
        var sprites = Directory.GetFiles(Folder, "OPS-*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase)
            .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p.Replace('\\','/')).OfType<Sprite>()).ToList();
        if (sprites.Count == 0) throw new System.InvalidOperationException("Install the Armor Stamps examples with their Sprite import settings first.");
        var set = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(AssetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            AssetDatabase.CreateAsset(set, AssetPath);
        }
        else Undo.RecordObject(set, "Rebuild Armor Stamp Sprite Set");
        set.setName = "OPS Armor Stamps and Corners";
        set.categories = new[] { OverlayPainterSpriteSet.Projection, OverlayPainterSpriteSet.Path };
        set.spriteNames = sprites.Select(s => s.name).ToList();
        set.spriteSheets = new() { new OverlayPainterSpriteSheet { channel = TexturePaintChannel.NormalControl, sprites = sprites } };
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssetIfDirty(set);
        SpriteMaterialThumbnails.Enqueue(set, true);
    }
}
