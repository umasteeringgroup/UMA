using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA.XNode;

public class TitledNode : Node, ITitleSupplier, IContentSupplier
{
    public virtual string GetTitle()
    {
        return this.GetType().Name.Replace("Node", "");
    }

#if UNITY_EDITOR
    public virtual void OnGUI()
    {
        return;
    }
#endif
}
