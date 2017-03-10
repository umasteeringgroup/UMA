using UnityEngine;
using UMA;
using UnityEngine.Networking;
using System.Collections;
using System;

namespace UMA.Examples
{
    public class PlayerSetup : NetworkBehaviour
    {
        public RuntimeAnimatorController animController;

        private GameObject thisUMA;

        //When local player GameObject starts, this gets called
        public override void OnStartLocalPlayer()
        {
            //Send command to server that UMA should be created with this recipe string we saved before.
            //USe netId to generate a unique name for our player.
            CmdCreateChar(MenuScript.umaString, netId.Value);
        }

        //Just relay the UMA creation command to all clients
        [Command]
        void CmdCreateChar(string savedPlayer, uint id)
        {
            RpcCreateChar(savedPlayer, id);
        }

        //Create UMA
        [ClientRpc]
        void RpcCreateChar(string savedPlayer, uint id)
        {
            //Give it a unique name
            gameObject.name = "Player|" + netId;

            ///Setup the UMA basics
            thisUMA = new GameObject("Player");
            UMADynamicAvatar dynamicAvatar = thisUMA.AddComponent<UMADynamicAvatar>();
            dynamicAvatar.Initialize();

            //IMPORTANT to set this up before loading!
            dynamicAvatar.animationController = animController;

            //Load our UMA based on the string we sent
            var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
            asset.recipeString = savedPlayer;
            dynamicAvatar.Load(asset);

            //Set upp correct transforms and parent the UMA to our player object.
            thisUMA.transform.position = Vector3.zero;
            thisUMA.transform.rotation = transform.rotation;
            thisUMA.transform.SetParent(transform);

            //Set up callback to be called when creation is done
            thisUMA.GetComponent<UMAData>().OnCharacterCreated += UMAFinished;
        }

        private void UMAFinished(UMAData data)
        {
            //Set up Character controller
            UMADynamicAvatar dynamicAvatar = thisUMA.GetComponent<UMADynamicAvatar>();
            UMAData umaData = dynamicAvatar.umaData;
            CharacterController chc = thisUMA.AddComponent<CharacterController>();
            chc.radius = umaData.characterRadius;
            chc.height = umaData.characterHeight;
            chc.center = new Vector3(0, chc.height / 2.0f, 0);

            //Enable Player controller
            GameObject basePlayer = gameObject;
            basePlayer.GetComponent<PlayerController>().enabled = true;

            //Setup player camera
            GameObject cam = (GameObject)Instantiate(Resources.Load("PlayerCam"));
            cam.transform.SetParent(thisUMA.transform);
        }
    }
}
