using UnityEngine;
using UnityEditor;
using System.Collections;
using UMA;


[CustomEditor(typeof(UMASaveTool))]
[CanEditMultipleObjects]
public class UMASaveToolEditor : Editor {
	
	public SerializedProperty avatarName;
	public SerializedProperty serializedAvatar;

	
    void OnEnable () {
        avatarName = serializedObject.FindProperty ("avatarName");
    }
	
	
	public override void OnInspectorGUI(){	
		serializedObject.Update();
		
		GUILayout.Label ("Avatar Name", EditorStyles.boldLabel);
		avatarName.stringValue = EditorGUILayout.TextArea(avatarName.stringValue);   
		
		GUILayout.Space(20);
		
		GUILayout.BeginHorizontal();
		if(GUILayout.Button("Save Avatar")){
			UMASaveTool umaSaveTool = (UMASaveTool)target;    
			GameObject gameObject = (GameObject)umaSaveTool.gameObject;
			UMADynamicAvatar umaDynamicAvatar = gameObject.GetComponent("UMADynamicAvatar") as UMADynamicAvatar;

			if(umaDynamicAvatar){
				umaDynamicAvatar.SaveToMemoryStream();
				var path = EditorUtility.SaveFilePanel("Save serialized Avatar","",avatarName.stringValue + ".txt","txt");
				if(path.Length != 0) {
					System.IO.File.WriteAllText(path, umaDynamicAvatar.streamedUMA);
				}
			}
		}
		
		if(GUILayout.Button("Load Avatar")){
			UMASaveTool umaSaveTool = (UMASaveTool)target;    
			GameObject gameObject = (GameObject)umaSaveTool.gameObject;
			UMAData umaData = gameObject.GetComponent("UMAData") as UMAData;
			UMADynamicAvatar umaDynamicAvatar = gameObject.GetComponent("UMADynamicAvatar") as UMADynamicAvatar;
			RaceData umaRace = umaData.umaRecipe.raceData;
			if(umaData && umaDynamicAvatar){
				var path = EditorUtility.OpenFilePanel("Load serialized Avatar","","txt");
				if (path.Length != 0) {
					umaDynamicAvatar.streamedUMA = System.IO.File.ReadAllText(path);
					umaDynamicAvatar.LoadFromMemoryStream();
					if(umaRace != umaData.umaRecipe.raceData){
						//Different race, we need to create it
						Transform tempParent = umaData.transform.parent.parent;
						
						UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
						
						if(umaData.umaRecipe.raceData.raceName == "HumanMale"){
							umaRecipe.raceData = umaDynamicAvatar.raceLibrary.raceDictionary["HumanMale"];
						}else if(umaData.umaRecipe.raceData.raceName == "HumanFemale"){
							umaRecipe.raceData = umaDynamicAvatar.raceLibrary.raceDictionary["HumanFemale"];
						}

			    		Transform tempUMA = (Instantiate(umaRecipe.raceData.racePrefab ,umaData.transform.position,umaData.transform.rotation) as GameObject).transform;
					
						UMAData newUMA = tempUMA.gameObject.GetComponentInChildren<UMAData>();
			        	newUMA.umaRecipe = umaRecipe;

						UMADynamicAvatar tempAvatar = newUMA.gameObject.AddComponent("UMADynamicAvatar") as UMADynamicAvatar;
						tempAvatar.Initialize();

						newUMA.gameObject.AddComponent("UMASaveTool");

						tempAvatar.streamedUMA = umaDynamicAvatar.streamedUMA;
						tempAvatar.umaPackRecipe = umaDynamicAvatar.umaPackRecipe;
						newUMA.umaRecipe = umaData.umaRecipe;
						
						newUMA.atlasResolutionScale = umaData.atlasResolutionScale;
						newUMA.Dirty(true, true, true);
						newUMA.transform.parent.gameObject.name = avatarName.stringValue;
						newUMA.transform.parent.transform.parent = tempParent;
						Destroy(umaData.transform.parent.gameObject);
					}else{					
						umaData.Dirty(true, true, true);
					}
				}
			}
		}
		
		GUILayout.EndHorizontal();
		
		GUILayout.Space(20);

		
		serializedObject.ApplyModifiedProperties();
	}
	
}
