using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public class NetworkDCA : NetworkBehaviour 
    {
        //Let's create this property so we don't need other scripts to be network behaviours just to check this.
        public bool isLocal { get { return isLocalPlayer; } }
        public Camera playerCamera;
        public DynamicCharacterAvatar avatar 
        { 
            get { return _avatar; } 
        }
        private DynamicCharacterAvatar _avatar;

        public const uint RaceMask         = 1 << 0;
        public const uint DnaMask          = 1 << 1;
        public const uint SkinColorMask    = 1 << 2;
        public const uint HairColorMask    = 1 << 3;

        public const uint HairMask         = 1 << 6;
        public const uint FeetMask         = 1 << 7;
        public const uint ChestMask        = 1 << 8;
        public const uint LegsMask         = 1 << 9;
        public const uint HandsMask        = 1 << 10;
        public const uint HelmetMask       = 1 << 11;
        public const uint UnderwearMask    = 1 << 12;

        [SerializeField]
        private bool networkDebug = false;

        void Start()
        {
            if (_avatar == null)
                _avatar = GetComponent<DynamicCharacterAvatar>();

            if (!isServer)
                _avatar.BuildCharacterEnabled = false; //We need to not build the avatar until after the initial state packet
            else
                _avatar.BuildCharacterEnabled = true;
        }

        public override void OnStartLocalPlayer()
        {
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(true);
            }
            else
            {
                playerCamera = gameObject.transform.Find("PlayerCamera").GetComponent<Camera>();
                playerCamera.gameObject.SetActive(true);
            }
        }

        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            if (initialState)
            {
                if (_avatar == null)
                    _avatar = GetComponent<DynamicCharacterAvatar>();

                if (_avatar == null)
                {
                    Debug.LogError("Avatar is null!");
                    return false;
                }

                writer.Write(_avatar.activeRace.name);
                writer.Write(_avatar.GetColor("Skin").color);
                writer.Write(_avatar.GetColor("Hair").color);
                //Can't write dna here, it's not created yet :(
                WriteWardrobe(writer, "Hair");
                WriteWardrobe(writer, "Helmet");
                WriteWardrobe(writer, "Chest");
                WriteWardrobe(writer, "Hands");
                WriteWardrobe(writer, "Legs");
                WriteWardrobe(writer, "Feet");
                WriteWardrobe(writer, "Underwear");
                return true;
            }

            bool wroteSyncVar = false;

            if ((syncVarDirtyBits & RaceMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.activeRace.name);
            }

            if ((syncVarDirtyBits & SkinColorMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.GetColor("Skin").color);
            }

            if ((syncVarDirtyBits & HairColorMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.GetColor("Hair").color);
            }

            if ((syncVarDirtyBits & HairMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Hair");
            }

            if ((syncVarDirtyBits & DnaMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteDNA(writer);
            }

            if ((syncVarDirtyBits & HelmetMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Helmet");
            }

            if ((syncVarDirtyBits & ChestMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Chest");
            }

            if ((syncVarDirtyBits & HandsMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Hands");
            }

            if ((syncVarDirtyBits & LegsMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Legs");
            }

            if ((syncVarDirtyBits & FeetMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Feet");
            }

            if ((syncVarDirtyBits & UnderwearMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Underwear");
            }

            return wroteSyncVar;
        }

        private void WriteDNA(NetworkWriter writer)
        {
            Dictionary<string, DnaSetter> dna = _avatar.GetDNA();

            foreach(string dnaName in dna.Keys)
            {
                writer.Write(dna[dnaName].Value);
            }
        }

        private void ReadDNA(NetworkReader reader)
        {
            Dictionary<string, DnaSetter> dna = _avatar.GetDNA();

            foreach (string dnaName in dna.Keys)
            {
                dna[dnaName].Set((float)reader.ReadSingle());
            }
        }

        private void WriteWardrobe(NetworkWriter writer, string slotName)
        {
            if (networkDebug) { Debug.Log(string.Format("Writing slot: {0}", slotName )); }

            if (_avatar.WardrobeRecipes.ContainsKey(slotName))
                writer.Write(_avatar.WardrobeRecipes[slotName].name);
            else
                writer.Write("");
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            if (initialState)
            {
                if (_avatar == null)
                    _avatar = GetComponent<DynamicCharacterAvatar>();

                _avatar.context = UMAContext.FindInstance();

                _avatar.ChangeRace(reader.ReadString());


                _avatar.SetColor("Skin", reader.ReadColor());
                _avatar.SetColor("Hair", reader.ReadColor());
                _avatar.UpdateColors(true);
                ReadDNA(reader);

                ReadWardrobe("Hair", reader.ReadString());
                ReadWardrobe("Helmet", reader.ReadString());
                ReadWardrobe("Chest", reader.ReadString());
                ReadWardrobe("Hands", reader.ReadString());
                ReadWardrobe("Legs", reader.ReadString());
                ReadWardrobe("Feet", reader.ReadString());
                ReadWardrobe("Underwear", reader.ReadString());

                _avatar.BuildCharacterEnabled = true;
                _avatar.BuildCharacter();
                _avatar.ForceUpdate(false, true, true);
                return;
            }

            int bitmask = (int)reader.ReadPackedUInt32();
            if ((bitmask & RaceMask) != 0)
                _avatar.ChangeRace(reader.ReadString());

            if ((bitmask & SkinColorMask) != 0)
                _avatar.SetColor("Skin", reader.ReadColor());

            if ((bitmask & HairColorMask) != 0)
                _avatar.SetColor("Hair", reader.ReadColor());

            if ((bitmask & HairMask) != 0)
                ReadWardrobe("Hair", reader.ReadString());

            if ((bitmask & DnaMask) != 0)
                ReadDNA(reader);

            if ((bitmask & HelmetMask) != 0)
                ReadWardrobe("Helmet", reader.ReadString());

            if ((bitmask & ChestMask) != 0)
                ReadWardrobe("Chest", reader.ReadString());

            if ((bitmask & HandsMask) != 0)
                ReadWardrobe("Hands", reader.ReadString());

            if ((bitmask & LegsMask) != 0)
                ReadWardrobe("Legs", reader.ReadString());

            if ((bitmask & FeetMask) != 0)
                ReadWardrobe("Feet", reader.ReadString());

            if ((bitmask & UnderwearMask) != 0)
                ReadWardrobe("Underwear", reader.ReadString());

            //If our bitmask is greater than 0 then we had a change so rebuild and forceupdate the avatar.
            //With more diverse masks we'll need to change this check to be more specific
            if (bitmask > 0)
            {
                _avatar.BuildCharacterEnabled = true;
                _avatar.BuildCharacter();
                _avatar.ForceUpdate(false, true, true);
            }
        }

        private void ReadWardrobe(string slotName, string wardrobeName)
        {
            if (_avatar == null)
                return;
            
            if (networkDebug) { Debug.Log(string.Format("Reading {0} slot: {1}", slotName, wardrobeName)); }

            if (string.IsNullOrEmpty(wardrobeName))
                _avatar.ClearSlot(slotName);
            else
                _avatar.SetSlot(slotName, wardrobeName);
        }

        [Command]
        public void CmdSetWardrobe(uint slotName, string wardrobeName)
        {
            if (_avatar == null)
                return;

            if (networkDebug) { Debug.Log(string.Format("CmdSetWardrobe: {0} {1}", slotName, wardrobeName)); }

            switch (slotName)
            {
                case HairMask:
                    ReadWardrobe("Hair", wardrobeName);
                    SetDirtyBit(HairMask);
                    break;
                case HelmetMask:
                    ReadWardrobe("Helmet", wardrobeName);
                    SetDirtyBit(HelmetMask);
                    break;
                case ChestMask:
                    ReadWardrobe("Chest", wardrobeName);
                    SetDirtyBit(ChestMask);
                    break;
                case HandsMask:
                    ReadWardrobe("Hands", wardrobeName);
                    SetDirtyBit(HandsMask);
                    break;
                case LegsMask:
                    ReadWardrobe("Legs", wardrobeName);
                    SetDirtyBit(LegsMask);
                    break;
                case FeetMask:
                    ReadWardrobe("Feet", wardrobeName);
                    SetDirtyBit(FeetMask);
                    break;
                case UnderwearMask:
                    ReadWardrobe("Underwear", wardrobeName);
                    SetDirtyBit(UnderwearMask);
                    break;
                default:
                    break;
            }

            _avatar.BuildCharacter();
            _avatar.ForceUpdate(false, true, true);
        }

        [Command]
        public void CmdSetRace(string raceName)
        {
            if (_avatar == null)
                return;

            _avatar.ChangeRace(raceName);
            SetDirtyBit(RaceMask);

            //Clear all slots?
            _avatar.ClearSlots();
            SetDirtyBit(HelmetMask);
            SetDirtyBit(HairMask);
            SetDirtyBit(FeetMask);
            SetDirtyBit(ChestMask);
            SetDirtyBit(LegsMask);
            SetDirtyBit(HandsMask);
            SetDirtyBit(UnderwearMask);

            _avatar.BuildCharacter();
            _avatar.ForceUpdate(false, true, true);
        }

        [Command]
        public void CmdSetSkinColor(Color newColor)
        {
            if (_avatar == null)
                return;
            
            _avatar.SetColor("Skin", newColor);
            _avatar.UpdateColors(true);
            SetDirtyBit(SkinColorMask);
        }

        [Command]
        public void CmdSetHairColor(Color newColor)
        {
            if (_avatar == null)
                return;

            _avatar.SetColor("Hair", newColor);
            _avatar.UpdateColors(true);
            SetDirtyBit(HairColorMask);
        }

        [Command]
        public void CmdUpdateDNA()
        {
            if (avatar == null)
                return;

            Dictionary<string, DnaSetter> dna = avatar.GetDNA();

            foreach (DnaSetter setter in dna.Values)
            {
                setter.Set(Random.Range(0.1f, 0.9f));
            }

            avatar.ForceUpdate(true, false, false);

            SetDirtyBit(DnaMask);
        }
    }
}
