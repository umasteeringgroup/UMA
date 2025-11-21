//	============================================================
//	Name:		ExpressionPlayerInspector
//	Author: 	Eli Curtz
//	Copyright:	(c) 2014 Eli Curtz
//	============================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UMA.Editors;

namespace UMA.PoseTools
{
	[CustomEditor(typeof(ExpressionPlayer), true)]
	public class ExpressionPlayerInspector : Editor
	{
		private ExpressionPlayer player;
        private UMAExpressionPlayer umaPlayer;
        private UMAExpressionSet expressionSet;
        private BonePoseConversionWindow converterWindow;
        private Vector2 _exprScroll;
        private bool _showPosePairs = true; // toggle

        // Cache list of expression property names (matches field names in ExpressionPlayer)
        private static readonly HashSet<string> ExpressionPropertyNames = new HashSet<string>(ExpressionPlayer.PoseNames)
        {
            // Additional hand pose names already included in PoseNames
        };
        // Also skip the expressionSet field (handled indirectly by pose pairs)
        private static readonly string ExpressionSetFieldName = "expressionSet";

		public void OnEnable()
		{
			player = target as ExpressionPlayer;
            umaPlayer = player as UMAExpressionPlayer;
            if (umaPlayer != null)
            {
                expressionSet = umaPlayer.expressionSet;
            }
		}

		public override void OnInspectorGUI()
		{
            serializedObject.Update();
            DrawNonExpressionProperties();
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            DrawHeaderTools();
            DrawExpressionsTable();
		}

