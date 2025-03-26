using UnityEngine;

namespace UMA
{
    public class UMABoneVisualizer : MonoBehaviour
    {

        public Transform rootNode;
        public bool DrawAsBones;
        public bool DrawAdjustBones;
        public bool AlwaysDrawGizmos;
        public Mesh BoneMesh;
        public string Filter;
        private string lastFilter;

        private Transform[] childNodes;
        private Vector3 Scale = new Vector3();

        void Start()
        {
            if (rootNode == null || BoneMesh == null)
            {
                Setup();
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning(string.Format("The BoneVisualizer on {0} is a helper component and should be removed for a final build.", gameObject.name));
            }
        }

        /// <summary>
        /// Find the root node and the Bone mesh if they aren't setup on the component.
        /// </summary>
        private void Setup()
        {
            if (rootNode == null)
            {
                rootNode = RecursiveFindBone(this.gameObject.transform, "Hips");
            }

            if (BoneMesh == null)
            {
                BoneMesh = Resources.Load<Mesh>("PlaceholderAssets/BoneMesh");
            }
        }

        /// <summary>
        /// Find a bone in the hierarchy
        /// </summary>
        /// <param name="bone"></param>
        /// <param name="boneName"></param>
        /// <returns></returns>
        private Transform RecursiveFindBone(Transform bone, string boneName)
        {
            if (bone.name == boneName)
            {
                return bone;
            }

            for (int i = 0; i < bone.childCount; i++)
            {
                var result = RecursiveFindBone(bone.GetChild(i), boneName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void OnDrawGizmos()
        {
            if (AlwaysDrawGizmos)
            {
                DrawBoneGizmos();
            }
        }

        /// <summary>
        /// Draw the bones
        /// </summary>
        void OnDrawGizmosSelected()
        {
            DrawBoneGizmos();
        }

        void DrawBoneGizmos()
        {
            if (rootNode == null)
            {
                Setup();
            }

            if (rootNode != null)
            {
                if (childNodes == null || childNodes.Length == 0)
                {
                    //get all joints to draw
                    PopulateChildren();
                }


                foreach (Transform child in childNodes)
                {
                    if (transform == null)
                    {
                        Setup();
                        return;
                    }
                    if (child == rootNode)
                    {
                        //list includes the root, if root then larger, green cube
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(child.position, 0.01f);
                    }
                    else
                    {
                        if (!DrawAdjustBones)
                        {
                            if (child.gameObject.name.ToLower().Contains("adjust"))
                            {
                                continue;
                            }
                        }
                        if (!string.IsNullOrEmpty(Filter))
                        {
                            if (!child.gameObject.name.ToLower().Contains(Filter.ToLower()))
                            {
                                continue;
                            }
                        }
                        if (DrawAsBones && BoneMesh != null)
                        {
                            float BoneLength = Vector3.Distance(child.position, child.parent.position);

                            Scale.Set(BoneLength / 10.0f, BoneLength / 10.0f, BoneLength);
                            Vector3 relativePos = child.transform.position - child.parent.transform.position;

                            if (relativePos.magnitude < 0.001f)
                            {
                                continue;
                            }

                            Quaternion rotation = (relativePos == Vector3.zero) ? Quaternion.identity : Quaternion.LookRotation(relativePos);
#if UNITY_EDITOR
                            if (child == UnityEditor.Selection.activeTransform)
                            {
                                Gizmos.color = Color.yellow;
                            }
#endif
                            Gizmos.DrawMesh(BoneMesh, child.parent.position, rotation, Scale);
                            Gizmos.color = Color.green;
                        }
                        else
                        {
                            Gizmos.color = Color.blue;
                            Gizmos.DrawLine(child.position, child.parent.position);
                            Gizmos.DrawCube(child.position, new Vector3(.01f, .01f, .01f));
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Cache the bones
        /// </summary>
        public void PopulateChildren()
        {
            if (rootNode != null)
            {
                childNodes = rootNode.GetComponentsInChildren<Transform>();
            }
        }
    }
}