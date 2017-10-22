using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

namespace UMA.CharacterSystem
{
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public class NetworkDCA : NetworkBehaviour 
    {
        //TODO decouple these gui objects
        public UMAWardrobeCollectionSelector hairDropDownSelector;
        public UMAWardrobeCollectionSelector helmetDropDownSelector;
        public UMAWardrobeCollectionSelector chestDropDownSelector;
        public UMAWardrobeCollectionSelector handsDropDownSelector;
        public UMAWardrobeCollectionSelector legsDropDownSelector;
        public UMAWardrobeCollectionSelector feetDropDownSelector;
        public UMAWardrobeCollectionSelector underwearDropDownSelector;
        public ChangeRaceButton raceButton;
        public DynamicColorList skinColorList;
        public DynamicColorList hairColorList;

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

            _avatar.umaData.CharacterCreated.AddListener(OnCharacterCreated);
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

            if ((base.syncVarDirtyBits & RaceMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.activeRace.name);
            }

            if ((base.syncVarDirtyBits & SkinColorMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.GetColor("Skin").color);
            }

            if ((base.syncVarDirtyBits & HairColorMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                writer.Write(_avatar.GetColor("Hair").color);
            }

            if ((base.syncVarDirtyBits & HairMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Hair");
            }

            if ((base.syncVarDirtyBits & HelmetMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Helmet");
            }

            if ((base.syncVarDirtyBits & ChestMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Chest");
            }

            if ((base.syncVarDirtyBits & HandsMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Hands");
            }

            if ((base.syncVarDirtyBits & LegsMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Legs");
            }

            if ((base.syncVarDirtyBits & FeetMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Feet");
            }

            if ((base.syncVarDirtyBits & UnderwearMask) != 0u)
            {
                if (!wroteSyncVar)
                {
                    writer.WritePackedUInt32(base.syncVarDirtyBits);
                    wroteSyncVar = true;
                }
                WriteWardrobe(writer, "Underwear");
            }

            return wroteSyncVar;
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
                ReadWardrobe("Hair", reader.ReadString());
                ReadWardrobe("Helmet", reader.ReadString());
                ReadWardrobe("Chest", reader.ReadString());
                ReadWardrobe("Hands", reader.ReadString());
                ReadWardrobe("Legs", reader.ReadString());
                ReadWardrobe("Feet", reader.ReadString());
                ReadWardrobe("Underwear", reader.ReadString());
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
            SetDirtyBit(SkinColorMask);
            _avatar.BuildCharacter();
            _avatar.ForceUpdate(false, true, false);
        }

        [Command]
        public void CmdSetHairColor(Color newColor)
        {
            if (_avatar == null)
                return;

            _avatar.SetColor("Hair", newColor);
            SetDirtyBit(HairColorMask);
            _avatar.BuildCharacter();
            _avatar.ForceUpdate(false, true, false);
        }
        //====================================================================
        //  UMAData Callbacks
        //====================================================================
        private void OnCharacterCreated(UMAData umaData)
        {
            if (_avatar == null)
            {
                Debug.LogError("Avatar is null!");
                return;
            }

            if (isLocalPlayer)
            {
                //TODO remove these
                raceButton = GameObject.Find("RaceButton").GetComponent<ChangeRaceButton>();
                raceButton.avatar = _avatar;

                hairDropDownSelector = GameObject.Find("HairDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                hairDropDownSelector.avatar = _avatar;

                helmetDropDownSelector = GameObject.Find("HelmetDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                helmetDropDownSelector.avatar = _avatar;

                chestDropDownSelector = GameObject.Find("ChestDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                chestDropDownSelector.avatar = _avatar;

                handsDropDownSelector = GameObject.Find("HandsDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                handsDropDownSelector.avatar = _avatar;

                legsDropDownSelector = GameObject.Find("LegsDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                legsDropDownSelector.avatar = _avatar;

                feetDropDownSelector = GameObject.Find("FeetDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                feetDropDownSelector.avatar = _avatar;

                underwearDropDownSelector = GameObject.Find("UnderwearDropdown").GetComponent<UMAWardrobeCollectionSelector>();
                underwearDropDownSelector.avatar = _avatar;

                skinColorList = GameObject.Find("SkinColorList").GetComponent<DynamicColorList>();
                skinColorList.avatar = _avatar;
                skinColorList.Initialize("Skin");

                hairColorList = GameObject.Find("HairColorList").GetComponent<DynamicColorList>();
                hairColorList.avatar = _avatar;
                hairColorList.Initialize("Hair");
            }
        }
    }
}
