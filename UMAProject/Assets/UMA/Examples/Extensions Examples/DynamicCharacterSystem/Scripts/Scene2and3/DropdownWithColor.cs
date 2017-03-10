using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI.CoroutineTween;

namespace UnityEngine.UI
{
    /// <summary>
    ///   <para>A standard dropdown that presents a list of options when clicked, of which one can be chosen.</para>
    /// </summary>
    //[AddComponentMenu("UI/Dropdown", 35), RequireComponent(typeof(RectTransform))]
    public class DropdownWithColor : Selectable, IEventSystemHandler, IPointerClickHandler, ISubmitHandler, ICancelHandler
    {
        protected internal class DropdownItem : MonoBehaviour, IEventSystemHandler, IPointerEnterHandler, ICancelHandler
        {
            [SerializeField]
            private Text m_Text;

            [SerializeField]
            private Image m_Image;

            [SerializeField]
            private RectTransform m_RectTransform;

            [SerializeField]
            private Toggle m_Toggle;

            public Text text
            {
                get
                {
                    return this.m_Text;
                }
                set
                {
                    this.m_Text = value;
                }
            }

            public Image image
            {
                get
                {
                    return this.m_Image;
                }
                set
                {
                    this.m_Image = value;
                }
            }

            public RectTransform rectTransform
            {
                get
                {
                    return this.m_RectTransform;
                }
                set
                {
                    this.m_RectTransform = value;
                }
            }

            public Toggle toggle
            {
                get
                {
                    return this.m_Toggle;
                }
                set
                {
                    this.m_Toggle = value;
                }
            }

            public virtual void OnPointerEnter(PointerEventData eventData)
            {
                EventSystem.current.SetSelectedGameObject(base.gameObject);
            }

            public virtual void OnCancel(BaseEventData eventData)
            {
                Dropdown componentInParent = base.GetComponentInParent<Dropdown>();
                if (componentInParent)
                {
                    componentInParent.Hide();
                }
            }
        }

        /// <summary>
        ///   <para>Class to store the text and/or image of a single option in the dropdown list.</para>
        /// </summary>
        [Serializable]
        public class OptionData
        {
            [SerializeField]
            private string m_Text;

            [SerializeField]
            private Sprite m_Image;

            [SerializeField]
            private Color m_Color;

            /// <summary>
            ///   <para>The text associated with the option.</para>
            /// </summary>
            public string text
            {
                get
                {
                    return this.m_Text;
                }
                set
                {
                    this.m_Text = value;
                }
            }

            /// <summary>
            ///   <para>The image associated with the option.</para>
            /// </summary>
            public Sprite image
            {
                get
                {
                    return this.m_Image;
                }
                set
                {
                    this.m_Image = value;
                }
            }

            /// <summary>
            ///   <para>The color associated with the option.</para>
            /// </summary>
            public Color color
            {
                get
                {
                    return this.m_Color;
                }
                set
                {
                    this.m_Color = value;
                }
            }

            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData()
            {
            }

            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(string text)
            {
                this.text = text;
            }

            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(Sprite image)
            {
                this.image = image;
            }

            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(Color color)
            {
                this.color = color;
            }

            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(string text, Sprite image)
            {
                this.text = text;
                this.image = image;
            }
            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(string text, Color color)
            {
                this.text = text;
                this.color = color;
            }
            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(Sprite image, Color color)
            {
                this.image = image;
                this.color = color;
            }
            /// <summary>
            ///   <para>Create an object representing a single option for the dropdown list.</para>
            /// </summary>
            /// <param name="text">Optional text for the option.</param>
            /// <param name="image">Optional image for the option.</param>
            /// <param name="color">Optional color for the option.</param>
            public OptionData(string text, Sprite image, Color color)
            {
                this.text = text;
                this.image = image;
                this.color = color;
            }
        }

        /// <summary>
        ///   <para>Class used internally to store the list of options for the dropdown list.</para>
        /// </summary>
        [Serializable]
        public class OptionDataList
        {
            [SerializeField]
            private List<DropdownWithColor.OptionData> m_Options;

