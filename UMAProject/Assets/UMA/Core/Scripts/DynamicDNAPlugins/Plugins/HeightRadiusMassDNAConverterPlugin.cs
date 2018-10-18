using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
	public class HeightRadiusMassDNAConverterPlugin : DynamicDNAPlugin
	{

		[System.Serializable]
		public class DNASizeAdjustment
		{
			public DNAEvaluator dnaForScale;
			public DNAEvaluator dnaForRadius;
			public DNAEvaluator dnaForMass;
		}

		public List<DNASizeAdjustment> sizeAdjusters = new List<DNASizeAdjustment>();

		[Tooltip("Adjust Scale")]
		[SerializeField]
		private bool _adjustScale = true;
		[Tooltip("When Unity calculates the bounds it uses bounds based on the animation included when the mesh was exported. This can mean the bounds are not very 'tight' or go below the characters feet. Checking this will make the bounds tight to the characters head/feet. You can then use the 'BoundsAdjust' option below to add extra space.")]
		[SerializeField]
		private bool _adjustBounds = true;
		[SerializeField]
		private bool _adjustRadius = true;
		[SerializeField]
		private bool _adjustMass = true;

		[SerializeField]
		private float _scale = 1f;
		[SerializeField]
		[Tooltip("The bone used in the overall scale calculations. For rigs based on the standard UMA Rig, this is usually the 'Position' bone.")]
		private string _bone = "Position";
		[SerializeField]
		private int _scaleBoneHash = -1084586333;//hash of the Position Bone

		[Tooltip("You can pad or tighten your bounds with these controls")]
		[SerializeField]
		private Vector3 _boundsAdjust = Vector3.zero;
		
		[Tooltip("This is used to adjust the fitting of the collider.")]
		[SerializeField]
		private Vector2 _radiusAdjust = new Vector2(0.23f, 0);
		
		[Tooltip("This is used to adjust the characters mass.")]
		[SerializeField]
		private Vector3 _massAdjust = new Vector3(46f, 26f, 26f);

		//we want all that to be 4 lines high (1x scale 1x bounds 1x radius 1x mass)

		private Dictionary<string, List<int>> _indexesForDnaNames = new Dictionary<string, List<int>>();

		//This is wrong
		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				if (_indexesForDnaNames.Count == 0)
				{
					for (int i = 0; i < sizeAdjusters.Count; i++)
					{
						if (!string.IsNullOrEmpty(sizeAdjusters[i].dnaForScale.dnaName))
						{
							if (!_indexesForDnaNames.ContainsKey(sizeAdjusters[i].dnaForScale.dnaName))
								_indexesForDnaNames.Add(sizeAdjusters[i].dnaForScale.dnaName, new List<int>());
							_indexesForDnaNames[sizeAdjusters[i].dnaForScale.dnaName].Add(i);
						}
						if (!string.IsNullOrEmpty(sizeAdjusters[i].dnaForMass.dnaName))
						{
							if (!_indexesForDnaNames.ContainsKey(sizeAdjusters[i].dnaForMass.dnaName))
								_indexesForDnaNames.Add(sizeAdjusters[i].dnaForMass.dnaName, new List<int>());
							_indexesForDnaNames[sizeAdjusters[i].dnaForMass.dnaName].Add(i);
						}
						if (!string.IsNullOrEmpty(sizeAdjusters[i].dnaForRadius.dnaName))
						{
							if (!_indexesForDnaNames.ContainsKey(sizeAdjusters[i].dnaForRadius.dnaName))
								_indexesForDnaNames.Add(sizeAdjusters[i].dnaForRadius.dnaName, new List<int>());
							_indexesForDnaNames[sizeAdjusters[i].dnaForRadius.dnaName].Add(i);
						}
					}
				}
				return _indexesForDnaNames;
			}
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			
		}

