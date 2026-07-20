using System.Xml.Serialization;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace UMA.Editors
{
    [ExecuteInEditMode] // ensures it runs in the Editor
    public class AnimationFramePreview : MonoBehaviour
    {
        [System.Serializable]
        public class AnimationPose
        {
            [XmlAttribute("ID")]
            public string ID = "";
            public int frame = 0;
        }

        public class PoseTransform
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        public AnimationClip clip;       // assign in Inspector
        [Range(0f, 1f)]
        public float normalizedTime = 0; // 0 = start, 1 = end
        private float lastTime = -1f;

        [SerializeField]
        public List<AnimationPose> poses = new List<AnimationPose>();

        void Update()
        {
            if (!Application.isPlaying && clip != null)
            {
                // Disable Animator so it doesn't override sampled pose
                var animator = GetComponent<Animator>();
                if (animator != null && animator.enabled)
                {
                    animator.enabled = false;
                }

                float time = normalizedTime * clip.length;
                if (time != lastTime)
                {
                    lastTime = time;
                    //Debug.Log($"Sampling clip '{clip.name}' at time {time} seconds. GameObject {gameObject.GetUmaObjectId()} name {gameObject.name}");
                    // Sample the clip at the chosen time
                    clip.SampleAnimation(gameObject, time);

                    // Force transforms to be marked dirty so Scene view updates
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(gameObject);
#endif
                }
            }
        }

        public void GotoFrame(float time)
        {
            if (clip != null && gameObject != null)
            {
                //Debug.Log($"[AnimationFramePreview] Going to time: {time} GameObject {gameObject.GetUmaObjectId()} name {gameObject.name}");
                clip.SampleAnimation(gameObject, time);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(gameObject);
                UnityEditor.SceneView.RepaintAll();
#endif
            }
        }

        public Dictionary<string, PoseTransform> GetPoseAtFrame(float time)
        {
            Dictionary<string, PoseTransform> poseAtFrame = new Dictionary<string, PoseTransform>();
            GotoFrame(time);
            poseAtFrame.Clear();
            foreach (Transform child in GetComponentsInChildren<Transform>())
            {
                PoseTransform p = new PoseTransform
                {
                    localPosition = child.localPosition,
                    localRotation = child.localRotation,
                    localScale = child.localScale
                };
                poseAtFrame[child.name] = p;
            }

            return poseAtFrame;
        }

#if UNITY_EDITOR
        public void SavePoseSet(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    serializer.Serialize(stream, poses);
                }
            }
        }

        public void LoadPoseSet(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var loadedPoses = serializer.Deserialize(stream) as List<AnimationPose>;
                    if (loadedPoses != null && loadedPoses.Count > 0)
                    {
                        poses = loadedPoses;
                    }
                    else
                    {
                        poses = new List<AnimationPose> { new AnimationPose() };
                    }
                }
                // Mark the component as dirty so Unity saves the changes
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}