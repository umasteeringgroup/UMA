//	============================================================
//	Name:		ExpressionPlayerInspector
//	Author: 	Eli Curtz
//	Copyright:	(c) 2014 Eli Curtz
//	============================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.PoseTools
{
	[CustomEditor(typeof(ExpressionPlayer), true)]
	public class ExpressionPlayerInspector : Editor
	{
		private ExpressionPlayer player;

		public void OnEnable()
		{
			player = target as ExpressionPlayer;
		}

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			if (GUILayout.Button("Reset Expression"))
			{
				float[] zeroes = new float[player.Values.Length];
				player.Values = zeroes;
				EditorUtility.SetDirty(player);
				AssetDatabase.SaveAssets();
			}

			if (GUILayout.Button("Save To Clip"))
			{
				string assetPath = EditorUtility.SaveFilePanelInProject("Save Expression Clip", "Expression", "anim", null);
				player.SaveExpressionClip(assetPath);
			}
		}

		[MenuItem("UMA/Pose Tools/Set Clip Generic", true)]
		static bool ValidateSetClipGeneric()
		{

			Object[] objs = Selection.objects;
			if ((objs == null) || (objs.Length < 1)) return false;

			bool hasLegacyClip = false;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null)
				{
					if (clip.legacy)
					{
						hasLegacyClip = true; break;
					}
				}
			}
			return hasLegacyClip;
		}

		[MenuItem("UMA/Pose Tools/Set Clip Generic")]
		static void SetClipGeneric()
		{
			Object[] objs = Selection.objects;
			if (objs == null) return;

			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null)
				{
					clip.legacy = false;
				}
			}
		}

		[MenuItem("UMA/Pose Tools/Set Clip Legacy", true)]
		static bool ValidateSetClipLegacy()
		{

			Object[] objs = Selection.objects;
			if ((objs == null) || (objs.Length < 1)) return false;

			bool hasGenericClip = false;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null)
				{
					if (!clip.legacy && !clip.humanMotion)
					{
						hasGenericClip = true; break;
					}
				}
			}

			return hasGenericClip;
		}

		[MenuItem("UMA/Pose Tools/Set Clip Legacy")]
		static void SetClipLegacy()
		{
			Object[] objs = Selection.objects;
			if (objs == null) return;

			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null)
				{
					clip.legacy = true;
				}
			}
		}
	}
}
#endif