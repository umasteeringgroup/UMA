using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UMA_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif
using UnityEngine.UI;
public class Preloader : MonoBehaviour
{
    public string[] Labels;
    public Slider LoadingSlider;
#if UMA_ADDRESSABLES
    private AsyncOperationHandle op;
#endif
    // Start is called before the first frame update
    void Start()
    {
#if UMA_ADDRESSABLES
        op = Addressables.DownloadDependenciesAsync(Labels, Addressables.MergeMode.Union, false);
        op.Completed += Op_Completed;
        //await op.Task;
#else
        Debug.Log("Addressables is not defined.");
        Text t = LoadingSlider.gameObject.GetComponentInChildren<Text>();
        t.text = "Sample requires addressables to run.";
#endif
    }

#if UMA_ADDRESSABLES
void Update()
    {
        LoadingSlider.value = op.PercentComplete;
        Text t = LoadingSlider.gameObject.GetComponentInChildren<Text>();
        t.text = op.Status.ToString();
    }

private void Op_Completed(AsyncOperationHandle obj)
    {
        if (obj.Status == AsyncOperationStatus.Succeeded)
        {
            LoadingSlider.gameObject.SetActive(false);
        }
        else
        {
            Text t = LoadingSlider.gameObject.GetComponentInChildren<Text>();
            t.text = obj.Status.ToString();
        }
    }
#endif
}
