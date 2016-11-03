using UnityEngine;

namespace UMACharacterSystem
{
	public class DynamicCharacterSystemBase : MonoBehaviour
	{
		//just here really so that there is something that can be assigned in UMAContext. 
		//We can make some methods available here tho
		public virtual void Awake() { }

		public virtual void OnEnable() { }

		public virtual void Start() { }

		public virtual void Refresh(bool forceUpdateRaceLibrary = true, string bundleToGather = "") { }

		public virtual void Update() { }

		public virtual void Init() { }
		//Unfortunately we cant do anything useful that uses UMATextRecipe because that doesn't exist here
	}
}
