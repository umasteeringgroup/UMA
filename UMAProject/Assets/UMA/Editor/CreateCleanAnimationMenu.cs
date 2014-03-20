using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class CreateCleanAnimationMenu : MonoBehaviour {

	[MenuItem("UMA/Create Clean Animation")]
	static void CreateCleanAniamtionMenuItem()
	{
		foreach(var obj in Selection.objects)
		{
			var anim = obj as AnimationClip;
			if (anim != null)
			{
				//var newClip = new AnimationClip();
				var newClip = Instantiate(anim) as AnimationClip;
				newClip.ClearCurves();
				AnimationUtility.SetAnimationType(newClip, ModelImporterAnimationType.Human);
				var curves = AnimationUtility.GetAllCurves(anim);
				foreach (var curve in curves)
				{
					if (!curve.propertyName.StartsWith("m_LocalScale") && !curve.propertyName.StartsWith("m_LocalPosition"))
					{
						newClip.SetCurve(curve.path, curve.type, curve.propertyName, curve.curve);
					}
				}


				var oldPath = AssetDatabase.GetAssetPath(anim);
				var folder = System.IO.Path.GetDirectoryName(oldPath);

				AssetDatabase.CreateAsset(newClip, AssetDatabase.GenerateUniqueAssetPath( folder + "/" + anim.name + ".anim"));
			}
			AssetDatabase.SaveAssets();
		}
	}

}
