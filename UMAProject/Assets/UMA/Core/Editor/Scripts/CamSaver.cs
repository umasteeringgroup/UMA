using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace UMA
{
    [Serializable]
    public class CamSaver
    {
        public Vector3 position;
        public Quaternion rotation;
        
        public CamSaver(Transform t)
        {
            position = t.position;
            rotation = t.localRotation;
        }

        public override string ToString()
        {
            return JsonUtility.ToJson(this);
        }

        public static CamSaver FromString(string s)
        {
           return JsonUtility.FromJson<CamSaver>(s);
        }
    }
}