            /// <summary>
            ///   <para>The list of options for the dropdown list.</para>
            /// </summary>
            public List<DropdownWithColor.OptionData> options
            {
                get
                {
                    return this.m_Options;
                }
                set
                {
                    this.m_Options = value;
                }
            }

            public OptionDataList()
            {
                this.options = new List<DropdownWithColor.OptionData>();
            }
        }

        /// <summary>
        ///   <para>UnityEvent callback for when a dropdown current option is changed.</para>
        /// </summary>
        [Serializable]
        public class DropdownEvent : UnityEvent<int>
        {
        }

        [SerializeField]
        private RectTransform m_Template;

        [SerializeField]
        private Text m_CaptionText;

        [SerializeField]
        private Image m_CaptionImage;

        [SerializeField, Space]
        private Text m_ItemText;

        [SerializeField]
        private Image m_ItemImage;

        [SerializeField, Space]
        private int m_Value;

        [SerializeField, Space]
        private DropdownWithColor.OptionDataList m_Options = new DropdownWithColor.OptionDataList();

        [SerializeField, Space]
        private DropdownWithColor.DropdownEvent m_OnValueChanged = new DropdownWithColor.DropdownEvent();

        private GameObject m_Dropdown;

        private GameObject m_Blocker;

        private List<DropdownWithColor.DropdownItem> m_Items = new List<DropdownWithColor.DropdownItem>();

       // private TweenRunner<FloatTween> m_AlphaTweenRunner;

        private bool validTemplate;

        private static DropdownWithColor.OptionData s_NoOptionData = new DropdownWithColor.OptionData();

