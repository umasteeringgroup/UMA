using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UMAGenericPopupChoice
{
    public GUIContent Content;
    public event System.Action OnClick;
    public bool isSeperator = false;
    public UMAGenericPopupChoice(GUIContent content, System.Action onClick)
    {
        Content = content;
        OnClick = onClick;
        isSeperator = false;
    }

    public UMAGenericPopupChoice()
    {
        this.isSeperator = true;
    }

    public void FireEvent()
    {
        if (OnClick != null)
        {
            OnClick();
        }
    }
}
