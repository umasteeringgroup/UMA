using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA.CharacterSystem
{
	public class DNAPanel : MonoBehaviour
	{
		public List<string> Markers = new List<string>();
		//private CharacterAvatar Avatar;
		public GameObject DnaEditor;
		public Vector3 InitialPos;
		public float YSpacing;
		public bool InvertMarkers;
		public List<GameObject> CreatedObjects = new List<GameObject>();
		public RectTransform ContentArea;

		public class DNAHolder : IComparable<DNAHolder>
		{
			public string name;
			public float  value;
			public int    index;
			public UMADnaBase    dnaBase;
			public DNAHolder(string Name, float Value, int Index, UMADnaBase DNABase)
			{
				name = Name;
				value = Value;
				index = Index;
				dnaBase = DNABase;
			}

			#region IComparable implementation
			public int CompareTo (DNAHolder other)
			{
				return string.Compare(name,other.name);
			}
			#endregion
		}

	public void Initialize (DynamicCharacterAvatar Avatar) 
		{

            for (int i = 0; i < CreatedObjects.Count; i++)
            {
                GameObject go = CreatedObjects[i];
                UMAUtils.DestroySceneObject(go);
            }

            CreatedObjects.Clear();

			UMADnaBase[] DNA = Avatar.GetAllDNA();

			List<DNAHolder> ValidDNA = new List<DNAHolder>();

            for (int i1 = 0; i1 < DNA.Length; i1++)
			{
                UMADnaBase d = DNA[i1];
                string[] names = d.Names;
				float[] values = d.Values;

				for (int i=0;i<names.Length;i++)
				{
					string name = names[i];
					if (IsThisCategory(name.ToLower()))
					{
						ValidDNA.Add(new DNAHolder(name,values[i],i,d));
					}
				}

			}

			ValidDNA.Sort( );

            for (int i = 0; i < ValidDNA.Count; i++)
			{
                DNAHolder dna = ValidDNA[i];
                GameObject go = GameObject.Instantiate(DnaEditor);
				go.transform.SetParent(ContentArea.transform);
				go.transform.localScale = new Vector3(1f, 1f, 1f);//Set the scale back to 1
				DNAEditor de = go.GetComponentInChildren<DNAEditor>();
			de.Initialize(dna.name.BreakupCamelCase(),dna.index,dna.dnaBase,Avatar,dna.value);
				go.SetActive(true);
				CreatedObjects.Add(go);
			}
		}

		bool IsThisCategory(string name)
		{
			bool retval = false;

            for (int i = 0; i < Markers.Count; i++)
			{
                string s = Markers[i];
                if (name.Contains(s))
				{
					retval = true;
					break;
				}
			}
			if (InvertMarkers)
            {
                return !retval;
            }
            else
            {
                return retval;
            }
        }
	}
}
