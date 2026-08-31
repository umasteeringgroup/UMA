using UMA.Dismemberment;
using UnityEngine;

/// <summary>uGUI-compatible sample action that restores a dismembered UMA character.</summary>
public sealed class UndoDismemberments : MonoBehaviour
{
    [Tooltip("Avatar containing UmaDismemberment. This is only used when Dismemberment is empty.")]
    public GameObject avatar;
    [Tooltip("The character to restore. When empty, Avatar and then the single active scene " +
        "dismemberment component are tried.")]
    public UmaDismemberment dismemberment;
    [Tooltip("Rebuild the current UMA recipe after restoring the original renderer meshes. " +
        "Keep this enabled for a complete skeleton, animation, and physics reset.")]
    public bool rebuildAvatar = true;

    public void OnClick()
    {
        Undo();
    }

    public void Undo()
    {
        ResolveDismemberment();
        if (dismemberment == null)
        {
            Debug.LogError("Undo Dismemberment could not find a target. Assign the avatar or " +
                "UmaDismemberment component on the button action.", this);
            return;
        }

        if (!dismemberment.TryUndoDismemberment(out string failure, rebuildAvatar))
            Debug.LogWarning($"Could not completely undo dismemberment: {failure}",
                dismemberment);
    }

    private void ResolveDismemberment()
    {
        if (dismemberment != null) return;
        if (avatar != null)
        {
            dismemberment = avatar.GetComponent<UmaDismemberment>();
            if (dismemberment == null)
                dismemberment = avatar.GetComponentInChildren<UmaDismemberment>(true);
            if (dismemberment == null)
                dismemberment = avatar.GetComponentInParent<UmaDismemberment>(true);
        }
        if (dismemberment != null) return;

        UmaDismemberment[] candidates = FindObjectsByType<UmaDismemberment>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (candidates.Length == 1)
        {
            dismemberment = candidates[0];
        }
        else if (candidates.Length > 1)
        {
            Debug.LogError("Undo Dismemberment found multiple UMA characters. Assign the " +
                "intended Avatar or Dismemberment reference explicitly.", this);
        }
    }
}
