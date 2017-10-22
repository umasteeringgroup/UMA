using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using UMA;
using UMA.CharacterSystem;

public class SetNetworkColor : MonoBehaviour 
{
    public DynamicCharacterAvatar avatar;

    public string ColorType;
    public Button _button;

    void Start()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClick);
    }

    public void OnClick()
    {
        if (avatar == null)
            return;

        if(ColorType == "Skin")
            avatar.GetComponent<NetworkDCA>().CmdSetSkinColor( _button.image.color );

        if(ColorType == "Hair")
            avatar.GetComponent<NetworkDCA>().CmdSetHairColor( _button.image.color );
    }
}