#if UNITY_EDITOR
		//this is wrong
		public override void OnAddEntryCallback(SerializedObject pluginSO, int entryIndex)
		{
			var listProp = pluginSO.FindProperty("sizeAdjusters");
			var newEl = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
			newEl.FindPropertyRelative("heightRatio").floatValue = 1f;
			newEl.FindPropertyRelative("massRatio").floatValue = 1f;
			newEl.FindPropertyRelative("radiusRatio").floatValue = 1f;
			base.OnAddEntryCallback(pluginSO, entryIndex);
		}

		//MorphSet also needs to override the header output so it can show its startingPose and startingBlendShape fields
		//(although both of those are just Poses/Blendshapes with a default weight of 1 really)
		public override float GetListHeaderHeight
		{
			get
			{
				return ((EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2) * 4) + (EditorGUIUtility.standardVerticalSpacing *2);
			}
		}

		public override bool DrawElementsListHeaderContent(Rect rect, SerializedObject pluginSO)
		{
			EditorGUI.indentLevel++;
			var prevIndent = EditorGUI.indentLevel;
			rect = EditorGUI.IndentedRect(rect);
			EditorGUI.indentLevel = 0;
			var adjustScaleProp = pluginSO.FindProperty("_adjustScale");
			var adjustBoundsProp = pluginSO.FindProperty("_adjustBounds");
			var adjustRadiusProp = pluginSO.FindProperty("_adjustRadius");
			var adjustMassProp = pluginSO.FindProperty("_adjustMass");

			var scaleAdjustProp = pluginSO.FindProperty("_scale");
			var scaleBoneProp = pluginSO.FindProperty("_bone");
			var scaleBoneHashProp = pluginSO.FindProperty("_scaleBoneHash");

			var boundAdjustProp = pluginSO.FindProperty("_boundsAdjust");
			var radiusAdjustProp = pluginSO.FindProperty("_radiusAdjust");
			var massAdjustProp = pluginSO.FindProperty("_massAdjust");

			//overall rects
			var scaleRect = new Rect(rect.xMin, rect.yMin, rect.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2));
			var boundsRect = new Rect(rect.xMin, scaleRect.yMax, rect.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2));
			var radiusRect = new Rect(rect.xMin, boundsRect.yMax, rect.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2));
			var massRect = new Rect(rect.xMin, radiusRect.yMax, rect.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2));

			//labelsRects are all 120f wide or maybe  1/3rd or whatever unity makes them by standard
			var scaleLabelRect = new Rect(rect.xMin, scaleRect.yMin, (rect.width / 3), scaleRect.height);
			var boundsLabelRect = new Rect(rect.xMin, boundsRect.yMin, (rect.width / 3), scaleRect.height);
			var radiusLabelRect = new Rect(rect.xMin, radiusRect.yMin, (rect.width / 3), scaleRect.height);
			var massLabelRect = new Rect(rect.xMin, massRect.yMin, (rect.width / 3), scaleRect.height);

			//fieldsRects are whatever is left
			var scaleFieldsRect = new Rect(scaleLabelRect.xMax, scaleRect.yMin, (rect.width / 3) *2, scaleRect.height);
			var scaleFields1LabelRect = new Rect(scaleFieldsRect.xMin, scaleFieldsRect.yMin, 40f, EditorGUIUtility.singleLineHeight);
			var scaleFields1FieldRect = new Rect(scaleFields1LabelRect.xMax, scaleFieldsRect.yMin, (scaleFieldsRect.width / 3) - 40f, EditorGUIUtility.singleLineHeight);
			var scaleFields2LabelRect = new Rect(scaleFields1FieldRect.xMax, scaleFieldsRect.yMin, 40f, EditorGUIUtility.singleLineHeight);
			var scaleFields2FieldRect = new Rect(scaleFields2LabelRect.xMax, scaleFieldsRect.yMin, ((scaleFieldsRect.width / 3)*2) -40f, EditorGUIUtility.singleLineHeight);
			var boundsFieldsRect = new Rect(scaleLabelRect.xMax, boundsRect.yMin, (rect.width / 3) *2, scaleRect.height);
			var radiusFieldsRect = new Rect(scaleLabelRect.xMax, radiusRect.yMin, (rect.width / 3) *2, scaleRect.height);
			var massFieldsRect = new Rect(scaleLabelRect.xMax, massRect.yMin, (rect.width / 3) *2, scaleRect.height);

			//you loose the fucking tooltips when you do toggleLeft WTFF?!?
			var scaleLabel = EditorGUI.BeginProperty(scaleLabelRect, new GUIContent(adjustScaleProp.displayName), adjustScaleProp);
			adjustScaleProp.boolValue = EditorGUI.ToggleLeft(scaleLabelRect, scaleLabel, adjustScaleProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!adjustScaleProp.boolValue);
			//cant do this here either!
			//EditorGUI.PropertyField(scaleFields1LabelRect, scaleAdjustProp);
			//EditorGUI.PropertyField(scaleFields2LabelRect, scaleBoneProp);
			EditorGUI.LabelField(scaleFields1LabelRect, "Scale");
			scaleAdjustProp.floatValue = EditorGUI.FloatField(scaleFields1FieldRect, scaleAdjustProp.floatValue);
			EditorGUI.LabelField(scaleFields2LabelRect, "Bone");
			scaleBoneProp.stringValue = EditorGUI.TextField(scaleFields2FieldRect, scaleBoneProp.stringValue);
			EditorGUI.EndDisabledGroup();

			var boundsLabel = EditorGUI.BeginProperty(boundsLabelRect, new GUIContent(adjustBoundsProp.displayName), adjustBoundsProp);
			adjustBoundsProp.boolValue = EditorGUI.ToggleLeft(boundsLabelRect, boundsLabel, adjustBoundsProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!adjustBoundsProp.boolValue);
			EditorGUI.PropertyField(boundsFieldsRect, boundAdjustProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();

			var radiusLabel = EditorGUI.BeginProperty(radiusLabelRect, new GUIContent(adjustRadiusProp.displayName), adjustRadiusProp);
			adjustRadiusProp.boolValue = EditorGUI.ToggleLeft(radiusLabelRect, radiusLabel, adjustRadiusProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!adjustRadiusProp.boolValue);
			EditorGUI.PropertyField(radiusFieldsRect, radiusAdjustProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();

			var massLabel = EditorGUI.BeginProperty(massLabelRect, new GUIContent(adjustMassProp.displayName), adjustMassProp);
			adjustMassProp.boolValue = EditorGUI.ToggleLeft(massLabelRect, massLabel, adjustMassProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!adjustMassProp.boolValue);
			EditorGUI.PropertyField(massFieldsRect, massAdjustProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();

			EditorGUI.indentLevel = prevIndent;
			EditorGUI.indentLevel--;
			return false;
		}
#endif

	}
}
