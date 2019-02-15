using UnityEngine;
using System.Collections;
//using System.Linq;

namespace UMA.Examples
{
    public class EnvPresetChooser : MonoBehaviour
    {
        public Transform[] presets
        {
            get
            {
                int i = 0;
                Transform[] transforms = new Transform[transform.childCount];
                foreach(Transform t in transform)
                {
                    transforms[i++] = t;
                }
                return transforms;               
                //return transform.Cast<Transform>().ToArray();
            }
        }

        public int GetActivePreset()
        {
            for (int i = 0, n = transform.childCount; i < n; ++i)
                if (transform.GetChild(i).gameObject.activeSelf)
                    return i;

            return -1;
        }

        public void SetActivePreset(int index)
        {
            if (index < 0 || index >= transform.childCount)
            {
                Debug.LogWarning("Invalid index in SetActivePreset");
                return;
            }

            for (int i = 0, n = transform.childCount; i < n; ++i)
                transform.GetChild(i).gameObject.SetActive(false);

            transform.GetChild(index).gameObject.SetActive(true);
        }

        public void DumpAllScreens()
        {
            StartCoroutine(DoDumpAllScreens());
        }

        IEnumerator DoDumpAllScreens()
        {
            yield return null;

            var oldActive = GetActivePreset();

            SetActivePreset(oldActive);
        }
    }
}