        /// <summary>
        ///   <para>The Rect Transform of the template for the dropdown list.</para>
        /// </summary>
        public RectTransform template
        {
            get
            {
                return this.m_Template;
            }
            set
            {
                this.m_Template = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>The Text component to hold the text of the currently selected option.</para>
        /// </summary>
        public Text captionText
        {
            get
            {
                return this.m_CaptionText;
            }
            set
            {
                this.m_CaptionText = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>The Image component to hold the image of the currently selected option.</para>
        /// </summary>
        public Image captionImage
        {
            get
            {
                return this.m_CaptionImage;
            }
            set
            {
                this.m_CaptionImage = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>The Text component to hold the text of the item.</para>
        /// </summary>
        public Text itemText
        {
            get
            {
                return this.m_ItemText;
            }
            set
            {
                this.m_ItemText = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>The Image component to hold the image of the item.</para>
        /// </summary>
        public Image itemImage
        {
            get
            {
                return this.m_ItemImage;
            }
            set
            {
                this.m_ItemImage = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>The list of possible options. A text string and an image can be specified for each option.</para>
        /// </summary>
        public List<DropdownWithColor.OptionData> options
        {
            get
            {
                return this.m_Options.options;
            }
            set
            {
                this.m_Options.options = value;
                this.RefreshShownValue();
            }
        }

        /// <summary>
        ///   <para>A UnityEvent that is invoked when when a user has clicked one of the options in the dropdown list.</para>
        /// </summary>
        public DropdownWithColor.DropdownEvent onValueChanged
        {
            get
            {
                return this.m_OnValueChanged;
            }
            set
            {
                this.m_OnValueChanged = value;
            }
        }

        /// <summary>
        ///   <para>The index of the currently selected option. 0 is the first option, 1 is the second, and so on.</para>
        /// </summary>
        public int value
        {
            get
            {
                return this.m_Value;
            }
            set
            {
                if (Application.isPlaying && (value == this.m_Value || this.options.Count == 0))
                {
                    return;
                }
                this.m_Value = Mathf.Clamp(value, 0, this.options.Count - 1);
                this.RefreshShownValue();
                this.m_OnValueChanged.Invoke(this.m_Value);
            }
        }

        protected DropdownWithColor()
        {
        }

        protected override void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            //this.m_AlphaTweenRunner = new TweenRunner<FloatTween>();
            //this.m_AlphaTweenRunner.Init(this);
            if (this.m_CaptionImage)
            {
                this.m_CaptionImage.enabled = (this.m_CaptionImage.sprite != null);
            }
            if (this.m_Template)
            {
                this.m_Template.gameObject.SetActive(false);
            }
        }

#if UNITY_EDITOR
        protected new void OnValidate()
        {
            //This doesn't work on WebGL as there is no OnValidate in Selectable on that platform
            base.OnValidate();

            if (!this.IsActive())
            {
                return;
            }
            this.RefreshShownValue();
        }
#endif

        /// <summary>
        ///   <para>Refreshes the text and image (if available) of the currently selected option.
        ///
        /// If you have modified the list of options, you should call this method afterwards to ensure that the visual state of the dropdown corresponds to the updated options.</para>
        /// </summary>
        public void RefreshShownValue()
        {
            DropdownWithColor.OptionData optionData = DropdownWithColor.s_NoOptionData;
            if (this.options.Count > 0)
            {
                optionData = this.options[Mathf.Clamp(this.m_Value, 0, this.options.Count - 1)];
            }
            if (this.m_CaptionText)
            {
                if (optionData != null && optionData.text != null)
                {
                    this.m_CaptionText.text = optionData.text;
                }
                else
                {
                    this.m_CaptionText.text = string.Empty;
                }
            }
            if (this.m_CaptionImage)
            {
                if (optionData != null)
                {
                    this.m_CaptionImage.sprite = optionData.image;
                    this.m_CaptionImage.color = optionData.color;
                }
                else
                {
                    this.m_CaptionImage.sprite = null;
                }
                this.m_CaptionImage.enabled = (this.m_CaptionImage.sprite != null);
            }
        }

        public void AddOptions(List<DropdownWithColor.OptionData> options)
        {
            this.options.AddRange(options);
            this.RefreshShownValue();
        }

        public void AddOptions(List<string> options)
        {
            for (int i = 0; i < options.Count; i++)
            {
                this.options.Add(new DropdownWithColor.OptionData(options[i]));
            }
            this.RefreshShownValue();
        }

        public void AddOptions(List<Sprite> options)
        {
            for (int i = 0; i < options.Count; i++)
            {
                this.options.Add(new DropdownWithColor.OptionData(options[i]));
            }
            this.RefreshShownValue();
        }

        /// <summary>
        ///   <para>Clear the list of options in the DropdownWithColor.</para>
        /// </summary>
        public void ClearOptions()
        {
            this.options.Clear();
            this.RefreshShownValue();
        }

        private void SetupTemplate()
        {
            this.validTemplate = false;
            if (!this.m_Template)
            {
                Debug.LogError("The dropdown template is not assigned. The template needs to be assigned and must have a child GameObject with a Toggle component serving as the item.", this);
                return;
            }
            GameObject gameObject = this.m_Template.gameObject;
            gameObject.SetActive(true);
            Toggle componentInChildren = this.m_Template.GetComponentInChildren<Toggle>();
            this.validTemplate = true;
            if (!componentInChildren || componentInChildren.transform == this.template)
            {
                this.validTemplate = false;
                Debug.LogError("The dropdown template is not valid. The template must have a child GameObject with a Toggle component serving as the item.", this.template);
            }
            else if (!(componentInChildren.transform.parent is RectTransform))
            {
                this.validTemplate = false;
                Debug.LogError("The dropdown template is not valid. The child GameObject with a Toggle component (the item) must have a RectTransform on its parent.", this.template);
            }
            else if (this.itemText != null && !this.itemText.transform.IsChildOf(componentInChildren.transform))
            {
                this.validTemplate = false;
                Debug.LogError("The dropdown template is not valid. The Item Text must be on the item GameObject or children of it.", this.template);
            }
            else if (this.itemImage != null && !this.itemImage.transform.IsChildOf(componentInChildren.transform))
            {
                this.validTemplate = false;
                Debug.LogError("The dropdown template is not valid. The Item Image must be on the item GameObject or children of it.", this.template);
            }
            if (!this.validTemplate)
            {
                gameObject.SetActive(false);
                return;
            }
            DropdownWithColor.DropdownItem dropdownItem = componentInChildren.gameObject.AddComponent<DropdownWithColor.DropdownItem>();
            dropdownItem.text = this.m_ItemText;
            dropdownItem.image = this.m_ItemImage;
            dropdownItem.toggle = componentInChildren;
            dropdownItem.rectTransform = (RectTransform)componentInChildren.transform;
            Canvas orAddComponent = DropdownWithColor.GetOrAddComponent<Canvas>(gameObject);
            orAddComponent.overrideSorting = true;
            orAddComponent.sortingOrder = 30000;
            DropdownWithColor.GetOrAddComponent<GraphicRaycaster>(gameObject);
            DropdownWithColor.GetOrAddComponent<CanvasGroup>(gameObject);
            gameObject.SetActive(false);
            this.validTemplate = true;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            T t = go.GetComponent<T>();
            if (!t)
            {
                t = go.AddComponent<T>();
            }
            return t;
        }

        /// <summary>
        ///   <para>Handling for when the dropdown is 'clicked'.</para>
        /// </summary>
        /// <param name="eventData">Current event.</param>
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            this.Show();
        }

        /// <summary>
        ///   <para>What to do when the event system sends a submit Event.</para>
        /// </summary>
        /// <param name="eventData">Current event.</param>
        public virtual void OnSubmit(BaseEventData eventData)
        {
            this.Show();
        }

        /// <summary>
        ///   <para>Called by a BaseInputModule when a Cancel event occurs.</para>
        /// </summary>
        /// <param name="eventData"></param>
        public virtual void OnCancel(BaseEventData eventData)
        {
            this.Hide();
        }

        /// <summary>
        ///   <para>Show the dropdown list.</para>
        /// </summary>
        public void Show()
        {
            if (!this.IsActive() || !this.IsInteractable() || this.m_Dropdown != null)
            {
                return;
            }
            if (!this.validTemplate)
            {
                this.SetupTemplate();
                if (!this.validTemplate)
                {
                    return;
                }
            }
            List<Canvas> list = MyListPool<Canvas>.Get();
            base.gameObject.GetComponentsInParent<Canvas>(false, list);
            if (list.Count == 0)
            {
                return;
            }
            Canvas canvas = list[0];
            MyListPool<Canvas>.Release(list);
            this.m_Template.gameObject.SetActive(true);
            this.m_Dropdown = this.CreateDropdownList(this.m_Template.gameObject);
            this.m_Dropdown.name = "Dropdown List";
            this.m_Dropdown.SetActive(true);
            RectTransform rectTransform = this.m_Dropdown.transform as RectTransform;
            rectTransform.SetParent(this.m_Template.transform.parent, false);
            DropdownWithColor.DropdownItem componentInChildren = this.m_Dropdown.GetComponentInChildren<DropdownWithColor.DropdownItem>();
            GameObject gameObject = componentInChildren.rectTransform.parent.gameObject;
            RectTransform rectTransform2 = gameObject.transform as RectTransform;
            componentInChildren.rectTransform.gameObject.SetActive(true);
            Rect rect = rectTransform2.rect;
            Rect rect2 = componentInChildren.rectTransform.rect;
            Vector2 vector = rect2.min - rect.min + (Vector2)componentInChildren.rectTransform.localPosition;
            Vector2 vector2 = rect2.max - rect.max + (Vector2)componentInChildren.rectTransform.localPosition;
            Vector2 size = rect2.size;
            this.m_Items.Clear();
            Toggle toggle = null;
            for (int i = 0; i < this.options.Count; i++)
            {
                DropdownWithColor.OptionData data = this.options[i];
                DropdownWithColor.DropdownItem item = this.AddItem(data, this.value == i, componentInChildren, this.m_Items);
                if (!(item == null))
                {
                    item.toggle.isOn = (this.value == i);
                    item.toggle.onValueChanged.AddListener(delegate (bool x)
                    {
                        this.OnSelectItem(item.toggle);
                    });
                    if (item.toggle.isOn)
                    {
                        item.toggle.Select();
                    }
                    if (toggle != null)
                    {
                        Navigation navigation = toggle.navigation;
                        Navigation navigation2 = item.toggle.navigation;
                        navigation.mode = Navigation.Mode.Explicit;
                        navigation2.mode = Navigation.Mode.Explicit;
                        navigation.selectOnDown = item.toggle;
                        navigation.selectOnRight = item.toggle;
                        navigation2.selectOnLeft = toggle;
                        navigation2.selectOnUp = toggle;
                        toggle.navigation = navigation;
                        item.toggle.navigation = navigation2;
                    }
                    toggle = item.toggle;
                }
            }
            Vector2 sizeDelta = rectTransform2.sizeDelta;
            sizeDelta.y = size.y * (float)this.m_Items.Count + vector.y - vector2.y;
            rectTransform2.sizeDelta = sizeDelta;
            float num = rectTransform.rect.height - rectTransform2.rect.height;
            if (num > 0f)
            {
                rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, rectTransform.sizeDelta.y - num);
            }
            Vector3[] array = new Vector3[4];
            rectTransform.GetWorldCorners(array);
            RectTransform rectTransform3 = canvas.transform as RectTransform;
            Rect rect3 = rectTransform3.rect;
            for (int j = 0; j < 2; j++)
            {
                bool flag = false;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 vector3 = rectTransform3.InverseTransformPoint(array[k]);
                    if (vector3[j] < rect3.min[j] || vector3[j] > rect3.max[j])
                    {
                        flag = true;
                        break;
                    }
                }
                if (flag)
                {
                    RectTransformUtility.FlipLayoutOnAxis(rectTransform, j, false, false);
                }
            }
            for (int l = 0; l < this.m_Items.Count; l++)
            {
                RectTransform rectTransform4 = this.m_Items[l].rectTransform;
                rectTransform4.anchorMin = new Vector2(rectTransform4.anchorMin.x, 0f);
                rectTransform4.anchorMax = new Vector2(rectTransform4.anchorMax.x, 0f);
                rectTransform4.anchoredPosition = new Vector2(rectTransform4.anchoredPosition.x, vector.y + size.y * (float)(this.m_Items.Count - 1 - l) + size.y * rectTransform4.pivot.y);
                rectTransform4.sizeDelta = new Vector2(rectTransform4.sizeDelta.x, size.y);
            }
            //this.AlphaFadeList(0.15f, 0f, 1f);
            this.m_Template.gameObject.SetActive(false);
            componentInChildren.gameObject.SetActive(false);
            this.m_Blocker = this.CreateBlocker(canvas);
        }

        /// <summary>
        ///   <para>Override this method to implement a different way to obtain a blocker GameObject.</para>
        /// </summary>
        /// <param name="rootCanvas">The root canvas the dropdown is under.</param>
        /// <returns>
        ///   <para>The obtained blocker.</para>
        /// </returns>
        protected virtual GameObject CreateBlocker(Canvas rootCanvas)
        {
            GameObject gameObject = new GameObject("Blocker");
            RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
            rectTransform.SetParent(rootCanvas.transform, false);
            rectTransform.anchorMin = Vector3.zero;
            rectTransform.anchorMax = Vector3.one;
            rectTransform.sizeDelta = Vector2.zero;
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            Canvas component = this.m_Dropdown.GetComponent<Canvas>();
            canvas.sortingLayerID = component.sortingLayerID;
            canvas.sortingOrder = component.sortingOrder - 1;
            gameObject.AddComponent<GraphicRaycaster>();
            Image image = gameObject.AddComponent<Image>();
            image.color = Color.clear;
            Button button = gameObject.AddComponent<Button>();
            button.onClick.AddListener(new UnityAction(this.Hide));
            return gameObject;
        }

        /// <summary>
        ///   <para>Override this method to implement a different way to dispose of a blocker GameObject that blocks clicks to other controls while the dropdown list is open.</para>
        /// </summary>
        /// <param name="blocker">The blocker to dispose of.</param>
        protected virtual void DestroyBlocker(GameObject blocker)
        {
            Object.Destroy(blocker);
        }

        /// <summary>
        ///   <para>Override this method to implement a different way to obtain a dropdown list GameObject.</para>
        /// </summary>
        /// <param name="template">The template to create the dropdown list from.</param>
        /// <returns>
        ///   <para>The obtained dropdown list.</para>
        /// </returns>
        protected virtual GameObject CreateDropdownList(GameObject template)
        {
            return Object.Instantiate<GameObject>(template);
        }

        /// <summary>
        ///   <para>Override this method to implement a different way to dispose of a dropdown list GameObject.</para>
        /// </summary>
        /// <param name="dropdownList">The dropdown list to dispose of.</param>
        protected virtual void DestroyDropdownList(GameObject dropdownList)
        {
            Object.Destroy(dropdownList);
        }

        protected virtual DropdownWithColor.DropdownItem CreateItem(DropdownWithColor.DropdownItem itemTemplate)
        {
            return Object.Instantiate<DropdownWithColor.DropdownItem>(itemTemplate);
        }

        protected virtual void DestroyItem(DropdownWithColor.DropdownItem item)
        {
        }

        private DropdownWithColor.DropdownItem AddItem(DropdownWithColor.OptionData data, bool selected, DropdownWithColor.DropdownItem itemTemplate, List<DropdownWithColor.DropdownItem> items)
        {
            DropdownWithColor.DropdownItem dropdownItem = this.CreateItem(itemTemplate);
            dropdownItem.rectTransform.SetParent(itemTemplate.rectTransform.parent, false);
            dropdownItem.gameObject.SetActive(true);
            dropdownItem.gameObject.name = "Item " + items.Count + ((data.text == null) ? string.Empty : (": " + data.text));
            if (dropdownItem.toggle != null)
            {
                dropdownItem.toggle.isOn = false;
            }
            if (dropdownItem.text)
            {
                dropdownItem.text.text = data.text;
            }
            if (dropdownItem.image)
            {
                dropdownItem.image.sprite = data.image;
                dropdownItem.image.enabled = (dropdownItem.image.sprite != null);
                dropdownItem.image.color = data.color;
            }
            items.Add(dropdownItem);
            return dropdownItem;
        }

        /*private void AlphaFadeList(float duration, float alpha)
        {
            CanvasGroup component = this.m_Dropdown.GetComponent<CanvasGroup>();
            this.AlphaFadeList(duration, component.alpha, alpha);
        }

        private void AlphaFadeList(float duration, float start, float end)
        {
            if (end.Equals(start))
            {
                return;
            }
            FloatTween info = new FloatTween
            {
                duration = duration,
                startValue = start,
                targetValue = end
            };
            info.AddOnChangedCallback(new UnityAction<float>(this.SetAlpha));
            info.ignoreTimeScale = true;
            this.m_AlphaTweenRunner.StartTween(info);
        }

        private void SetAlpha(float alpha)
        {
            if (!this.m_Dropdown)
            {
                return;
            }
            CanvasGroup component = this.m_Dropdown.GetComponent<CanvasGroup>();
            component.alpha = alpha;
        }*/

        /// <summary>
        ///   <para>Hide the dropdown list.</para>
        /// </summary>
        public void Hide()
        {
            if (this.m_Dropdown != null)
            {
                //this.AlphaFadeList(0.15f, 0f);
                base.StartCoroutine(this.DelayedDestroyDropdownList(0.15f));
            }
            if (this.m_Blocker != null)
            {
                this.DestroyBlocker(this.m_Blocker);
            }
            this.m_Blocker = null;
            this.Select();
        }

        [DebuggerHidden]
        private IEnumerator DelayedDestroyDropdownList(float delay)
        {
            /*Dropdown.< DelayedDestroyDropdownList > c__Iterator2 < DelayedDestroyDropdownList > c__Iterator = new Dropdown.< DelayedDestroyDropdownList > c__Iterator2();
            < DelayedDestroyDropdownList > c__Iterator.delay = delay;
            < DelayedDestroyDropdownList > c__Iterator.<$> delay = delay;
            < DelayedDestroyDropdownList > c__Iterator.<> f__this = this;
            return < DelayedDestroyDropdownList > c__Iterator;*/
            yield return new WaitForSeconds(delay);

            DestroyDropdownList(this.m_Dropdown);

        }

        private void OnSelectItem(Toggle toggle)
        {
            if (!toggle.isOn)
            {
                toggle.isOn = true;
            }
            int num = -1;
            Transform transform = toggle.transform;
            Transform parent = transform.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i) == transform)
                {
                    num = i - 1;
                    break;
                }
            }
            if (num < 0)
            {
                return;
            }
            this.value = num;
            this.Hide();
        }
    }
}
