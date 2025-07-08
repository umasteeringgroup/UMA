#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;


// this class is only used to save/load curves in the editor. 
public class DNACurve : ScriptableObject 
{
    public AnimationCurve Curve = new AnimationCurve();
    public float minMapping = 0.0f; // The minimum value to map. This will be the base value when the adjusted input is 0.
    public float maxMapping = 1.0f; // The maximum value to map. This will be the maximum value when the adjusted input is 1.
    public string Description;

    [UnityEditor.MenuItem("Assets/Create/UMA/DNA/DNA Curve Mapping")]
    public static void CreateDNA()
    {
        UMA.CustomAssetUtility.CreateAsset<DNACurve>();
    }
}
#endif
