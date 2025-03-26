using UnityEngine;
using UnityEngine.EventSystems;

namespace UMA
{

	public class Rotator : MonoBehaviour
	{
		bool rotating = false;
		bool gotLastPoint = false;
		public float scale = 0.1f;

		public GameObject rotateMe;

		Vector3 lastPoint = Vector3.negativeInfinity;

		// Start is called before the first frame update
		void Start()
		{

		}

		// Update is called once per frame
		void Update()
		{

		}

		void OnMouseDrag()
		{
			if (!EventSystem.current.IsPointerOverGameObject())
			{
				return;
			}
			if (rotateMe == null)
			{
				return;
			}

			if (!gotLastPoint)
			{
				gotLastPoint = true;
				lastPoint = Input.mousePosition;
				return;
			}

			Vector3 currentMousePoint = Input.mousePosition;
			float delta = (lastPoint.x - currentMousePoint.x) * scale;

			if (delta != 0.0f)
			{
				Vector3 localRotation = rotateMe.transform.localRotation.ToEuler();
				localRotation.y += delta;
				rotateMe.transform.localRotation = Quaternion.EulerAngles(localRotation);
			}
			lastPoint = Input.mousePosition;
		}
		void OnMouseDown()
		{
			rotating = true;
			lastPoint = Input.mousePosition;

		}

		public void OnMouseUp()
		{
			rotating = false;
		}
	}
}