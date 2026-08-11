using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace UMA
{
    public class Preloader : MonoBehaviour
    {
        [Header("Addressable Labels to preload")]
        public List<string> Labels;
        [Header("Loading Slider to update")]
        public Slider LoadingSlider;
        [Header("Object to activate on completion")]
        public GameObject ActivateOnCompletion;

        private AsyncOperationHandle op;

        public void Start()
        {
            StartCoroutine(Initialize());
        }

        private void PreloadLogger(string message)
        {
            Debug.Log($"{Time.realtimeSinceStartup} Message: {message} gameobject {gameObject.name}");
        }

        private IEnumerator Initialize()
        {
            yield return new WaitForSeconds(1);
            PreloadLogger("Starting Initialize");
            InitAddressables();
        }

        private async void InitAddressables()
        {
            PreloadLogger("Initializing Addressables " + gameObject.name);
            op = Addressables.InitializeAsync();
            await op.Task;
            PreloadLogger("Addressables Initialized");
            if (Labels.Count > 0)
            {
                op = Addressables.DownloadDependenciesAsync(Labels, Addressables.MergeMode.Union, false);
                op.Completed += OpCompleted;
            }
            else
            {
                CompleteLoading();
            }
            PreloadLogger("Downloading Dependencies completed " + gameObject.name);
        }

        private void Update()
        {
            if (LoadingSlider == null || !LoadingSlider.isActiveAndEnabled || !op.IsValid()) return;
            LoadingSlider.value = op.PercentComplete;
            Text text = LoadingSlider.gameObject.GetComponentInChildren<Text>();
            if (text != null)
                text.text = op.Status + " " + op.PercentComplete.ToString("P") + " Complete";
        }

        private void OpCompleted(AsyncOperationHandle operation)
        {
            if (operation.Status == AsyncOperationStatus.Succeeded)
                CompleteLoading();
            else
                Debug.Log("Preloader error: " + operation.Status);
            Addressables.Release(operation);
        }

        private void CompleteLoading()
        {
            if (ActivateOnCompletion != null) ActivateOnCompletion.SetActive(true);
            if (LoadingSlider != null) LoadingSlider.gameObject.SetActive(false);
        }
    }
}
