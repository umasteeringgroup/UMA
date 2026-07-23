using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
namespace UMA
{
    public class UMAMountedItemSample : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public GameObject swordPrefab;
        private string InstantiatedItemName;
        bool mounted = false;
        
        public List<RuntimeAnimatorController> UnmountedAnimators = new List<RuntimeAnimatorController>();

        public List<RuntimeAnimatorController> MountedAnimators = new List<RuntimeAnimatorController>();


        public void Start()
        {
            UMAMountedItem umi = swordPrefab.GetComponent<UMAMountedItem>();
            if (umi != null)
            {
                InstantiatedItemName = swordPrefab.name + "_" + umi.ID;
            }
        }

        public int currentAnimator;
        public void OnPoseClick()
        {
            List<RuntimeAnimatorController> animators = mounted ? MountedAnimators : UnmountedAnimators;
            if (mounted)
            {
                
            }
            if (animators.Count > 0)
            {
                currentAnimator++;
                if (currentAnimator >= animators.Count)
                {
                    currentAnimator = 0;
                }
                RuntimeAnimatorController controller = animators[currentAnimator];
                if (avatar != null)
                {
                    avatar.animator.runtimeAnimatorController = controller;
                    avatar.animationController = controller;
                    if (avatar.raceAnimationControllers != null)
                        avatar.raceAnimationControllers.defaultAnimationController = controller;
                }
            }
        }


        public void MountSword()
        {
            if (string.IsNullOrEmpty(InstantiatedItemName))
            {
                return;
            }

            var item = GetItemIfMounted(swordPrefab, InstantiatedItemName);
            if (item == null)
            {
                GameObject go = GameObject.Instantiate(swordPrefab, avatar.gameObject.transform);
                go.name = InstantiatedItemName;
                go.SetActive(true);
            }
            avatar.animator.runtimeAnimatorController = MountedAnimators[0];
            avatar.animationController = MountedAnimators[0];
            if (avatar.raceAnimationControllers != null)
                avatar.raceAnimationControllers.defaultAnimationController = MountedAnimators[0];
            mounted = true;
        }

        public void UnMountSword()
        {
            if (string.IsNullOrEmpty(InstantiatedItemName))
            {
                return;
            }

            var item = GetItemIfMounted(swordPrefab, InstantiatedItemName);
            if (item != null)
            {
                GameObject.Destroy(item);
            }
            avatar.animator.runtimeAnimatorController = UnmountedAnimators[0];
            avatar.animationController = UnmountedAnimators[0];
            if (avatar.raceAnimationControllers != null)
                avatar.raceAnimationControllers.defaultAnimationController = UnmountedAnimators[0];            
            mounted = false;    
        }

        private GameObject GetItemIfMounted(GameObject go, string Name)
        {
            var mountedItems = avatar.gameObject.GetComponentsInChildren<UMAMountedItem>();
            foreach (var item in mountedItems)
            {
                // Don't mount more than once.
                if (item.gameObject.name == Name)
                {
                    return item.gameObject;
                }
            }
            return null;
        }
    }
}
