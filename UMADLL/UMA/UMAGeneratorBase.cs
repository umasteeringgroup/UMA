using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
    public abstract class UMAGeneratorBase : MonoBehaviour
    {
        public string[] textureNameList;
        public abstract void addDirtyUMA(UMAData umaToAdd);
    }
}
