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
			if (dnaRange.dnaConverter != null) {
				dnaSource = dnaRange.dnaConverter.DNAType.GetConstructor (System.Type.EmptyTypes).Invoke (null) as UMADnaBase;
				if (dnaSource != null)
				{
					if (dnaRange.dnaConverter.DNAType == typeof(DynamicUMADna)) {
						entryCount = ((DynamicDNAConverterBehaviourBase)dnaRange.dnaConverter).dnaAsset.Names.Length;
					} else {
						entryCount = dnaSource.Count;
					}
				}
			}
		}

	    public override void OnInspectorGUI()
	    {
			bool dirty = false;

			DnaConverterBehaviour newSource = EditorGUILayout.ObjectField("DNA Converter", dnaRange.dnaConverter, typeof(DnaConverterBehaviour), true) as DnaConverterBehaviour;

			if (newSource != dnaRange.dnaConverter)
			{
				dnaRange.dnaConverter = newSource;
				dnaSource = null;
				if (dnaRange.dnaConverter != null)
				{
					dnaSource = dnaRange.dnaConverter.DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
				}
				if (dnaSource == null)
				{
					entryCount = 0;
				}
				else
				{
					if (dnaRange.dnaConverter.DNAType == typeof(DynamicUMADna))
					{
						entryCount = ((DynamicDNAConverterBehaviourBase)dnaRange.dnaConverter).dnaAsset.Names.Length;
					}
					else
					{
						entryCount = dnaSource.Count;
					}
				}		
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
					dnaNames = ((DynamicDNAConverterBehaviourBase)dnaRange.dnaConverter).dnaAsset.Names;
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