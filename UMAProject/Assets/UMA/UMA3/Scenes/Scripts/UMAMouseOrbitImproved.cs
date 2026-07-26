using UnityEngine;
using System.Collections;
using System.Collections.Generic;
//Added to detect whether the pointer is over a UI element...
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace UMA.Examples
{
    //DOS MODIFIED really we want this to 'look at' the target bone but move with the Global one
    //not sure how to do that though...
    //TODO if the user changes the size of the head to be really big we end up inside. We need this to calculate its minimum distance based on the size of thecapsule collider (assuming this is correct
    [AddComponentMenu("Camera-Control/Mouse Orbit with zoom")]
    public class UMAMouseOrbitImproved : MonoBehaviour
    {

        //DOS Modified added an option to choose which mose button to use (for touch you dont have a right button option)
        public enum mouseBtnOpts { Left = 0, Right = 1, Middle = 2 }
        public mouseBtnOpts mouseButtonToUse = mouseBtnOpts.Right;

        public Transform target;
        public float distance = 5.0f;
        public float xSpeed = 120.0f;
        public float ySpeed = 120.0f;
        public float scrollrate = 3.0f;
        public float yMinLimit = -20f;
        public float yMaxLimit = 80f;

        public float distanceMin = .5f;
        public float distanceMax = 15f;
        public Vector3 Offset;
        public bool AlwaysOn = false;
        public float ZoomSensitivity = 1.0f;

        [Tooltip("use this to enable the user to orbit the camera around the character on touchscreen devices")]
        public bool singleTouchOrbiting = true;
        [Tooltip("use this to enable the user to pinch to zoom the camera on touchscreen devices")]
        public bool pinchToZoom = true;


        public bool Clip;
        public enum targetOpts {  Head, Chest, Spine, Hips, LeftFoot, LeftHand, LeftLowerArm, LeftLowerLeg, LeftShoulder, LeftUpperArm, LeftUpperLeg, RightFoot, RightHand, RightLowerArm, RightLowerLeg, RightShoulder, RightUpperArm, RightUpperLeg }
        public targetOpts TargetBone;

        private string[] targetStrings = { "Head", "Chest", "Spine", "Hips", "LeftFoot", "LeftHand", "LeftLowerArm", "LeftLowerLeg", "LeftShoulder", "LeftUpperArm", "LeftUpperLeg", "RightFoot", "RightHand", "RightLowerArm", "RightLowerLeg", "RightShoulder", "RightUpperArm", "RightUpperLeg" };
        private UMAData umaData;
        private Rigidbody _rigidbody;
        private GameObject TargetGO;
        private UMAPlayerActions controls;
        private readonly List<TouchControl> activeTouches = new List<TouchControl>();

        bool switchingTarget = false;
        float smoothing = 7f;


        float defaultx, defaulty, defaultdistance;
        float x = 0.0f;
        float y = 0.0f;

        class TempTransform {
            public Vector3 position;
            public Quaternion rotation;
        }

        private void Awake()
        {
            controls = new UMAPlayerActions();
        }

        private void OnEnable()
        {
            controls?.Enable();
        }

        private void OnDisable()
        {
            controls?.Disable();
        }

        private void OnDestroy()
        {
            controls?.Dispose();
            controls = null;
        }

        // Use this for initialization
        void Start()
        {
            Vector3 angles = transform.eulerAngles;
            x = angles.y;
            y = angles.x;
            defaultx = x;
            defaulty = y;
            defaultdistance = distance;

            _rigidbody = GetComponent<Rigidbody>();

            // Make the rigid body not change rotation
            if (_rigidbody != null)
            {
                _rigidbody.freezeRotation = true;
            }
        }

        public void Reset()
        {
            x = defaultx;
            y = defaulty;
            distance = defaultdistance;
        }

        public void SwitchTarget(Transform _dstTarget)
        {
            StopAllCoroutines();
            target = null;
            switchingTarget = true;
            StartCoroutine(SwitchTargetCoroutine(_dstTarget));
        }

        IEnumerator SwitchTargetCoroutine(Transform _dstTarget)
        {
            yield return null;
            while (Vector3.Distance(transform.position, UpdatePos(_dstTarget).position) > 0.01f)
            {
                transform.position = Vector3.Lerp(transform.position, UpdatePos(_dstTarget).position, smoothing * Time.deltaTime);
                transform.rotation = Quaternion.Lerp(transform.rotation, UpdatePos(_dstTarget).rotation, smoothing * Time.deltaTime);
                
                yield return null;
            }
            switchingTarget = false;
            target = _dstTarget;
        }

        private Vector3 GetTarget(Transform dstTarget = null)
        {
            Transform t = target;
            if (dstTarget != null)
            {
                t = dstTarget;
            }

            //if (!string.IsNullOrEmpty(TargetBone))
            if (TargetBone >= 0)
            {
                if (dstTarget != null)
                {
                    umaData = dstTarget.GetComponent<UMAData>();
                }
                else
                {
                    umaData = target.GetComponent<UMAData>();
                }

                if (umaData != null && umaData.umaRecipe != null && umaData.umaRecipe.raceData != null && umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid && umaData.skeleton != null)
                {
                    string boneName = umaData.umaRecipe.raceData.TPose.BoneNameFromHumanName(targetStrings[(int)TargetBone]);
					if (!string.IsNullOrEmpty(boneName))
					{
						var bone = umaData.skeleton.GetBoneGameObject(Animator.StringToHash(boneName));
						if(bone != null)
                        {
                            t = bone.transform;
                        }
                    }
                }

                if (t == null)
                {
                    if (dstTarget != null)
                    {
                        Transform rendTrans = dstTarget.Find("UMARenderer");
                        if(rendTrans == null)
                        {
                            return dstTarget.position;
                        }

                        Renderer rend = rendTrans.GetComponent<Renderer>();
                        float height = rend.bounds.size.y;
                        distance = (height / 2) * 1.75f;
                        return dstTarget.Find("Root").position + new Vector3(0, height / 2, 0);
                    }
                    else
                    {
                        Transform rendTrans = target.Find("UMARenderer");
                        if(rendTrans == null)
                        {
                            return target.position;
                        }

                        Renderer rend = rendTrans.GetComponent<Renderer>();
                        float height = rend.bounds.size.y;
                        distance = (height / 2) * 1.75f;
                        return target.Find("Root").position + new Vector3(0, height / 2, 0);
                    }
                }
            }

            Vector3 dest = t.position + Offset;
            return dest;
        }

        void LateUpdate()
        {
            if (switchingTarget || target == null)
            {
                return;
            }

            UpdatePos();
        }

        TempTransform UpdatePos(Transform dstTarget = null)
        {
            TempTransform newTransform = new TempTransform();
            Vector3 tgt = GetTarget(dstTarget);

            // A generated UMA can temporarily resolve to its root position
            // while its skeleton is still being built or downloaded. Scene
            // targets such as a crowd container are allowed to sit at ground
            // level and should remain valid orbit targets.
            Transform activeTarget = dstTarget != null ? dstTarget : target;
            if (activeTarget != null &&
                activeTarget.GetComponent<UMAData>() != null &&
                tgt.y <= 0f)
            {
                return newTransform;
            }

            CollectActiveTouches();

            //DOS Modified tweaked this to be selectable
            if (IsOrbitButtonPressed() && activeTouches.Count == 0)
            {
                Vector2 look = controls != null ? controls.Player.Look.ReadValue<Vector2>() : Vector2.zero;
                x += look.x * xSpeed * 0.04f;
                y -= look.y * ySpeed * 0.02f;
            }
            else if (activeTouches.Count == 1)
            {
                var touchZero = activeTouches[0];
                if (AlwaysOn || !IsPointerOverUI(touchZero))
                {
                    Vector2 touchDelta = touchZero.delta.ReadValue();
                    x += touchDelta.x * (xSpeed / 5)/* * distance */ * 0.04f;
                    y -= touchDelta.y * (ySpeed / 5) * 0.02f;
                }
            }

            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null || AlwaysOn == true)
            {
                if (activeTouches.Count == 2 && pinchToZoom)
                {
                    // Store both touches.
                    var touchZero = activeTouches[0];
                    var touchOne = activeTouches[1];

                    Vector2 touchZeroPosition = touchZero.position.ReadValue();
                    Vector2 touchOnePosition = touchOne.position.ReadValue();
                    Vector2 touchZeroDelta = touchZero.delta.ReadValue();
                    Vector2 touchOneDelta = touchOne.delta.ReadValue();

                    // Find the position in the previous frame of each touch.
                    Vector2 touchZeroPrevPos = touchZeroPosition - touchZeroDelta;
                    Vector2 touchOnePrevPos = touchOnePosition - touchOneDelta;

                    // Find the magnitude of the vector (the distance) between the touches in each frame.
                    float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
                    float touchDeltaMag = (touchZeroPosition - touchOnePosition).magnitude;

                    // Find the difference in the distances between each frame. Flip it so it goes the right way
                    float deltaMagnitudeDiff = ((prevTouchDeltaMag - touchDeltaMag) * -1) * ZoomSensitivity;
                    distance = Mathf.Clamp(distance - (deltaMagnitudeDiff / 10) * (scrollrate / 10), distanceMin, distanceMax);
                }
                else
                {
                    var mouse = Mouse.current;
                    // Input System reports a typical mouse-wheel notch as +/-120 on Windows.
                    float scroll = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
                    distance = Mathf.Clamp(distance - scroll * scrollrate, distanceMin, distanceMax);
                }
            }


            y = ClampAngle(y, yMinLimit, yMaxLimit);


            Quaternion rotation = Quaternion.Euler(y, x, 0);

            if (Clip)
            {
                RaycastHit hit;
                if (Physics.Linecast(tgt, transform.position, out hit))
                {
                    distance -= hit.distance;
                }
            }
            Vector3 negDistance = new Vector3(0.0f, 0.0f, -distance);
            Vector3 position = rotation * negDistance + tgt;

            newTransform.rotation = rotation;
            newTransform.position = position;
            if (dstTarget == null)
            {
                transform.rotation = rotation;
                transform.position = position;
            }
            return newTransform;
        }

        private void CollectActiveTouches()
        {
            activeTouches.Clear();
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            for (int i = 0; i < touchscreen.touches.Count; i++)
            {
                var touch = touchscreen.touches[i];
                if (touch.press.isPressed)
                {
                    activeTouches.Add(touch);
                }
            }
        }

        private bool IsOrbitButtonPressed()
        {
            if (controls != null)
            {
                if (mouseButtonToUse == mouseBtnOpts.Left)
                {
                    return controls.Player.Shoot.IsPressed();
                }
                if (mouseButtonToUse == mouseBtnOpts.Right)
                {
                    return controls.Player.Undo.IsPressed();
                }
            }

            var mouse = Mouse.current;
            return mouse != null && mouse.middleButton.isPressed;
        }

        private static bool IsPointerOverUI(TouchControl touch)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue());
        }

        public static float ClampAngle(float angle, float min, float max)
        {
            // These must be "while" loops. It's possible at low framerate that we have moved more than 360 degrees.
            while (angle < -360F)
            {
                angle += 360F;
            }

            while (angle > 360F)
            {
                angle -= 360F;
            }

            return Mathf.Clamp(angle, min, max);
        }
    }
}
