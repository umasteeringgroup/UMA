using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FloatToPercentTxt : MonoBehaviour {
    public Text text;
    public void setText(float percent)
    {
        text.text = (Mathf.Round(percent * 100)).ToString() + "%";
    }
}
