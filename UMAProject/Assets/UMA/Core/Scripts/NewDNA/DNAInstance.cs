#define LEGACY_DNA_ENABLED
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    [System.Serializable]
    public class DNAInstance
    {
        public string Name;
        public float Value;
        public bool enabled = true;

        public DNAInstance Clone()
        {
            return new DNAInstance(Name, Value) { enabled = this.enabled };
        }

        public DNAInstance(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }
}
