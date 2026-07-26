using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Identifies runtime physics components owned by a UnitySpringJointAnimator.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class UnitySpringJointAnimatorBone : MonoBehaviour
    {
        [HideInInspector]
        public UnitySpringJointAnimator Owner;

        [HideInInspector]
        public UMAData UMAData;

        [HideInInspector]
        public Rigidbody OwnedRigidbody;

        [HideInInspector]
        public SpringJoint OwnedJoint;

        [HideInInspector]
        public SphereCollider OwnedCollider;

        [HideInInspector]
        public bool LayerWasChanged;

        [HideInInspector]
        public int OriginalLayer;
    }
}
