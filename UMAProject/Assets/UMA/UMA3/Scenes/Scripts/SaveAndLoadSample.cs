using UMA.CharacterSystem;
using UMA;
using UnityEngine;
using UnityEngine.UI;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;

namespace UMA
{
    public class SaveAndLoadSample : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public UMARandomAvatar Randomizer;
        public Button LoadButton;
        public bool useAvatarDefinition;
        public bool useCompressedString;

        public string avatarString;
        public string compressedString;
        public int saveStringSize;
        public int avatarStringSize;
        public int compressedStringSize;
        public int asciiStringSize;
        public int binarySize;

        [System.NonSerialized]
        public Stack<string> avatarDefinitionQueue = new Stack<string>();
        public string lastAvatarDefinition;


        public void Start()
        {
            // Cache the initial avatar definition for inspector display only.
            // The queue is populated by GenerateANewUMA, which pushes the current
            // avatar before randomizing — this avoids a duplicate initial entry.
            lastAvatarDefinition = Avatar.GetAvatarDefinitionString(false);
        }

        public void GenerateANewUMA()
        {
            // save the current one to the queue before generating a new one
            lastAvatarDefinition = Avatar.GetAvatarDefinitionString(false);
            avatarDefinitionQueue.Push(lastAvatarDefinition);

            Randomizer.Randomize(Avatar);
            Avatar.BuildCharacter(false);
        }

        public void GoBack()
        {
            if (avatarDefinitionQueue.Count == 0)
            {
                return;
            }            
            Debug.Log("Loading previous avatar definition from queue.");
            lastAvatarDefinition = avatarDefinitionQueue.Pop();
            Avatar.LoadAvatarDefinition(lastAvatarDefinition);
            Avatar.BuildCharacter(false);
        }

        public void SaveUMA()
        {
            avatarString = Avatar.GetAvatarDefinitionString(true);
            compressedString = Avatar.GetAvatarDefinition(true).ToCompressedString("|");
            asciiStringSize = Avatar.GetAvatarDefinition(true).ToASCIIString().Length;

            binarySize = BinaryDefinition.ToBinary(new BinaryFormatter(), Avatar.GetAvatarDefinition(true)).Length;
            avatarStringSize = avatarString.Length * 2;
            compressedStringSize = compressedString.Length * 2; // utf-16

            LoadButton.interactable = true;
        }

        public void LoadUMA()
        {

            if (useCompressedString)
            {
                AvatarDefinition adf = AvatarDefinition.FromCompressedString(compressedString, '|');
                Avatar.LoadAvatarDefinition(adf);
                Avatar.BuildCharacter(false); // don't restore old DNA...
            }
            else
            {
                Avatar.LoadAvatarDefinition(avatarString);
                Avatar.BuildCharacter(false); // We must not restore the old DNA
            }
        }
    }
}
