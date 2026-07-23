using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

namespace UMA
{
    public class UMABoneVisualizer : MonoBehaviour
    {
        public UMAData umaData;
        public Transform rootNode;
        public bool DrawAsBones;
        public bool DrawAdjustBones;
        public bool AlwaysDrawGizmos = true;
        public bool DrawBoneNames;
        public Mesh BoneMesh;
        public string Filter;
        public Color BoneColor = new Color(0.1f, 0.65f, 1f, 1f);
        public Color RootBoneColor = Color.green;
        public Color SelectedBoneColor = Color.yellow;
        public float LineThickness = 3f;
        public float JointSize = 0.015f;

        void Start()
        {
            Setup();

            if (Application.isPlaying)
            {
#if UNITY_EDITOR
                Debug.LogWarning(string.Format("The BoneVisualizer on {0} is a helper component and should be removed for a final build.", gameObject.name));
#endif
            }
        }

        private void Setup()
        {
            if (umaData == null)
            {
                umaData = GetComponent<UMAData>();
                if (umaData == null)
                {
                    umaData = GetComponentInParent<UMAData>();
                }
                if (umaData == null)
                {
                    umaData = GetComponentInChildren<UMAData>();
                }
            }

            if (rootNode == null && umaData != null && umaData.skeleton != null)
            {
                rootNode = umaData.skeleton.GetRootTransform();
            }

            if (BoneMesh == null)
            {
                BoneMesh = Resources.Load<Mesh>("PlaceholderAssets/BoneMesh");
            }
        }

        private void OnDrawGizmos()
        {
            if (AlwaysDrawGizmos)
            {
                DrawBoneGizmos();
            }
        }

        void OnDrawGizmosSelected()
        {
            DrawBoneGizmos();
        }

        void DrawBoneGizmos()
        {
            if (umaData == null || umaData.skeleton == null)
            {
                Setup();
            }

            if (umaData == null || umaData.skeleton == null)
            {
                return;
            }

#if UNITY_EDITOR
            DrawSceneHandles();
#else
            DrawRuntimeGizmos();
#endif
        }

#if UNITY_EDITOR
        private void DrawSceneHandles()
        {
            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            try
            {
                foreach (UMASkeleton.BoneData bone in umaData.skeleton.boneHashData.Values)
                {
                    DrawBoneHandle(bone);
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private void DrawBoneHandle(UMASkeleton.BoneData bone)
        {
            if (bone == null || bone.boneTransform == null || !ShouldDrawBone(bone.boneTransform))
            {
                return;
            }

            Transform boneTransform = bone.boneTransform;
            Transform parentTransform = GetParentTransform(bone);
            Color color = GetBoneColor(boneTransform, parentTransform == null);
            float handleSize = HandleUtility.GetHandleSize(boneTransform.position);
            float jointSize = Mathf.Max(0.001f, JointSize * handleSize);

            using (new Handles.DrawingScope(color))
            {
                if (parentTransform != null)
                {
                    Handles.DrawAAPolyLine(Mathf.Max(1f, LineThickness), parentTransform.position, boneTransform.position);
                }

                Handles.SphereHandleCap(0, boneTransform.position, Quaternion.identity, jointSize, EventType.Repaint);

                if (DrawBoneNames)
                {
                    Handles.Label(boneTransform.position, boneTransform.name);
                }
            }
        }
#endif

        private void DrawRuntimeGizmos()
        {
            foreach (UMASkeleton.BoneData bone in umaData.skeleton.boneHashData.Values)
            {
                if (bone == null || bone.boneTransform == null || !ShouldDrawBone(bone.boneTransform))
                {
                    continue;
                }

                Transform parentTransform = GetParentTransform(bone);
                Gizmos.color = GetBoneColor(bone.boneTransform, parentTransform == null);
                if (parentTransform != null)
                {
                    Gizmos.DrawLine(parentTransform.position, bone.boneTransform.position);
                }
                Gizmos.DrawSphere(bone.boneTransform.position, JointSize);
            }
        }

        private Transform GetParentTransform(UMASkeleton.BoneData bone)
        {
            if (bone == null || umaData == null || umaData.skeleton == null)
            {
                return null;
            }

            UMASkeleton.BoneData parentBone;
            if (umaData.skeleton.boneHashData.TryGetValue(bone.parentBoneNameHash, out parentBone) && parentBone != null)
            {
                return parentBone.boneTransform;
            }

            return bone.boneTransform != null ? bone.boneTransform.parent : null;
        }

        private bool ShouldDrawBone(Transform boneTransform)
        {
            if (boneTransform == null)
            {
                return false;
            }

            if (!DrawAdjustBones && boneTransform.name.ToLowerInvariant().Contains("adjust"))
            {
                return false;
            }

            return string.IsNullOrEmpty(Filter) || boneTransform.name.ToLowerInvariant().Contains(Filter.ToLowerInvariant());
        }

        private Color GetBoneColor(Transform boneTransform, bool isRoot)
        {
#if UNITY_EDITOR
            if (boneTransform == Selection.activeTransform)
            {
                return SelectedBoneColor;
            }
#endif
            return isRoot ? RootBoneColor : BoneColor;
        }

        public void PopulateChildren()
        {
            Setup();
        }
    }
}
