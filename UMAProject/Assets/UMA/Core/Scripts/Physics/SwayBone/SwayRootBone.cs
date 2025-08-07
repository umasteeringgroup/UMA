using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public class SwayRootBone : SwayBone
	{
		public string AnchorBoneName; // The name of the bone that the first sway bone anchors to.
        public Transform anchorBoneTransform; // The bone that the first sway bone anchors to.

        [Tooltip("For debugging purposes. forces changes on all bones")]
		public bool UpdateChangesEachFrame = false;
		[Tooltip("Bones that you want to ignore - these and their children are not processed")]
		public List<Transform> Exclusions = new List<Transform>();
		private List<SwayBone> SwayBones = new List<SwayBone>();
#pragma warning disable CS0414
		private float step = 1.0f / 60.0f;
#pragma warning restore CS0414

		public void Setup(float elasticity, float inertia, float limit)
		{
			this.limit = limit;
            this.elasticity = elasticity;
            this.inertia = inertia;
            this.Reorient = false;
            this.OrientOnly = false;
			InitializeRoot();
        }

        public void InitializeRoot()
        {
            if (anchorBoneTransform == null)
            {
                // Find the anchor bone transform
                anchorBoneTransform = transform.parent;
                if (anchorBoneTransform == null)
                {
                    Debug.LogError("No parent found for SwayRootBone. Please set the AnchorBoneName or assign a parent.");
                    return;
                }
            }
            SwayBones.Clear();
            AddChildBones(anchorBoneTransform, true);
        }

        public void SetupBoneChains()
		{
			AddChildBones(transform, true);
		}

		private void AddChildBones(Transform transform, bool toplevel)
		{
			foreach (Transform t in transform)
			{
				if (Exclusions.Contains(t))
				{
					continue;
				}

				SwayBone sb = t.gameObject.GetComponent<SwayBone>();
				if (sb == null)
				{
					sb = t.gameObject.AddComponent<SwayBone>();
				}
				sb.elasticity = elasticity;
				sb.inertia = inertia;
				sb.limit = limit;
				sb.OrientOnly = OrientOnly;
				sb.Reorient = Reorient;
				sb.isTopLevel = toplevel;
				sb.Initialize();
				SwayBones.Add(sb);
				if (t.childCount > 0)
				{
					AddChildBones(t, false);
				}
			}
		}

		public void UpdateRootBone(float step)
		{
			for (int i = 0; i < SwayBones.Count; i++)
			{
				SwayBone sb = SwayBones[i];
				sb.DoUpdate(step);
				if (UpdateChangesEachFrame)
				{
					sb.elasticity = elasticity;
					sb.inertia = inertia;
					sb.limit = limit;
					sb.OrientOnly = OrientOnly;
					sb.Reorient = Reorient;
				}
			}
		}
	}
}