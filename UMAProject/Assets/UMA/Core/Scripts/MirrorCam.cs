using UnityEngine;

namespace UMA
{

    public class MirrorCam : MonoBehaviour
    {

        public Transform playerTarget;
        public Transform mirror;


        // Update is called once per frame
        void Update()
        {
            Vector3 localPlayer = mirror.transform.InverseTransformPoint(playerTarget.position);
            Vector3 lookatMirror = mirror.TransformPoint(new Vector3(localPlayer.x, localPlayer.y, localPlayer.z));
            transform.LookAt(lookatMirror, mirror.up);
        }
    }
}
