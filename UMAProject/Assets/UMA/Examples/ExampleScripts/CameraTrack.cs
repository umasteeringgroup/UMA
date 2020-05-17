using UnityEngine;
using System.Collections;

namespace UMA.Examples
{
    public class CameraTrack : MonoBehaviour
    {

        public Transform target;

        void Update()
        {
            if (target && Input.GetAxis("Mouse ScrollWheel") != 0)
            {
                var movementVector = (target.transform.position - transform.position) / 10;
                movementVector.y = 0;
                if (Input.GetAxis("Mouse ScrollWheel") < 0) // back
                {
                    transform.position = transform.position + movementVector;
                }
                if (Input.GetAxis("Mouse ScrollWheel") > 0) // forward
                {
                    transform.position = transform.position - movementVector;
                }
            }
        }

        void LateUpdate()
        {
            if (target)
            {
                Vector3 relative = transform.InverseTransformPoint(target.position);
                float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                transform.Rotate(0, angle, 0, Space.World);
            }
        }
    }
}
