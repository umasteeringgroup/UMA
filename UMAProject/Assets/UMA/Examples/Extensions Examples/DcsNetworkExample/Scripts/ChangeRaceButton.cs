using UnityEngine;
using System.Collections;
using UMA.CharacterSystem;

public class ChangeRaceButton : MonoBehaviour 
{
    public DynamicCharacterAvatar avatar
    {
        get { return _avatar; }
        set { _avatar = value; Initialize(); }
    }
    private DynamicCharacterAvatar _avatar;

    private void Initialize()
    {
        if (_avatar == null)
            return;
    }

    public void changeRace()
    {
        if (avatar == null)
            return;

        if (avatar.activeRace.name == "HumanMale")
            avatar.GetComponent<NetworkDCA>().CmdSetRace("HumanFemale");
        else
            avatar.GetComponent<NetworkDCA>().CmdSetRace("HumanMale");
    }
}
