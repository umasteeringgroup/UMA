using UnityEngine;
using System.Collections.Generic;
using UMACharacterSystem;
using UMA;
using UnityEngine.UI;

public class AvailableColorsHandler : MonoBehaviour
{
    public DynamicCharacterAvatar Avatar;

    List<OverlayColorData> Colors = new List<OverlayColorData>();
    public GameObject ColorPanel;
    public GameObject ColorButtonPrefab;
    public string ColorName;
    public GameObject LabelPrefab;

    public void Setup(DynamicCharacterAvatar avatar, string colorName, GameObject colorPanel)
    {
        ColorName = colorName;
        Avatar = avatar;
        ColorPanel = colorPanel;
        Colors.Add(GetColor(Color.white, Color.white));
        Colors.Add(GetColor(Color.red, Color.white));
        Colors.Add(GetColor(Color.yellow, Color.white));
        Colors.Add(GetColor(Color.magenta, Color.white));
        Colors.Add(GetColor(Color.grey, Color.white));
        Colors.Add(GetColor(Color.green, Color.white));
        Colors.Add(GetColor(Color.cyan, Color.white));
        Colors.Add(GetColor(Color.blue, Color.white));
        Colors.Add(GetColor(Color.black, Color.white));
    }

    public OverlayColorData GetColor(Color c, Color additive)
    {
        OverlayColorData ocd = new OverlayColorData(3);
        ocd.channelMask[0] = c;
        ocd.channelAdditiveMask[0] = additive;
        return ocd;
    }

    public void OnClick()
    {
        Cleanup();

        AddLabel(ColorName);
        foreach(OverlayColorData ocd in Colors)
        {
            AddButton(ocd);
        }
    }

    private void AddLabel(string theText)
    {
        GameObject go = GameObject.Instantiate(LabelPrefab);
        go.transform.SetParent(ColorPanel.transform);
        Text txt = go.GetComponentInChildren<Text>();
        txt.text = theText;
    }

    private void AddButton(OverlayColorData ocd)
    {
        GameObject go = GameObject.Instantiate(ColorButtonPrefab);
        ColorHandler ch = go.GetComponent<ColorHandler>();
        ch.Setup(Avatar,ColorName, ocd );
        Image i = go.GetComponent<Image>();
        i.color = ocd.color;
        go.transform.SetParent(ColorPanel.transform);
    }

    private void Cleanup()
    {
        foreach (Transform t in ColorPanel.transform)
        {
            GameObject.Destroy(t.gameObject);
        }
    }
}
