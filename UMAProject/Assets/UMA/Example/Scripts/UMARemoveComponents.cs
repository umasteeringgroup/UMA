using UnityEngine;
using System.Collections;

namespace UMA
{
	public class UMARemoveComponents : MonoBehaviour
	{
		public string[] removeComponentNames;
		public string[] disableComponentNames;

		public void OnCharacterUpdate(UMA.UMAData data)
		{
			foreach (var componentName in removeComponentNames)
			{
				var component = data.animator.GetComponent(componentName);
				Destroy(component);
			}
			foreach (var componentName in disableComponentNames)
			{
				var behavior = (data.animator.GetComponent(componentName) as Behaviour);
				if (behavior != null) behavior.enabled = false;
			}


		}

	}
}