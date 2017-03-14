using UnityEngine;
using System.Collections;

namespace UMA
{
    public interface INameProvider
    {
        string GetAssetName();
        int GetNameHash();
    }
}
