using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Integrations
{
	public static class PowerToolsIntegration
	{
		private static Type powerPackPersistance;
		private static Type GetPowerPackPersistanceType()
		{
			if (powerPackPersistance == null)
			{
				foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				{
					powerPackPersistance = assembly.GetType("UMA.PowerTools.PowerPackPersistance");
					if (powerPackPersistance != null) break;
				}
			}
			return powerPackPersistance;
		}
		private static Type umaEditorAvatarType;
		private static Type GetUMAEditorAvatarType()
		{
			if (umaEditorAvatarType == null)
			{
				foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				{
					umaEditorAvatarType = assembly.GetType("UMA.PowerTools.PowerPackPersistance");
					if (umaEditorAvatarType != null) break;
				}
			}
			return umaEditorAvatarType;
		}


		private static UnityEngine.Object GetPowerPackPersistanceInstance()
		{
			var method = GetPowerPackPersistanceType().GetMethod("GetInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			return method.Invoke(null, null) as UnityEngine.Object;
		}
		private static void ReleasePowerPackPersistanceInstance(UnityEngine.Object instance)
		{
			var method = powerPackPersistance.GetMethod("Release");
			method.Invoke(instance, null);
		}

		public static bool HasPowerTools()
		{
			return GetPowerPackPersistanceType() != null;
		}

		public static GameObject GetPreview(UMARecipeBase recipeBase)
		{
			return GameObject.Find("PowerTools_" + recipeBase.name);
		}

		public static bool HasPreview(UMARecipeBase recipeBase)
		{
			return GetPreview(recipeBase) != null;
		}

		public static void Show(UMARecipeBase recipeBase)
		{
			var go = new GameObject("PowerTools_" + recipeBase.name);
			go.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
			var avatar = go.AddComponent<UMADynamicAvatar>();
			avatar.umaRecipe = recipeBase;

			var persistance = GetPowerPackPersistanceType();
			var showAvatarMethod = persistance.GetMethod("ShowAvatar", new Type[] { typeof(UMAAvatarBase) });
			var instance = GetPowerPackPersistanceInstance();
			showAvatarMethod.Invoke(instance, new object[] { avatar });
			ReleasePowerPackPersistanceInstance(instance);
			SetAvatarDestroyParent(avatar, true);
#if UNITY_EDITOR
			go.transform.position = UnityEditor.SceneView.lastActiveSceneView.pivot - new Vector3(0,1,0);
#endif
		}

		private static void SetAvatarDestroyParent(UMADynamicAvatar avatar, bool destroyParent)
		{
			var umaEditorAvatarType = GetUMAEditorAvatarType();
			var umaEditorAvatar = avatar.GetComponentInChildren(umaEditorAvatarType);
			umaEditorAvatarType.GetField("destroyParent").SetValue(umaEditorAvatar, destroyParent);
		}

		public static void Hide(UMARecipeBase recipeBase)
		{
			var avatar = GetPreview(recipeBase).GetComponent<UMADynamicAvatar>();
			var persistance = GetPowerPackPersistanceType();
			var hideAvatarMethod = persistance.GetMethod("HideAvatar", new Type[] { typeof(UMAAvatarBase) });
			var instance = GetPowerPackPersistanceInstance();
			hideAvatarMethod.Invoke(instance, new object[] { avatar });
			ReleasePowerPackPersistanceInstance(instance);
		}

		public static void Refresh(UMARecipeBase recipeBase)
		{
			var avatar = GetPreview(recipeBase).GetComponent<UMADynamicAvatar>();
			var persistance = GetPowerPackPersistanceType();
			SetAvatarDestroyParent(avatar, false);
			var hideAvatarMethod = persistance.GetMethod("HideAvatar", new Type[] { typeof(UMAAvatarBase) });
			var instance = GetPowerPackPersistanceInstance();
			hideAvatarMethod.Invoke(instance, new object[] { avatar });
			var showAvatarMethod = persistance.GetMethod("ShowAvatar", new Type[] { typeof(UMAAvatarBase) });
			showAvatarMethod.Invoke(instance, new object[] { avatar });
			SetAvatarDestroyParent(avatar, true);
			ReleasePowerPackPersistanceInstance(instance);
		}

		public static void HideAll()
		{
			var persistance = GetPowerPackPersistanceType();
			var instance = GetPowerPackPersistanceInstance();
			var hideAllMethod = persistance.GetMethod("HideAll");
			hideAllMethod.Invoke(instance, null);
			ReleasePowerPackPersistanceInstance(instance);
		}

	}
}
