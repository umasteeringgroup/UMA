using UnityEngine;
using System.Collections;

public class DnaConverterBehaviour : MonoBehaviour 
{
    public System.Type DNAType;
    public System.Action<UMAData> ApplyDnaAction;
    public void ApplyDna(UMAData data)
    {
        ApplyDnaAction(data);
    }
}