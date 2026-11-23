using System;
using UMA;
using UnityEngine;

namespace UMA
{

    [Serializable]
    public class DynamicExpression
    {
        public string Name;
        public DNA ExpressionDNA;
        public DNAInstance ExpressionValue;
    }
}
