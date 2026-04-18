using UMA.CharacterSystem;
using UnityEngine;
namespace UMA
{
    public class UMAMountedItemSample : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public GameObject swordPrefab;
        private string InstantiatedItemName;

        public void Start()
        {
            UMAMountedItem umi = swordPrefab.GetComponent<UMAMountedItem>();
            if (umi != null)
            {
                InstantiatedItemName = swordPrefab.name + "_" + umi.ID;
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
