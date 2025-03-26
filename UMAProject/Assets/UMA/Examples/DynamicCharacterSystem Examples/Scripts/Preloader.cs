using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UMA_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif
using UnityEngine.UI;

namespace UMA
{

    public class Preloader : MonoBehaviour
    {
#if UMA_ADDRESSABLES
    [Header("Addressable Labels to preload")]
    public List<string> Labels;
#else
        [Header("The preloader requres addressables to run")]
        public List<string> Labels = new List<string>();
#endif
        [Header("Loading Slider to update")]
        public Slider LoadingSlider;
        [Header("Object to activate on completion")]
        public GameObject ActivateOnCompletion;

#if UMA_ADDRESSABLES
    private AsyncOperationHandle op;
#endif
        // Start is called before the first frame update
        public void Start()
        {
#if UMA_ADDRESSABLES
        StartCoroutine(Initialize());
#else
            if (ActivateOnCompletion != null)
            {
                ActivateOnCompletion.SetActive(true);
            }
#endif
        }

        private void PreloadLogger(string s)
        {
            Debug.Log($"{Time.realtimeSinceStartup} Message: {s} ");
        }


        private IEnumerator Initialize()
        {
            yield return new WaitForSeconds(1);
            PreloadLogger("Starting Initialize");
            InitAddressables();
        }

        private async void InitAddressables()
        {
#if UMA_ADDRESSABLES
            PreloadLogger("Initializing Addressables");
            op = Addressables.InitializeAsync();
            await op.Task;
            PreloadLogger($"Addressables Initialized");
            if (Labels.Count > 0)
            {
                op = Addressables.DownloadDependenciesAsync(Labels, Addressables.MergeMode.Union, false);
                op.Completed += Op_Completed;
            }
            else
            {
                if (ActivateOnCompletion != null)
                {
                    ActivateOnCompletion.SetActive(true);
                }
                if (LoadingSlider != null)
                {
                    LoadingSlider.gameObject.SetActive(false);
                }
            }
            PreloadLogger("Downloading Dependencies completed" );
#else
                if (LoadingSlider != null)
                {
                    LoadingSlider.gameObject.SetActive(false);
                }
#endif
        }

#if UMA_ADDRESSABLES
        void Update()
    {
        if (LoadingSlider != null && LoadingSlider.isActiveAndEnabled && op.IsValid())
        {
            LoadingSlider.value = op.PercentComplete;
            Text t = LoadingSlider.gameObject.GetComponentInChildren<Text>();
            t.text = op.Status.ToString() + " " + op.PercentComplete.ToString("P") + " Complete";
        }
    }

        private void Op_Completed(AsyncOperationHandle obj)
        {
            if (obj.Status == AsyncOperationStatus.Succeeded)
            {
                if (ActivateOnCompletion != null)
                {
                    ActivateOnCompletion.SetActive(true);
                }
            }
            else
            {
                Debug.Log("Preloader error: " + obj.Status);
            }
            if (LoadingSlider != null)
            {
                LoadingSlider.gameObject.SetActive(false);
            }
            Addressables.Release(obj);
        }
#endif
    }
}
