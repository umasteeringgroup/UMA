using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    public class UMAMaterialAnimator : MonoBehaviour
    {
        public string slotTag = "AnimateMaterial"; // Any slots with this tag will be animated.

        public enum MaterialAnimationType
        {
            Float,
            Color
        }
        [System.Serializable]
        public class MaterialAnimation
        {
#if UNITY_EDITOR
            public bool show = true;               // If true, the animation is shown in the inspector.
#endif
            public MaterialAnimationType type;     // The type of animation. 
            public string overlayTag;              // The tag of the overlay to animate (or empty for all overlays).
            public string propertyName;            // The name of the property to animate.
            public AnimationCurve curve = new AnimationCurve(); // The curve that is used to animate the property.

            public bool useChannel;                // If true, the channel number is used. If false, the property is animated directly.
            public int channelNumber;              // The overlay number to animate, if any. 0 = base, 1 = first overlay, etc.

            public float MinFloatValue;            // The minimum value of the property.
            public float MaxFloatValue;            // The maximum value of the property.
            public Color MinColorValue;            // The minimum value of the property.
            public Color MaxColorValue;            // The maximum value of the property.

            public override string ToString()
            {
                if (type == MaterialAnimationType.Float)
                {
                    return $"{propertyName} Float {MinFloatValue} {MaxFloatValue}";
                }
                else
                {
                    return $"{propertyName} Color {ColorUtility.ToHtmlStringRGBA(MinColorValue)} {ColorUtility.ToHtmlStringRGBA(MaxColorValue)}";
                }
            }

            public void Apply(MaterialAnimationInstance instance, float time, int propertyIndex = 0)
            {
                if (MaterialAnimationType.Float == type)
                {
                    ApplyFloat(instance, time, MinFloatValue, MaxFloatValue, propertyIndex);
                }
                else
                {
                    ApplyColor(instance, time, MinColorValue, MaxColorValue, propertyIndex);
                }
            }

            public void ApplyColor(MaterialAnimationInstance mat, float time, Color MinValue, Color MaxValue, int propertyIndex = 0)
            {
                Color value = (curve.Evaluate(time) * MaxValue) + MinValue;
                // First see if it has a shader-level property 
                if (mat.material.HasProperty(propertyName))
                {
                    mat.material.SetColor(propertyName, value);
                }
                // Then see if it has a layer-level property
                string layerPropertyName = $"propertyName{mat.layer}";
                if (mat.material.HasProperty(propertyName))
                {
                    mat.material.SetColor(propertyName, value);
                }
                // only if we are using a channel do we need to set the channel property
                if (useChannel)
                {
                    string layerChannelPropertyName = $"propertyName{mat.layer}_{channelNumber}";
                    if (mat.material.HasProperty(layerChannelPropertyName))
                    {
                        mat.material.SetColor(layerChannelPropertyName, value);
                    }
                }
            }


            public void ApplyFloat(MaterialAnimationInstance mat, float time, float MinValue, float MaxValue, int propertyIndex = 0)
            {
                float value = (curve.Evaluate(time) * MaxValue) + MinValue;
                // First see if it has a shader-level property
                if (mat.material.HasProperty(propertyName))
                {
                    mat.material.SetFloat(propertyName, value);
                }
                // Then see if it has a layer-level property
                string layerPropertyName = $"propertyName{mat.layer}";
                if (mat.material.HasProperty(propertyName))
                {
                    mat.material.SetFloat(propertyName, value);
                }
                // only if we are using a channel do we need to set the channel property
                if (useChannel)
                {
                    string layerChannelPropertyName = $"propertyName{mat.layer}_{channelNumber}";
                    if (mat.material.HasProperty(layerChannelPropertyName))
                    {
                        mat.material.SetFloat(layerChannelPropertyName, value);
                    }
                }
            }

        }

        [SerializeField]
        public List<MaterialAnimation> animations = new List<MaterialAnimation>();

        private bool initialized = false;

        public class MaterialAnimationInstance
        {
            public MaterialAnimation animation; // The animation that this instance is using.
            public Material material;           // The material that is being animated.
            public SlotData slot;               // The slot that is being animated.
            public int layer;                   // The layer that is being animated.
        }

        private List<MaterialAnimationInstance> instances = new List<MaterialAnimationInstance>();

        // Start is called before the first frame update
        void Start()
        {
            Initialize();
        }

        // Setup the event to find/update the slot material
        public void Initialize()
        {
            DynamicCharacterAvatar avatar = GetComponentInParent<DynamicCharacterAvatar>();
            if (avatar != null)
            {
                if (avatar.umaData != null)
                {
                    avatar.umaData.CharacterUpdated.AddListener(OnCharacterUpdated);
                    initialized = true;
                }
            }
        }

        private void OnCharacterUpdated(UMAData umaData)
        {
            var slots = umaData.umaRecipe.GetIndexedSlotsByTag();
            var renderers = umaData.GetRenderers();
            // Generate a fresh list of material instances
            instances.Clear();
            for (int i = 0; i < animations.Count; i++)
            {
                MaterialAnimation anim = animations[i];
                if (anim != null)
                {
                    if (slots.ContainsKey(slotTag))
                    {
                        List<SlotData> slotlist = slots[slotTag];
                        if (slotlist != null)
                        {
                            // For each slot with the tag
                            // Get the slot data
                            // and then process the overlays to see if any need to be animated.
                            foreach (SlotData slot in slotlist)
                            {
                                // Get the renderer for this slot
                                var renderer = renderers[slot.skinnedMeshRenderer];
                                if (renderer != null)
                                {
                                    // Get the material for this slot
                                    Material material = renderer.sharedMaterials[slot.submeshIndex];
                                    if (material != null)
                                    {
                                        var ovls = slot.GetOverlayList();
                                        for (int layer = 0; layer < ovls.Count; layer++)
                                        {
                                            var ovl = ovls[layer];
                                            // if we DO have an Overlay
                                            // AND we have no overlayTag OR the overlay has the overlayTag
                                            // then add the animation instance
                                            if (ovl != null && (string.IsNullOrEmpty(anim.overlayTag) || ovl.HasTag(anim.overlayTag)))
                                            {
                                                MaterialAnimationInstance instance = new MaterialAnimationInstance();
                                                instance.animation = anim;
                                                instance.material = material;
                                                instance.slot = slot;
                                                instance.layer = layer;
                                                instances.Add(instance);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (!initialized)
            {
                Initialize();
                if (!initialized)
                {
                    return;
                }
            }

            for (int i = 0; i < instances.Count; i++)
            {
                MaterialAnimationInstance anim = instances[i];
                if (anim != null)
                {
                    anim.animation.Apply(anim, Time.time, i);
                }
            }
        }
    }
}