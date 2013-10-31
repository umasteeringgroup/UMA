using UnityEngine;
using UnityEditor;
using System.Collections;


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
			UMAData umaData = gameObject.GetComponent("UMAData") as UMAData;
			
			if(umaData){
				umaData.SaveToMemoryStream();
				var path = EditorUtility.SaveFilePanel("Save serialized Avatar","",avatarName.stringValue + ".txt","txt");
				if(path.Length != 0) {
					System.IO.File.WriteAllText(path, umaData.streamedUMA);
				}
			}
		}
		
		if(GUILayout.Button("Load Avatar")){
			UMASaveTool umaSaveTool = (UMASaveTool)target;    
			GameObject gameObject = (GameObject)umaSaveTool.gameObject;
			UMAData umaData = gameObject.GetComponent("UMAData") as UMAData;
			RaceData umaRace = umaData.umaRecipe.raceData;
			if(umaData){
				var path = EditorUtility.OpenFilePanel("Load serialized Avatar","","txt");
				if (path.Length != 0) {
					umaData.streamedUMA = System.IO.File.ReadAllText(path);
					umaData.LoadFromMemoryStream();
					if(umaRace != umaData.umaRecipe.raceData){
						//Different race, we need to create it
						Transform tempParent = umaData.transform.parent.parent;
						
						UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
						
						if(umaData.umaRecipe.raceData.raceName == "HumanMale"){
							umaRecipe.raceData = umaData.raceLibrary.raceDictionary["HumanMale"];
						}else if(umaData.umaRecipe.raceData.raceName == "HumanFemale"){
							umaRecipe.raceData = umaData.raceLibrary.raceDictionary["HumanFemale"];
						}

			    		Transform tempUMA = (Instantiate(umaRecipe.raceData.racePrefab ,umaData.transform.position,umaData.transform.rotation) as GameObject).transform;
					
						UMAData newUMA = tempUMA.gameObject.GetComponentInChildren<UMAData>();
			        	newUMA.umaRecipe = umaRecipe;
						
						newUMA.streamedUMA = umaData.streamedUMA;
						newUMA.umaPackRecipe = umaData.umaPackRecipe;
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