using System.Collections;
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
        public Sprite clearImage;
        public Sprite PoseImage;

        [Header("Misc")]
        public List<RuntimeAnimatorController> Animators = new List<RuntimeAnimatorController>();

        private List<string> ConsoleLog = new List<string>();
        private List<string> PendingLog = new List<string>();
        private GameObject currentButton;
        private string currentInfoText;

        // Resume guard
        private bool resumedRecently = false;
        private float resumeBlockTime = 1.0f; // seconds to wait after resume

        private void Start()
        {
            mainCameraTransform = Camera.main != null ? Camera.main.transform : null;
            Application.logMessageReceived += HandleLog;
            // Defer initial ShowInfo slightly to avoid race on resume/start
            StartCoroutine(InitialShowInfo());
        }

        private IEnumerator InitialShowInfo()
        {
            yield return null;
            ShowInfo();
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

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
                if (mainCameraTransform != null)
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
            OverlayColorData newColor = color.Clone();
            if (avatar != null)
                avatar.SetRawColor(ColorName, newColor, true);
        }

        public void SetDNA(string DNAName, float value)
        {
            if (!constructing && avatar != null)
            {
                avatar.SetDNA(DNAName, value, false);
                avatar.ForceUpdate(true, true, true);
            }
        }

        public void SetItem(UMAWardrobeRecipe item)
        {
            if (avatar != null)
            {
                avatar.SetSlot(item);
                avatar.BuildCharacter(true);
            }
        }

        public void ClearItem(string category)
        {
            if (avatar != null)
            {
                avatar.ClearSlot(category);
                avatar.BuildCharacter(true);
            }
        }
        #endregion

        public void CopyToClipboard()
        {
            if (avatar == null) return;
            AvatarDefinition def = avatar.GetAvatarDefinition(true, true);
            string recipeString = def.ToCompressedString();
            GUIUtility.systemCopyBuffer = recipeString;
        }

        private Vector3 GetLerpPosition(Transform pos, string bone, float offset)
        {
            float boneY = pos.position.y;
            if (avatar != null && avatar.umaData != null && avatar.umaData.skeleton != null)
            {
                Transform boneXform = avatar.umaData.skeleton.GetBoneTransform(bone);
                if (boneXform != null)
                {
                    boneY = boneXform.position.y;
                }
            }
            Vector3 newPos = new Vector3(pos.position.x, boneY + offset, pos.position.z);
            return newPos;
        }

        private void StartLerp(Transform pos, string bone, float offset)
        {
            if (mainCameraTransform == null) return;
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
                GameObject label = SafeInstantiatePrefab(ColorLabel, layoutParent.transform);
                if (label != null)
                {
                    Text labelText = label.GetComponent<Text>();
                    if (labelText != null) labelText.text = colorTable.sharedColorName;
                }

                GameObject colorContainer = SafeInstantiatePrefab(GridContainer, layoutParent.transform);

                if (colorContainer != null)
                {
                    foreach (OverlayColorData color in colorTable.colors)
                    {
                        GameObject colorSelector = SafeInstantiatePrefab(ColorSelector, colorContainer.transform);
                        if (colorSelector == null) continue;
                        ColorEffector effector = colorSelector.GetComponent<ColorEffector>();
                        if (effector != null)
                            effector.Setup(this, colorTable.sharedColorName, color);
                    }
                }
            }
        }

        public string[] BreakCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new string[1] { str };

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
                foreach (SharedColorTable colorTable in colorTables)
                {
                    AddColors(container, colorTable);
                }

                AddWardrobeItems(ItemsContainer, items);
                AddDNA(container, DNA);
                LayoutRebuilder.ForceRebuildLayoutImmediate(container.GetComponent<RectTransform>());
            }
        }

        public bool isCompatible(UMAWardrobeRecipe item, List<string> compatibleRaces)
        {
            foreach (string race in compatibleRaces)
            {
                if (item.compatibleRaces.Contains(race))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddWardrobeItems(GameObject container, List<UMAWardrobeRecipe> items)
        {
            if (avatar == null || avatar.activeRace == null)
            {
                Debug.LogWarning("AddWardrobeItems aborted: avatar or activeRace null");
                return;
            }

            RaceData race = avatar.activeRace.data;
            List<string> races = race.GetCrossCompatibleRaces();
            races.Add(race.raceName);

            if (container != null)
            {
                foreach (Transform child in container.transform)
                {
                    Destroy(child.gameObject);
                }

                Dictionary<string, List<UMAWardrobeRecipe>> SortedItems = new Dictionary<string, List<UMAWardrobeRecipe>>();

                foreach (UMAWardrobeRecipe item in items)
                {
                    if (isCompatible(item, races))
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
                        GameObject header = SafeInstantiatePrefab(ColorLabel, container.transform);
                        if (header != null)
                        {
                            Text headerText = header.GetComponent<Text>();
                            if (headerText != null) headerText.text = key;
                        }

                        AddWardrobeItemsForCategory(container, categoryItems, key, header);
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(container.GetComponent<RectTransform>());
            }
        }

        private void AddWardrobeItemsForCategory(GameObject container, List<UMAWardrobeRecipe> items, string category, GameObject categoryHeader = null)
        {
            if (container != null)
            {
                Text headerTextComponent = categoryHeader != null ? categoryHeader.GetComponent<Text>() : null;

                GameObject gridContainer = SafeInstantiatePrefab(ItemContainer, container.transform);
                if (gridContainer == null) return;

                GameObject clearObj = SafeInstantiatePrefab(Item, gridContainer.transform);
                if (clearObj != null)
                {
                    ItemEffector clearEffector = clearObj.GetComponent<ItemEffector>();
                    if (clearEffector != null)
                    {
                        clearEffector.clearImage = clearImage;
                        clearEffector.Setup(this, null, category);
                        clearEffector.SetCategoryHeader(headerTextComponent);
                    }
                }

                foreach (UMAWardrobeRecipe item in items)
                {
                    GameObject itemObj = SafeInstantiatePrefab(Item, gridContainer.transform);
                    if (itemObj == null) continue;
                    ItemEffector effector = itemObj.GetComponent<ItemEffector>();
                    if (effector != null)
                    {
                        effector.Setup(this, item, category);
                        effector.SetCategoryHeader(headerTextComponent);
                    }
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
            GameObject dnaAdjuster = SafeInstantiatePrefab(DNAAdjuster, parent.transform);
            if (dnaAdjuster == null) return;
            DNAEffector effector = dnaAdjuster.GetComponent<DNAEffector>();

            float value = 0.5f;
            var allDNA = avatar != null ? avatar.GetDNA() : null;
            if (allDNA != null)
            {
                if (allDNA.ContainsKey(dna))
                {
                    value = allDNA[dna].Value;
                }
            }
            if (effector != null)
                effector.Setup(this, dna, label, value);
        }

        private void AddHeader(GameObject container, string currentHeader)
        {
            GameObject header = SafeInstantiatePrefab(ColorLabel, container.transform);
            if (header != null)
            {
                Text headerText = header.GetComponent<Text>();
                if (headerText != null) headerText.text = currentHeader;
            }
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
                if (image != null) image.color = new Color(0f, 0.003f, 0.25f, 1f);
            }
        }

        private void DeactivateButton(GameObject button)
        {
            if (button != null)
            {
                Image image = button.GetComponent<Image>();
                if (image != null) image.color = new Color(0f, 0f, 0f, 0.35f);
            }
        }

        // Public ShowInfo entry point with resume guard
        public void ShowInfo()
        {
            if (resumedRecently)
            {
                StartCoroutine(DeferredShowInfo());
                return;
            }
            ShowInfoImmediate();
        }

        // The actual ShowInfo logic factored out for safe reuse
        private void ShowInfoImmediate()
        {
            DeactivateButtons();
            CleanContainer(DNAContainer);

            if (avatar == null)
            {
                Debug.LogError("NewUMAGUI.ShowInfoImmediate: avatar is null, aborting.");
                return;
            }
            if (avatar.activeRace == null)
            {
                Debug.LogError("NewUMAGUI.ShowInfoImmediate: avatar.activeRace is null, aborting.");
                return;
            }

            if (InfoText != null && DNAContainer != null)
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
                // Use safe instantiate helper
                SafeInstantiatePrefab(InfoText, DNAContainer.transform);
            }
            ShowLog();
            ActivateButton(BackButton);
        }

        private IEnumerator DeferredShowInfo()
        {
            // Wait a short time to allow native subsystems to stabilize after resume
            yield return new WaitForSeconds(0.5f);
            resumedRecently = false;
            // Double-check state
            if (avatar == null || avatar.activeRace == null || DNAContainer == null)
            {
                Debug.LogWarning("DeferredShowInfo aborted due to null state");
                yield break;
            }
            ShowInfoImmediate();
        }

        private string TranslateMacros(string text)
        {
            string result = text;
            if (avatar != null && avatar.activeRace != null)
                result = text.Replace("{racename}", avatar.activeRace.name);
            return result;
        }

        private void ShowLogItem(string logtext)
        {
            if (!showConsole) return;
            if (ItemsContainer == null)
            {
                Debug.LogWarning("ShowLogItem aborted: ItemsContainer null");
                return;
            }
            GameObject log = SafeInstantiatePrefab(LogLabel, ItemsContainer.transform);
            if (log == null) return;
            Text logText = log.GetComponent<Text>();
            if (logText != null) logText.text = logtext;

            Button button = log.GetComponentInChildren<Button>();
            if (button != null)
            {
                button.onClick.AddListener(OnLogitemClick);
            }
        }

        private void ShowLogDetail(string log)
        {
            if (LogDetailContainer == null) return;
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

        public int currentAnimator;
        public void OnPoseClick()
        {
            if (Animators.Count > 0)
            {
                currentAnimator++;
                if (currentAnimator >= Animators.Count)
                {
                    currentAnimator = 0;
                }
                RuntimeAnimatorController controller = Animators[currentAnimator];
                if (avatar != null)
                {
                    avatar.animator.runtimeAnimatorController = controller;
                    avatar.animationController = controller;
                    if (avatar.raceAnimationControllers != null)
                        avatar.raceAnimationControllers.defaultAnimationController = controller;
                }
            }
        }

        public void OnRaceClick(string raceName)
        {
            if (avatar != null)
            {
                avatar.ChangeRace(raceName);
                ShowInfo();
            }
        }

        public void OnBackClick()
        {
            ShowInfo();
        }
        #endregion

        #region Scrolling
        public void DoDrag(BaseEventData eventData)
        {
            if (avatar == null || avatar.transform == null) return;
            GameObject parent = avatar.transform.parent != null ? avatar.transform.parent.gameObject : null;
            if (parent == null) return;

            PointerEventData pointerData = eventData as PointerEventData;
            if (pointerData != null)
            {
                parent.transform.Rotate(Vector3.up, -pointerData.delta.x);
            }
        }
        #endregion

        #region Safe Instantiate Helpers

        // Instantiate without using the parent overload and set parent afterwards.
        private GameObject SafeInstantiatePrefab(GameObject prefab, Transform parent)
        {
            if (prefab == null)
            {
                Debug.LogWarning("SafeInstantiatePrefab: prefab is null");
                return null;
            }
            if (parent == null)
            {
                Debug.LogWarning($"SafeInstantiatePrefab: parent is null for prefab {prefab.name}");
                return null;
            }

            GameObject instance = null;
            try
            {
                instance = Instantiate(prefab);
                if (instance == null)
                {
                    Debug.LogError($"SafeInstantiatePrefab: Instantiate returned null for {prefab.name}");
                    return null;
                }
                instance.transform.SetParent(parent, false);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"SafeInstantiatePrefab failed for {prefab.name}: {ex}");
                if (instance != null) Destroy(instance);
                instance = null;
            }
            return instance;
        }

        private IEnumerator SafeInstantiateNextFrame(GameObject prefab, Transform parent)
        {
            yield return null;
            SafeInstantiatePrefab(prefab, parent);
        }

        #endregion

        #region Resume Handlers

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                resumedRecently = true;
                StartCoroutine(ClearResumeFlagAfterDelay());
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                resumedRecently = true;
                StartCoroutine(ClearResumeFlagAfterDelay());
            }
        }

        private IEnumerator ClearResumeFlagAfterDelay()
        {
            yield return new WaitForSeconds(resumeBlockTime);
            resumedRecently = false;
        }

        #endregion
    }
}