        // Draw all serialized properties except those we render manually in the expression table
        private void DrawNonExpressionProperties()
        {
            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                // Skip script reference
                if (prop.name == "m_Script") { EditorGUI.BeginDisabledGroup(true); EditorGUILayout.PropertyField(prop, true); EditorGUI.EndDisabledGroup(); continue; }
                // Skip expression floats and expressionSet
                if (ExpressionPropertyNames.Contains(prop.name) || prop.name == ExpressionSetFieldName)
                {
                    continue;
                }
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        private void DrawHeaderTools()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Expression", GUILayout.Width(140)))
            {
                Undo.RecordObject(player, "Reset Expression");
                float[] zeroes = new float[player.Values.Length];
                player.Values = zeroes;
                EditorUtility.SetDirty(player);
                TrySimulateOnce(player);
            }
            if (GUILayout.Button("Save To Clip", GUILayout.Width(120)))
            {
                string assetPath = EditorUtility.SaveFilePanelInProject("Save Expression Clip", "Expression", "anim", null);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    player.SaveExpressionClip(assetPath);
                }
            }
            EditorGUI.BeginDisabledGroup(expressionSet == null);
            if (GUILayout.Button("Edit Expression Set", GUILayout.Width(160)))
            {
                if (expressionSet == null && umaPlayer != null)
                {
                    expressionSet = umaPlayer.expressionSet;
                }
                if (expressionSet != null)
                {
                    EditorUtility.OpenPropertyEditor(expressionSet);
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            _showPosePairs = EditorGUILayout.ToggleLeft("Show Pose Pairs", _showPosePairs, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DrawExpressionsTable()
        {
            if (player == null)
            {
                EditorGUILayout.HelpBox("No player.", MessageType.Info); return;
            }
            if (umaPlayer == null || expressionSet == null || expressionSet.posePairs == null || expressionSet.posePairs.Length != ExpressionPlayer.PoseCount)
            {
                EditorGUILayout.HelpBox("UMAExpressionSet not assigned or invalid. Assign on UMAExpressionPlayer.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("Pose", GUILayout.Width(140));
            GUILayout.Label("Value", GUILayout.Width(_showPosePairs ? 60 : 200));
            if (_showPosePairs)
            {
                GUILayout.Label("Primary", GUILayout.Width(160));
                GUILayout.Label("Inverse", GUILayout.Width(160));
            }
            GUILayout.Label("Edit", GUILayout.Width(50));
            GUILayout.Label("Convert", GUILayout.Width(65));
            EditorGUILayout.EndHorizontal();

            _exprScroll = EditorGUILayout.BeginScrollView(_exprScroll, GUILayout.Height(400));
            float[] vals = player.Values;
            for (int i = 0; i < ExpressionPlayer.PoseCount; i++)
            {
                var pair = expressionSet.posePairs[i];
                if (pair == null) { pair = expressionSet.posePairs[i] = new UMAExpressionSet.PosePair(); }
                string primaryName = ExpressionPlayer.PrimaryPoseName(i) ?? ExpressionPlayer.PoseNames[i];

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(primaryName, GUILayout.Width(140));
                EditorGUI.BeginChangeCheck();
                float newVal = EditorGUILayout.Slider(vals[i], -1f, 1f, GUILayout.Width(_showPosePairs ? 160 : 200));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(player, "Change Expression Value");
                    vals[i] = newVal;
                    player.Values = vals;
                    EditorUtility.SetDirty(player);
                    TrySimulateOnce(player);
                }
                if (_showPosePairs)
                {
                    EditorGUI.BeginChangeCheck();
                    var newPrimary = EditorGUILayout.ObjectField(pair.primary, typeof(UMABonePose), false, GUILayout.Width(160)) as UMABonePose;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(expressionSet, "Assign Primary Pose");
                        pair.primary = newPrimary;
                        EditorUtility.SetDirty(expressionSet);
                    }
                    EditorGUI.BeginChangeCheck();
                    var newInverse = EditorGUILayout.ObjectField(pair.inverse, typeof(UMABonePose), false, GUILayout.Width(160)) as UMABonePose;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(expressionSet, "Assign Inverse Pose");
                        pair.inverse = newInverse;
                        EditorUtility.SetDirty(expressionSet);
                    }
                }
                bool hasEditablePose = pair.primary != null || pair.inverse != null;
                EditorGUI.BeginDisabledGroup(!hasEditablePose);
                if (GUILayout.Button("Edit", GUILayout.Width(50)))
                {
                    var poseToEdit = pair.primary != null ? pair.primary : pair.inverse;
                    if (poseToEdit != null)
                    {
                        Selection.activeObject = poseToEdit;
                        EditorGUIUtility.PingObject(poseToEdit);
                    }
                }
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Convert", GUILayout.Width(65)))
                {
                    QueueForConversion(pair);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void QueueForConversion(UMAExpressionSet.PosePair pair)
        {
            converterWindow = Resources.FindObjectsOfTypeAll<BonePoseConversionWindow>()?.Length > 0
                ? Resources.FindObjectsOfTypeAll<BonePoseConversionWindow>()[0]
                : BonePoseConversionWindow.GetWindow<BonePoseConversionWindow>();
            if (converterWindow == null)
            {
                converterWindow = BonePoseConversionWindow.GetWindow<BonePoseConversionWindow>();
            }
            var field = typeof(BonePoseConversionWindow).GetField("_queuedPoses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var list = field.GetValue(converterWindow) as IList<UMABonePose>;
                if (list != null)
                {
                    list.Clear();
                    if (pair.primary != null) list.Add(pair.primary);
                    if (pair.inverse != null) list.Add(pair.inverse);
                }
            }
            converterWindow.Repaint();
            EditorWindow.FocusWindowIfItsOpen(typeof(BonePoseConversionWindow));
        }

        private static void TrySimulateOnce(ExpressionPlayer p)
        {
            var umaPlayer = p as UMAExpressionPlayer;
            if (umaPlayer != null)
            {
                umaPlayer.EditorSimulateOnce();
            }
            else
            {
                SceneView.RepaintAll();
            }
        }

		[MenuItem("UMA/Pose Tools/Set Clip Generic", true, priority = 1)]
		static bool ValidateSetClipGeneric()
		{
			Object[] objs = Selection.objects;
			if ((objs == null) || (objs.Length < 1)) return false;
			bool hasLegacyClip = false;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null && clip.legacy) { hasLegacyClip = true; break; }
			}
			return hasLegacyClip;
		}

		[MenuItem("UMA/Pose Tools/Set Clip Generic",priority =1)]
		static void SetClipGeneric()
		{
			Object[] objs = Selection.objects;
			if (objs == null) return;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null) { clip.legacy = false; }
			}
		}

		[MenuItem("UMA/Pose Tools/Set Clip Legacy", true, priority = 1)]
		static bool ValidateSetClipLegacy()
		{
			Object[] objs = Selection.objects;
			if ((objs == null) || (objs.Length < 1)) return false;
			bool hasGenericClip = false;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null && !clip.legacy && !clip.humanMotion) { hasGenericClip = true; break; }
			}
			return hasGenericClip;
		}

		[MenuItem("UMA/Pose Tools/Set Clip Legacy", priority = 1)]
		static void SetClipLegacy()
		{
			Object[] objs = Selection.objects;
			if (objs == null) return;
			foreach (Object obj in objs)
			{
				AnimationClip clip = obj as AnimationClip;
				if (clip != null) { clip.legacy = true; }
			}
		}
	}
}
#endif