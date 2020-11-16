using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;

public class SpeedCheck : MonoBehaviour
{
    // Start is called before the first frame update
    public UMAGeneratorBuiltin Generator;
    public Rect ScreenLoc;

    private void OnGUI()
    {
        int ms = Mathf.FloorToInt(Generator.ElapsedTicks / 10000.0f);
        GUI.Label(ScreenLoc,"Speed: " + ms+"ms");
    }
}
