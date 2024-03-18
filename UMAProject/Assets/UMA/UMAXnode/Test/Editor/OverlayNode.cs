using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UMA.XNode;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateNodeMenu("BaseObjects/Overlay")]
public class OverlayNode : TitledNode {
	public OverlayDataAsset OverlayData;

    // [Input] public List<Texture2D> Textures;
	[Input] public List<OverlayColorData> Colors;
	[Input] public List<string> Tags;
	[Output] public OverlayNode Overlay;
    
    protected override void Init() {
		base.Init();
		
	}

	override public string GetTitle()
	{
		if (OverlayData != null)
			return "Overlay: " + OverlayData.overlayName;
		else
			return "Overlay: None";
    }

	public Texture GetFirstNonNullTexture()
	{
        if (OverlayData != null)
		{
			for (int i = 0; i< OverlayData.textureList.Length; i++)
			{
				if (OverlayData.textureList[i] != null)
				{
                          return OverlayData.textureList[i];     
				}
			}
        }
        return null;
    }
#if UNITY_EDITOR
    public override void OnGUI()
	{
		Texture texture = GetFirstNonNullTexture();
		if (texture != null)
		{
            EditorGUILayout.LabelField(new GUIContent(texture), GUILayout.Width(64), GUILayout.Height(64));
        }
    }
#endif

	// Return the correct value of an output port when requested
	public override object GetValue(NodePort port) {
		return this; // Replace this
	}
}