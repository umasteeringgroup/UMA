using UnityEngine;

namespace UMA.Examples
{
	using UnityEngine.InputSystem;

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
		private UMAPlayerActions controls;

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

		void Update()
		{
			if (controls != null && (controls.Player.Shoot.IsPressed() || controls.Player.Undo.IsPressed()))
			{
				Vector2 look = controls.Player.Look.ReadValue<Vector2>();
				rotation.x += look.x * sensitivityX;
				rotation.y -= look.y * sensitivityY;

				rotation.y = ClampAngle(rotation.y, yMinLimit, yMaxLimit);
				transform.localRotation = Quaternion.Euler(rotation.y, rotation.x, 0);
			}

			float speed = forwardSpeed;
			if (controls != null && controls.Player.Run.IsPressed())
			{
				speed *= runMultiplier;
			}

			Vector2 move = controls != null ? controls.Player.Move.ReadValue<Vector2>() : Vector2.zero;
			if (!Mathf.Approximately(move.y, 0f))
			{
				ChangePosition(move.y * speed);
			}
			if (!Mathf.Approximately(move.x, 0f))
			{
				if (strafeMode)
				{
					StrafePosition(move.x * speed);
				}
				else
				{
					rotation.x = ClampAngle(rotation.x + move.x * keyRotationSpeed * Time.deltaTime);
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
