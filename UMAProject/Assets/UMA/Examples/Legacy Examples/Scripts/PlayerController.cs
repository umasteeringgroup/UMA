using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace UMA.Examples
{
    public class PlayerController : NetworkBehaviour
    {
        private Transform camContainer;
        private Transform umaTransform;
        private Vector3 target = Vector3.zero;
        private Animator animator;

        public void Start()
        {
            //Set up some stuff
            umaTransform = transform.GetChild(0);
            camContainer = umaTransform.Find("PlayerCam(Clone)");
            animator = umaTransform.GetComponent<Animator>();

            //If we are not the local player, disable the camera and disable script
            if (!isLocalPlayer)
            {
                enabled = false;
                camContainer.gameObject.SetActive(false);
                return;
            }
        }

        void Update()
        {
            //Check for mouse click on the "Plane" object, if detected and player is not close, move there
            //If player is closer than 0.5 meters, stop moving.
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 100.0f))
                {
                    if (hit.collider.name == "Plane")
                    {
                        target = hit.point;
                    }
                }
            }

            if (Vector3.Distance(target, umaTransform.position) > 0.75f)
                animator.SetFloat("Speed", 1);

            else
                animator.SetFloat("Speed", 0);

            //Rotate the player towards the target
            RotatePlayer();
        }

        //Make sure the camera is not rotated when the player is
        void LateUpdate()
        {
            //camContainer.position = myTransform.position + new Vector3(0, 5, -4);
            camContainer.eulerAngles = new Vector3(-45, 0, 0);
        }

        //Calculate and rotate towards the target
        private void RotatePlayer()
        {
            Vector3 targetVector = target - umaTransform.position;
            targetVector.y = 0;
            float angle = Vector3.Angle(Vector3.forward, targetVector);
            Vector3 cross = Vector3.Cross(Vector3.forward, targetVector);
            if (cross.y < 0)
                angle *= -1;
            Vector3 finalVector = umaTransform.rotation.eulerAngles;
            finalVector.y = angle;
            Quaternion rotTo = Quaternion.Euler(finalVector);
            umaTransform.rotation = Quaternion.Lerp(umaTransform.rotation, rotTo, Time.deltaTime * 10);
        }
    }
}
