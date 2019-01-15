#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(DNARangeAsset))]
	public class DNARangeInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA/DNA/DNA Range Asset")]
	    public static void CreateOverlayMenuItem()
	    {
			CustomAssetUtility.CreateAsset<DNARangeAsset>();
	    }

		private DNARangeAsset dnaRange;
		private UMADnaBase dnaSource;

		private int entryCount = 0;
		
		public void OnEnable()
		{
			dnaRange = target as DNARangeAsset;
			GetEntryCount();
		}
		//UMA 2.8 FixDNAPrefabs:multiUse function for getting this
		private void GetEntryCount()
		{
			if (dnaRange.dnaConverter != null)
			{
				if (dnaRange.dnaConverter.DNAType == typeof(DynamicUMADna))
				{
					entryCount = ((IDynamicDNAConverter)dnaRange.dnaConverter).dnaAsset.Names.Length;
				}
				else
				{
					dnaSource = dnaRange.dnaConverter.DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
					if (dnaSource != null)
					{

						entryCount = dnaSource.Count;
					}
				}
			}
			else
			{
				entryCount = 0;
			}
		}

	    public override void OnInspectorGUI()
	    {
			bool dirty = false;

			var currentSource = dnaRange.dnaConverter;
			IDNAConverter newSource = currentSource;

			var converterProp = serializedObject.FindProperty("_dnaConverter");

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(converterProp);
			if (EditorGUI.EndChangeCheck())
			{
				var converterFieldProp = converterProp.FindPropertyRelative("_converter");
				if(converterFieldProp.objectReferenceValue != null)
					newSource = converterFieldProp.objectReferenceValue as IDNAConverter;
				GetEntryCount();
				serializedObject.ApplyModifiedProperties();
			}
			//UMA 2.8 FixDNAPrefabs:Use the propertyField
			//newSource = EditorGUILayout.ObjectField("DNA Converter", dnaRange.dnaConverter, typeof(DnaConverterBehaviour), true) as DnaConverterBehaviour;

			if (currentSource != newSource)
			{
				dnaRange.dnaConverter = newSource;
				dnaSource = null;
				//UMA 2.8 FixDNAPrefabs:Done in GetEntryCount
				/*if (dnaRange.dnaConverter != null)
				{
					if (dnaRange.dnaConverter.DNAType == typeof(DynamicUMADna))
					{
						entryCount = ((IDynamicDNAConverter)dnaRange.dnaConverter).dnaAsset.Names.Length;
					}
					else
					{
						dnaSource = dnaRange.dnaConverter.DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
						if (dnaSource == null)
						{
							entryCount = 0;
						}
						else
						{
							entryCount = dnaSource.Count;
						}
					}
				}*/
				dnaRange.means = new float[entryCount];
				dnaRange.deviations = new float[entryCount];
				dnaRange.spreads = new float[entryCount];
				for (int i = 0; i < entryCount; i++)
				{
					dnaRange.means[i] = 0.5f;
					dnaRange.deviations[i] = 0.16f;
					dnaRange.spreads[i] = 0.5f;
				}

				dirty = true;
			}

			GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

			if (dnaRange.dnaConverter != null)
			{
				GUILayout.Space(2f);
				GUIStyle headerStyle = new GUIStyle();
				headerStyle.alignment = TextAnchor.MiddleCenter;
				headerStyle.normal.textColor = Color.white;
				headerStyle.fontSize =  12;
				EditorGUILayout.LabelField(dnaRange.dnaConverter.DNAType.Name, headerStyle); 

				string[] dnaNames;
				if (dnaRange.dnaConverter.DNAType == typeof(DynamicUMADna))
				{
					dnaNames = ((IDynamicDNAConverter)dnaRange.dnaConverter).dnaAsset.Names;
				}
				else
				{
					dnaNames = dnaSource.Names;
				}

				for (int i = 0; i < entryCount; i++)
				{
					float currentMin = dnaRange.means[i] - dnaRange.spreads[i];
					float currentMax = dnaRange.means[i] + dnaRange.spreads[i];
					float min = currentMin;
					float max = currentMax;
					EditorGUILayout.PrefixLabel(dnaNames[i]);
					EditorGUILayout.MinMaxSlider(ref min, ref max, 0f, 1f);
					if ((min != currentMin) || (max != currentMax))
					{
						dnaRange.means[i] = (min + max) / 2f;
						dnaRange.spreads[i] = (max - min) / 2f;
						dnaRange.deviations[i] = dnaRange.spreads[i] / 3f;
						dirty = true;
					}
				}
			}

			if (dirty)
			{
				EditorUtility.SetDirty(dnaRange);
				AssetDatabase.SaveAssets();
			}
		}  
	}
}
#endif