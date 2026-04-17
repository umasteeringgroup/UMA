using UnityEngine;

namespace UMA.Examples
{
	[AddComponentMenu("Camera-Control/Simple Scene Walker")]
	public class SceneWalker : MonoBehaviour
	{
		public bool flyMode = false;
		public bool strafeMode = false;
		public float forwardSpeed = 1.0f;
		public float runMultiplier = 3.0f;
		public float mouseSpeed = 1.5f;
		public float sensitivityX = 2f;
		public float sensitivityY = 2f;
		public float keyRotationSpeed = 60f;

		public float yMinLimit = -60f;
		public float yMaxLimit = 60f;

		Vector3 rotation = new Vector3(0, 0, 0);

		Quaternion originalRotation;

		void Update()
		{
			if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
			{
				// Read the mouse input axis
				rotation.x += Input.GetAxis("Mouse X") * sensitivityX;
				rotation.y -= Input.GetAxis("Mouse Y") * sensitivityY;

				rotation.y = ClampAngle(rotation.y, yMinLimit, yMaxLimit);
				transform.localRotation = Quaternion.Euler(rotation.y, rotation.x, 0);
			}

			float speed = forwardSpeed;
			if (Input.GetKey(KeyCode.LeftShift))
			{
				speed *= runMultiplier;
			}

			if (Input.GetKey(KeyCode.W))
			{
				ChangePosition(speed);
			}
			if (Input.GetKey(KeyCode.S))
			{
				ChangePosition(0 - speed);
			}
			if (Input.GetKey(KeyCode.A))
			{
				if (strafeMode)
				{
					StrafePosition(-speed);
				}
				else
				{
					rotation.x = ClampAngle(rotation.x - keyRotationSpeed * Time.deltaTime);
					transform.localRotation = Quaternion.Euler(rotation.y, rotation.x, 0);
				}
			}
			if (Input.GetKey(KeyCode.D))
			{
				if (strafeMode)
				{
					StrafePosition(speed);
				}
				else
				{
					rotation.x = ClampAngle(rotation.x + keyRotationSpeed * Time.deltaTime);
					transform.localRotation = Quaternion.Euler(rotation.y, rotation.x, 0);
				}
			}
		}

		void ChangePosition(float Speed)
		{
			Vector3 NewPosition = transform.position + Camera.main.transform.forward * Speed * Time.deltaTime;
			if (!flyMode)
            {
                NewPosition.y = transform.position.y;
            }

            transform.position = NewPosition;
		}

		void StrafePosition(float Speed)
		{
			Vector3 NewPosition = transform.position + Camera.main.transform.right * Speed * Time.deltaTime;
			if (!flyMode)
            {
                NewPosition.y = transform.position.y;
            }

            transform.position = NewPosition;
		}

		void Start()
		{
			Vector3 euler = transform.eulerAngles;
			rotation.x = -euler.y;
			rotation.y = euler.x;
		}

		public static float ClampAngle(float angle)
		{
			// first, need to make sure it wraps correctly.
			while (angle < 0.0F)
            {
                angle += 360F;
            }

            while (angle > 360F)
            {
                angle -= 360F;
            }

            return angle;
		}

		public static float ClampAngle(float angle, float min, float max)
		{
			// first, need to make sure it wraps correctly.
			while (angle < -360F)
            {
                angle += 360F;
            }

            while (angle > 360F)
            {
                angle -= 360F;
            }
            // once it wraps, then we clamp.
            return Mathf.Clamp(angle, min, max);
		}
	}
}