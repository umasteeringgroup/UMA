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
        public DNAGroup parentGroup;

        public DNAInstance Clone()
        {
            return new DNAInstance(Name, Value, parentGroup) { enabled = this.enabled };
        }

        public DNAInstance(string name, float value, DNAGroup parentGroup)
        {
            Name = name;
            Value = value;
            this.parentGroup = parentGroup;
        }
    }
}
