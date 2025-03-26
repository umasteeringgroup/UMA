using System.Collections.Generic;
using System.Text;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UMA
{

    public class NewUMAGUI : MonoBehaviour, IDNASelector, IColorSelector, IItemSelector
    {
        private bool lerping;
        private Vector3 lerpTo;
        private float lerpPos = 0.0f;
        private Vector3 LerpStart;
        private Transform mainCameraTransform;
        private bool constructing = false;

        [Header("UMA")]
        public DynamicCharacterAvatar avatar;
        public bool showConsole = false;

        [Header("GUI Prefabs")]
        public GameObject ColorSelector;
        public GameObject DNAAdjuster;
        public GameObject ColorLabel;
        public GameObject GridContainer;
        public GameObject Item;
        public GameObject ItemContainer;
        public GameObject InfoText;
        public GameObject LogLabel;


        [Header("Camera Animation")]
        public Transform FacePos;
        public Transform LegsPos;
        public Transform BodyPos;
        public string FaceBoneName = "Head";
        public string LegsBoneName = "Hips";
        public float FaceBoneOffset = 0.0f;
        public float LegsBoneOffset = 0.0f;

        public float lerpSpeed = 1.0f;
        public AnimationCurve lerpCurve;


        [Header("Test")]
        public List<string> Labels = new List<string>();

        [Header("Color Tables")]
        public List<SharedColorTable> FaceColors = new List<SharedColorTable>();
        public List<SharedColorTable> HairColors = new List<SharedColorTable>();
        public List<SharedColorTable> LegsColors = new List<SharedColorTable>();
        public List<SharedColorTable> BodyColors = new List<SharedColorTable>();

        [Header("DNA")]
        public List<string> FaceDNA = new List<string>();
        public List<string> HairDNA = new List<string>();
        public List<string> LegsDNA = new List<string>();
        public List<string> BodyDNA = new List<string>();

        [Header("Items")]
        public List<UMAWardrobeRecipe> FaceItems = new List<UMAWardrobeRecipe>();
        public List<UMAWardrobeRecipe> HairItems = new List<UMAWardrobeRecipe>();
        public List<UMAWardrobeRecipe> LegsItems = new List<UMAWardrobeRecipe>();
        public List<UMAWardrobeRecipe> BodyItems = new List<UMAWardrobeRecipe>();

        /* [Header("Panels")]
         public GameObject FacePanel;
         public GameObject LegsPanel;
         public GameObject BodyPanel;
         public GameObject HairPanel;
         public GameObject MainPanel; */

        [Header("Containers")]
        public GameObject DNAContainer;
        public GameObject ItemsContainer;
        public GameObject LogDetailContainer;



        [Header("Buttons")]
        public GameObject FaceButton;
        public GameObject LegsButton;
        public GameObject BodyButton;
        public GameObject HairButton;
        public GameObject BackButton;

        private List<string> ConsoleLog = new List<string>();
        private List<string> PendingLog = new List<string>();
        private GameObject currentButton;
        private string currentInfoText;

        private void Start()
        {
            mainCameraTransform = Camera.main.transform;
            Application.logMessageReceived += HandleLog;
            ShowInfo();
        }



        // Update is called once per frame
        void Update()
        {
            if (lerping)
            {
                lerpPos += lerpSpeed * Time.deltaTime;
                if (lerpPos > 1.0f)
                {
                    lerpPos = 1.0f;
                    lerping = false;
                }
                mainCameraTransform.position = Vector3.Lerp(LerpStart, lerpTo, lerpCurve.Evaluate(lerpPos));
            }
            if (PendingLog.Count > 0)
            {
                List<string> tempLog = new List<string>();
                tempLog.AddRange(PendingLog);
                PendingLog.Clear();
                foreach (string log in tempLog)
                {
                    ShowLogItem(log);
                }
            }
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            ConsoleLog.Add(logString);
            PendingLog.Add(logString);
        }

        #region EventHandlers
        public void SetColor(string ColorName, OverlayColorData color)
        {
            avatar.SetColor(ColorName, color, true);
        }

        public void SetDNA(string DNAName, float value)
        {
            if (!constructing)
            {
                avatar.SetDNA(DNAName, value, true);
            }
        }

        public void SetItem(UMAWardrobeRecipe item)
        {
            avatar.SetSlot(item);
            avatar.BuildCharacter(true);
        }

        #endregion

        private Vector3 GetLerpPosition(Transform pos, string bone, float offset)
        {
            float boneY = pos.position.y;
            Transform boneXform = avatar.umaData.skeleton.GetBoneTransform(bone);
            if (boneXform != null)
            {
                boneY = boneXform.position.y;
            }
            Vector3 newPos = new Vector3(pos.position.x, boneY + offset, pos.position.z);
            return newPos;
        }

        private void StartLerp(Transform pos, string bone, float offset)
        {
            LerpStart = mainCameraTransform.position;
            lerpTo = GetLerpPosition(pos, bone, offset);
            lerping = true;
            lerpPos = 0.0f;
        }

        #region Panel Setup


        private void AddColors(GameObject layoutParent, SharedColorTable colorTable)
        {
            if (layoutParent != null && colorTable != null)
            {
                // Create the color name label.
                // get the name from the color table

                GameObject label = Instantiate(ColorLabel, layoutParent.transform);
                Text labelText = label.GetComponent<Text>();
                labelText.text = colorTable.sharedColorName;

                // get the prefab for the horizontal layout group
                // instantiate it and add it to the layout parent
                // add the color selectors to the layout group
                GameObject colorContainer = Instantiate(GridContainer, layoutParent.transform);

                foreach (OverlayColorData color in colorTable.colors)
                {
                    GameObject colorSelector = Instantiate(ColorSelector, colorContainer.transform);
                    ColorEffector effector = colorSelector.GetComponent<ColorEffector>();
                    effector.Setup(this, colorTable.sharedColorName, color);
                    //ColorSelector selector = colorSelector.GetComponent<ColorSelector>();
                    //selector.color = color.color;
                    //selector.colorName = color.name;
                    //selector.colorTable = colorTable;
                }
            }
        }


        public string[] BreakCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new string[1] { str };

            // We want to build string in a loop. 
            // StringBuilder has been specially desinged for this
            StringBuilder sb = new StringBuilder();

            bool nextUpper = true;
            foreach (var c in str)
            {
                if (char.IsUpper(c))
                    sb.Append(' ');

                if (nextUpper)
                    sb.Append(char.ToUpper(c));
                else
                    sb.Append(c);
                nextUpper = false;
            }


            string result = sb.ToString();
            return result.Split(' ');
        }

        private void CleanContainer(GameObject container)
        {
            if (container != null)
            {
                foreach (Transform child in container.transform)
                {
                    Destroy(child.gameObject);
                }
            }
        }


        private void SetupCategory(GameObject container, List<SharedColorTable> colorTables, List<string> DNA, List<UMAWardrobeRecipe> items)
        {
            CleanContainer(container);
            if (container != null)
            {
                // add the color selectors
                foreach (SharedColorTable colorTable in colorTables)
                {
                    AddColors(container, colorTable);
                }

                AddWardrobeItems(ItemsContainer, items);

                // add the DNA selectors
                AddDNA(container, DNA);
                // force the layout to update
                LayoutRebuilder.ForceRebuildLayoutImmediate(container.GetComponent<RectTransform>());
            }
        }

        private void AddWardrobeItems(GameObject container, List<UMAWardrobeRecipe> items)
        {
            if (container != null)
            {
                foreach (Transform child in container.transform)
                {
                    Destroy(child.gameObject);
                }


                Dictionary<string, List<UMAWardrobeRecipe>> SortedItems = new Dictionary<string, List<UMAWardrobeRecipe>>();

                foreach (UMAWardrobeRecipe item in items)
                {
                    if (item.compatibleRaces.Contains(avatar.activeRace.name))
                    {
                        string category = item.wardrobeSlot;
                        if (SortedItems.ContainsKey(category))
                        {
                            SortedItems[category].Add(item);
                        }
                        else
                        {
                            List<UMAWardrobeRecipe> newList = new List<UMAWardrobeRecipe>();
                            newList.Add(item);
                            SortedItems.Add(category, newList);
                        }
                    }
                }

                List<string> keys = new List<string>(SortedItems.Keys);

                foreach (string key in keys)
                {
                    List<UMAWardrobeRecipe> categoryItems = SortedItems[key];
                    if (categoryItems.Count > 0)
                    {
                        GameObject header = Instantiate(ColorLabel, container.transform);
                        Text headerText = header.GetComponent<Text>();
                        headerText.text = key;

                        AddWardrobeItemsForCategory(container, categoryItems, key);
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(container.GetComponent<RectTransform>());
            }
        }

        private void AddWardrobeItemsForCategory(GameObject container, List<UMAWardrobeRecipe> items, string category)
        {
            if (container != null)
            {
                GameObject gridContainer = Instantiate(ItemContainer, container.transform);

                foreach (UMAWardrobeRecipe item in items)
                {
                    GameObject itemObj = Instantiate(Item, gridContainer.transform);

                    ItemEffector effector = itemObj.GetComponent<ItemEffector>();
                    effector.Setup(this, item);
                }
            }
        }

        private void AddDNA(GameObject container, List<string> DNA)
        {
            constructing = true;
            List<string> sortedDNA = new List<string>();
            sortedDNA.AddRange(DNA);
            sortedDNA.Sort();

            List<string> miscDNA = new List<string>();

            string currentHeader = "";
            foreach (string dna in sortedDNA)
            {
                string[] split = BreakCamelCase(dna);
                if (split.Length > 1)
                {
                    if (split[0] != currentHeader)
                    {
                        currentHeader = split[0];
                        AddHeader(container, currentHeader);
                    }
                    AddEffector(container, dna, split[1]);
                }
                else
                {
                    miscDNA.Add(dna);
                }
            }

            if (miscDNA.Count > 0)
            {
                AddHeader(container, "Misc");
                foreach (string dna in miscDNA)
                {
                    AddEffector(container, dna, dna);
                }
            }
            constructing = false;
        }

        private void AddEffector(GameObject parent, string dna, string label)
        {
            GameObject dnaAdjuster = Instantiate(DNAAdjuster, parent.transform);
            DNAEffector effector = dnaAdjuster.GetComponent<DNAEffector>();

            float value = 0.5f;
            var allDNA = avatar.GetDNA();
            if (allDNA != null)
            {
                if (allDNA.ContainsKey(dna))
                {
                    value = allDNA[dna].Value;
                }
            }
            effector.Setup(this, dna, label, value);
        }

        private void AddHeader(GameObject container, string currentHeader)
        {
            GameObject header = Instantiate(ColorLabel, container.transform);
            Text headerText = header.GetComponent<Text>();
            headerText.text = currentHeader;
        }

        #endregion

        #region panel buttons    
        private void DeactivateButtons()
        {
            DeactivateButton(HairButton);
            DeactivateButton(BackButton);
            DeactivateButton(FaceButton);
            DeactivateButton(LegsButton);
            DeactivateButton(BodyButton);
        }

        private void ActivateButton(GameObject button)
        {
            if (button != null)
            {
                Image image = button.GetComponent<Image>();
                image.color = new Color(0f, 0.003f, 0.25f, 1f);
            }
        }

        private void DeactivateButton(GameObject button)
        {
            if (button != null)
            {
                Image image = button.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.35f);
            }
        }

        public void ShowInfo()
        {
            DeactivateButtons();
            CleanContainer(DNAContainer);
            if (InfoText != null)
            {
                Text t = InfoText.GetComponent<Text>();

                if (t != null)
                {
                    if (string.IsNullOrEmpty(currentInfoText))
                    {
                        currentInfoText = t.text;
                    }
                    t.text = TranslateMacros(currentInfoText);
                }
                Instantiate(InfoText, DNAContainer.transform);
            }
            ShowLog();
            ActivateButton(BackButton);
        }

        private string TranslateMacros(string text)
        {
            string result = text.Replace("{racename}", avatar.activeRace.name);
            return result;
        }

        private void ShowLogItem(string logtext)
        {
            if (!showConsole)
            {
                return;
            }
            GameObject log = Instantiate(LogLabel, ItemsContainer.transform);
            Text logText = log.GetComponent<Text>();
            logText.text = logtext;

            Button button = log.GetComponentInChildren<Button>();
            if (button != null)
            {
                button.onClick.AddListener(OnLogitemClick);
            }
        }

        private void ShowLogDetail(string log)
        {
            Text text = LogDetailContainer.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = log;
            }
        }

        private void ShowLog()
        {
            CleanContainer(ItemsContainer);
            foreach (string log in ConsoleLog)
            {
                ShowLogItem(log);
            }
            PendingLog.Clear();
        }

        public void OnLogitemClick()
        {
            Text clicked = EventSystem.current.currentSelectedGameObject.GetComponent<Text>();
            if (clicked != null)
            {
                string log = clicked.text;
                ShowLogDetail(log);
            }
        }

        public void OnFaceClick()
        {
            DeactivateButtons();
            if (FacePos != null)
            {
                StartLerp(FacePos, FaceBoneName, FaceBoneOffset);
            }
            ActivateButton(FaceButton);
            SetupCategory(DNAContainer, FaceColors, FaceDNA, FaceItems);
        }

        public void OnLegsClick()
        {
            DeactivateButtons();
            if (LegsPos != null)
            {
                StartLerp(LegsPos, LegsBoneName, LegsBoneOffset);
            }
            ActivateButton(LegsButton);
            SetupCategory(DNAContainer, LegsColors, LegsDNA, LegsItems);
        }
        public void OnBodyClick()
        {
            DeactivateButtons();
            if (BodyPos != null)
            {
                StartLerp(BodyPos, "", 0.0f);
            }
            ActivateButton(BodyButton);
            SetupCategory(DNAContainer, BodyColors, BodyDNA, BodyItems);
        }
        public void OnHairClick()
        {
            DeactivateButtons();
            if (FacePos != null)
            {
                StartLerp(FacePos, FaceBoneName, FaceBoneOffset);
            }
            ActivateButton(HairButton);
            SetupCategory(DNAContainer, HairColors, HairDNA, HairItems);
        }

        public void OnRaceClick(string raceName)
        {
            avatar.ChangeRace(raceName);
            ShowInfo(); // clears the UI of the other races items.
        }

        public void OnBackClick()
        {
            ShowInfo();
        }
        #endregion

        #region Scrolling
        public void DoDrag(BaseEventData eventData)
        {
            GameObject parent = avatar.transform.parent.gameObject;

            // rotate the parent object base on mouse input
            PointerEventData pointerData = eventData as PointerEventData;
            if (pointerData != null)
            {
                parent.transform.Rotate(Vector3.up, -pointerData.delta.x);
            }


        }
        #endregion
    }
}