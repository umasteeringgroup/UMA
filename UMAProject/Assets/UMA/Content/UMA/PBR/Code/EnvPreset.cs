using UnityEngine;

[ExecuteInEditMode]
public class EnvPreset : MonoBehaviour {
	public Material skyboxMaterial;
	public float	ambientIntensity = 1;
	public float	growthIndirectScale = 2f;
	public float	growthIndirectDirection = 0.5f;
	public float	growthDirectOcclusionBoost = 0.15f;
	public bool 	forceProbes = false;

	void OnEnable() {
		var buildReflectionProbes = false;

		if(RenderSettings.skybox != skyboxMaterial) {
			RenderSettings.skybox = skyboxMaterial;
			buildReflectionProbes = forceProbes;
		}

		if(RenderSettings.ambientIntensity != ambientIntensity) {
			RenderSettings.ambientIntensity = ambientIntensity;
			buildReflectionProbes = forceProbes;
		}

#if UNITY_EDITOR
		if(buildReflectionProbes) {
			var mi = typeof(UnityEditor.Lightmapping).GetMethod("BakeAllReflectionProbesSnapshots", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
			if(mi != null)
				mi.Invoke(null, null);
		}
#endif
	}
}
