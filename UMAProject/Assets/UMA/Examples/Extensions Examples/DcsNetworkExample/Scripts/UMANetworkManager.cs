using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UMA;
using UMA.CharacterSystem;

public class UMANetworkManager : NetworkManager
{
    /*public override void OnServerAddPlayer(NetworkConnection conn, short playerControllerId)
    {
        var player = (GameObject)GameObject.Instantiate(playerPrefab, GetStartPosition().position, GetStartPosition().rotation);
        NetworkServer.AddPlayerForConnection(conn, player, playerControllerId);
    }*/
}
