using UMA.CharacterSystem;
using UMA;
using UnityEngine;
using UnityEngine.UI;
using System.Runtime.Serialization.Formatters.Binary;

namespace UMA
{
    public class SaveAndLoadSample : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public UMARandomAvatar Randomizer;
        public Button LoadButton;
        public bool useAvatarDefinition;
        public bool useCompressedString;

        public string saveString;
        public string avatarString;
        public string compressedString;
        public int saveStringSize;
        public int avatarStringSize;
        public int compressedStringSize;
        public int asciiStringSize;
        public int binarySize;

        public void GenerateANewUMA()
        {
            Randomizer.Randomize(Avatar);
            Avatar.BuildCharacter(false);
        }

        public void SaveUMA()
        {
            avatarString = Avatar.GetAvatarDefinitionString(true);
            saveString = Avatar.GetCurrentRecipe();
            compressedString = Avatar.GetAvatarDefinition(true).ToCompressedString("|");
            asciiStringSize = Avatar.GetAvatarDefinition(true).ToASCIIString().Length;

            binarySize = BinaryDefinition.ToBinary(new BinaryFormatter(), Avatar.GetAvatarDefinition(true)).Length;
            saveStringSize = saveString.Length * 2;
            avatarStringSize = avatarString.Length * 2;
            compressedStringSize = compressedString.Length * 2; // utf-16

            LoadButton.interactable = true;
        }

        public void LoadUMA()
        {
            if (string.IsNullOrEmpty(saveString))
            {
                return;
            }

            if (useCompressedString)
            {
                AvatarDefinition adf = AvatarDefinition.FromCompressedString(compressedString, '|');
                Avatar.LoadAvatarDefinition(adf);
                Avatar.BuildCharacter(false); // don't restore old DNA...
            }
            else if (useAvatarDefinition)
            {
                Avatar.LoadAvatarDefinition(avatarString);
                Avatar.BuildCharacter(false); // We must not restore the old DNA
            }
            else
            {
                Avatar.LoadFromRecipeString(saveString);
            }
        }
    }
}