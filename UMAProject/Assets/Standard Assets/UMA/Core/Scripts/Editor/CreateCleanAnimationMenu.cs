#if UNITY_EDITOR
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
				var newClip = Instantiate(anim) as AnimationClip;
				newClip.ClearCurves();
				var bindings = AnimationUtility.GetCurveBindings(anim);
				foreach (var binding in bindings)
				{
					if (!binding.propertyName.StartsWith("m_LocalScale") && !binding.propertyName.StartsWith("m_LocalPosition"))
					{
						AnimationUtility.SetEditorCurve(newClip, binding, AnimationUtility.GetEditorCurve(anim, binding));
					}
				}


				var oldPath = AssetDatabase.GetAssetPath(anim);
				var folder = System.IO.Path.GetDirectoryName(oldPath);

				AssetDatabase.CreateAsset(newClip, AssetDatabase.GenerateUniqueAssetPath( folder + "/" + anim.name + ".anim"));
			}
			AssetDatabase.SaveAssets();
		}
	}

	[MenuItem("UMA/Create Non-Scale Animation")]
	static void CreateNonScaleAniamtionMenuItem()
	{
		foreach (var obj in Selection.objects)
		{
			var anim = obj as AnimationClip;
			if (anim != null)
			{
				var newClip = Instantiate(anim) as AnimationClip;
				newClip.ClearCurves();
				var bindings = AnimationUtility.GetCurveBindings(anim);
				foreach (var binding in bindings)
				{
					if (!binding.propertyName.StartsWith("m_LocalScale"))
					{
						AnimationUtility.SetEditorCurve(newClip, binding, AnimationUtility.GetEditorCurve(anim, binding));
					}
				}


				var oldPath = AssetDatabase.GetAssetPath(anim);
				var folder = System.IO.Path.GetDirectoryName(oldPath);

				AssetDatabase.CreateAsset(newClip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + anim.name + ".anim"));
			}
			AssetDatabase.SaveAssets();
		}
	}
}
#endif