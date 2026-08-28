using UMA.Dismemberment;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Minimal uGUI sample for the UMA 3 dismemberment API.</summary>
[RequireComponent(typeof(Button))]
public sealed class GUIDismemberment : MonoBehaviour
{
    [Tooltip("Legacy scene reference. The component is resolved from this avatar when needed.")]
    public GameObject avatar;
    public UmaDismemberment dismemberment;
    public HumanBodyBones boneToSlice;

    private Button button;

    private void OnEnable()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
        ResolveDismemberment();
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(OnClick);
    }

    private void ResolveDismemberment()
    {
        if (dismemberment == null && avatar != null)
            dismemberment = avatar.GetComponent<UmaDismemberment>();
    }

    private void OnClick()
    {
        ResolveDismemberment();
        if (dismemberment == null)
        {
            Debug.LogError("UmaDismemberment was not assigned or found on the avatar.", this);
            return;
        }
        if (!dismemberment.TrySlice(boneToSlice, out _, out string failure))
            Debug.LogWarning($"Could not dismember {boneToSlice}: {failure}", dismemberment);
    }
}
