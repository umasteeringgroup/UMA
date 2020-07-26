using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	//OnPreviewGUI
	//http://timaksu.com/post/126337219047/spruce-up-your-custom-unity-inspectors-with-a
	//
	[CustomEditor(typeof(MeshHideAsset))]
	public class MeshHideInspector : Editor 
	{
		private Mesh _meshPreview;
		private UMAMeshData _meshData;
		private PreviewRenderUtility _previewRenderUtility;
		private Vector2 _drag;
		private Material _material;
		private bool _autoInitialize = true;

		private int selectedRaceIndex = 0;
		private List<RaceData> foundRaces = new List<RaceData>();
		private List<string> foundRaceNames = new List<string>();

		void OnEnable()
		{
			MeshHideAsset source = target as MeshHideAsset;

			SetRaceLists();

			if (source.asset == null)
				return;

			if( _material == null )
				_material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

			if (_meshPreview == null)
			{
				UpdateMeshPreview();
			}

			if (_previewRenderUtility == null)
			{
				_previewRenderUtility = new PreviewRenderUtility();
				ResetPreviewCamera();
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			MeshHideAsset source = target as MeshHideAsset;
			bool beginSceneEditing = false;

			//DrawDefaultInspector();
			SlotDataAsset obj = EditorGUILayout.ObjectField("SlotDataAsset", source.asset, typeof(SlotDataAsset), false) as SlotDataAsset;
			if (obj != source.asset)
			{
				source.asset = obj as SlotDataAsset;
				if (_autoInitialize)
				{
				  UpdateSourceAsset(obj);
				}
			}
			EditorGUILayout.LabelField("Slot Name", source.AssetSlotName.ToString());
			if (source.HasReference)
			{
				EditorGUILayout.HelpBox("Warning: This Mesh Hide Asset contains a reference. It should be freed so the referenced asset is not included in the build.", MessageType.Warning);
				if (GUILayout.Button("Free Reference"))
				{
					source.FreeReference();
					EditorUtility.SetDirty(source);
					AssetDatabase.SaveAssets();
				}
			}

			_autoInitialize = EditorGUILayout.Toggle(new GUIContent("AutoInitialize (recommended)", "Checking this will auto initialize the MeshHideAsset when a slot is added (recommended).  " +
				"For users that are rebuilding slots that don't change the geometry, the slot reference will be lost but can be reset without losing the existing MeshHide information by unchecking this." ),_autoInitialize);

			if (source.asset == null)
				EditorGUILayout.HelpBox("No SlotDataAsset set! Begin by adding a SlotDataAsset to the object field above.", MessageType.Error);


			//Race Selector here
			GUILayout.Space(20);
			selectedRaceIndex = EditorGUILayout.Popup("Select Base Slot by Race", selectedRaceIndex, foundRaceNames.ToArray());
			if( selectedRaceIndex <= 0)
			{
				EditorGUILayout.HelpBox("Quick selection of base slots by race. This is not needed to create a mesh hide asset, any slot can be used.", MessageType.Info);
			}
			else
			{
				UMAData.UMARecipe baseRecipe = new UMAData.UMARecipe();
				foundRaces[selectedRaceIndex].baseRaceRecipe.Load(baseRecipe, UMAContextBase.Instance);

				foreach(SlotData sd in baseRecipe.slotDataList)
				{
					if (sd != null && sd.asset != null)
					{
						if( GUILayout.Button(string.Format("{0} ({1})", sd.asset.name, sd.slotName)))
						{
							if( UpdateSourceAsset(sd.asset))
								selectedRaceIndex = 0;
						}
					}
				}                
			}

			GUILayout.Space(20);
			if (source.TriangleCount > 0)
			{
				EditorGUILayout.LabelField("Triangle Indices Count: " + source.TriangleCount);
				EditorGUILayout.LabelField("Submesh Count: " + source.SubmeshCount);
				if (source.asset != null)
				{
					EditorGUILayout.LabelField("Current Submesh: " + source.asset.subMeshIndex);
				}
				EditorGUILayout.LabelField("Hidden Triangle Count: " + source.HiddenCount);
			}
			else
				EditorGUILayout.LabelField("No triangle array found");

			GUILayout.Space(20);
			if (!GeometrySelectorWindow.IsOpen)
			{
				EditorGUI.BeginDisabledGroup(source.asset == null);
				if (GUILayout.Button("Begin Editing", GUILayout.MinHeight(50)))
				{
					if (source.asset != null)
						beginSceneEditing = true;
				}
				EditorGUI.EndDisabledGroup();
				GUILayout.Space(20);
				GUILayout.Label("Editing will be done in an empty scene.");
				GUILayout.Label("You will be prompted to save the scene");
				GUILayout.Label("if there are any unsaved changes.");
			}
			serializedObject.ApplyModifiedProperties();

			if (beginSceneEditing)
			{
				// This has to happen outside the inspector
				EditorApplication.delayCall += CreateSceneEditObject;
			}
		}
    
		private bool UpdateSourceAsset( SlotDataAsset newObj )
		{
			MeshHideAsset source = target as MeshHideAsset;
			bool update = false;

			if (source.asset != null)
			{
				if (EditorUtility.DisplayDialog("Warning", "Setting a new source slot will clear the existing data on this asset!", "OK", "Cancel"))
					update = true;
			}
			else
			{
				update = true;
			}

			if(update)
			{
				source.AssetSlotName = newObj.slotName;
				source.Initialize();
				UpdateMeshPreview();
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(target);
				return true;
			}
			return false;
		}

		private void UpdateMeshPreview()
		{
			MeshHideAsset source = target as MeshHideAsset;
			if (source.asset == null)
			{
				_meshPreview = null;
				return;
			}

			if (_meshPreview == null)
			{
				_meshPreview = new Mesh();
#if UMA_32BITBUFFERS
				_meshPreview.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
			}

			UpdateMeshData( source.triangleFlags);

			_meshPreview.Clear();
			_meshPreview.vertices = _meshData.vertices;
			_meshPreview.subMeshCount = _meshData.subMeshCount;

			for(int i = 0; i < _meshData.subMeshCount; i++)
				_meshPreview.SetTriangles(_meshData.submeshes[i].triangles, i);

			ResetPreviewCamera();
		}

		public void UpdateMeshData( BitArray[] triangleFlags )
		{
			if (triangleFlags == null)
			{
				Debug.LogWarning("UpdateMeshData: triangleFlags are null!");
				return;
			}

			if (_meshData == null )
				_meshData = new UMAMeshData();

			MeshHideAsset source = target as MeshHideAsset;

			UMAMeshData sourceData = source.asset.meshData;
				
			_meshData.submeshes = new SubMeshTriangles[sourceData.subMeshCount];
			_meshData.subMeshCount = sourceData.subMeshCount;

			bool has_normals = (sourceData.normals != null && sourceData.normals.Length != 0);

			_meshData.vertices = new Vector3[sourceData.vertexCount];
			sourceData.vertices.CopyTo(_meshData.vertices, 0);

			if(has_normals)
			{
				_meshData.normals = new Vector3[sourceData.vertexCount];
				sourceData.normals.CopyTo(_meshData.normals, 0);
			}

			for (int i = 0; i < sourceData.subMeshCount; i++)
			{
				List<int> newTriangles = new List<int>();
				for (int j = 0; j < triangleFlags[i].Count; j++)
				{
					if (!triangleFlags[i][j])
					{
						newTriangles.Add(sourceData.submeshes[i].triangles[(j*3) + 0]);
						newTriangles.Add(sourceData.submeshes[i].triangles[(j*3) + 1]);
						newTriangles.Add(sourceData.submeshes[i].triangles[(j*3) + 2]);
					}
				}
				_meshData.submeshes[i] = new SubMeshTriangles();
				_meshData.submeshes[i].triangles = new int[newTriangles.Count];
				newTriangles.CopyTo(_meshData.submeshes[i].triangles);
			}
		}

		private void CreateSceneEditObject()
		{
			MeshHideAsset source = target as MeshHideAsset;
			if (source.asset == null)
				return;

			if (GeometrySelectorWindow.IsOpen)
			{
				return;
			}

			if (GeometrySelectorExists())
			{
				GameObject.DestroyImmediate(GameObject.Find("GeometrySelector").gameObject);
			}

			bool hasDirtyScenes = false;

			for (int i = 0; i < EditorSceneManager.sceneCount; i++)
			{
				Scene sc = EditorSceneManager.GetSceneAt(i);
				if (sc.isDirty)
					hasDirtyScenes = true;
			}
			if (hasDirtyScenes)
			{
				int saveChoice = EditorUtility.DisplayDialogComplex("Modified scenes detected", "Opening the Mesh Hide Editor will close all scenes and create a new blank scene. Any current scene changes will be lost unless saved.", "Save and Continue", "Continue without saving", "Cancel");

				switch (saveChoice)
				{
					case 0: // Save and continue
					{
						if (!EditorSceneManager.SaveOpenScenes())
							return;
						break;
					}
					case 1: // don't save and continue
						break;
					case 2: // cancel
						return;
				}
			}

			SceneView sceneView = SceneView.lastActiveSceneView;

			if (sceneView == null)
			{
				EditorUtility.DisplayDialog("Error", "A Scene View must be open and active", "OK");
				return;
			}

			SceneView.lastActiveSceneView.Focus();

			List<GeometrySelector.SceneInfo> currentscenes = new List<GeometrySelector.SceneInfo>();

			for (int i = 0; i < EditorSceneManager.sceneCount; i++)
			{
				Scene sc = EditorSceneManager.GetSceneAt(i);
				GeometrySelector.SceneInfo si = new GeometrySelector.SceneInfo();
				si.path = sc.path;
				si.name = sc.name;
				if (i == 0)
				{
					// first scene should clear the temp scene.
					si.mode = OpenSceneMode.Single;
				}
				else
				{
					si.mode = sc.isLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading;
				}
				currentscenes.Add(si); 
			}

#if UNITY_2019_1_OR_NEWER
			Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
#else
			Scene s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
#endif
			EditorSceneManager.SetActiveScene(s);
			GameObject obj = EditorUtility.CreateGameObjectWithHideFlags("GeometrySelector", HideFlags.DontSaveInEditor); 
			GeometrySelector geometry = obj.AddComponent<GeometrySelector>();

			if (geometry != null)
			{
				Selection.activeGameObject = obj;
				SceneView.lastActiveSceneView.FrameSelected(true); 

				geometry.meshAsset = source;
				geometry.restoreScenes = currentscenes;
				geometry.currentSceneView = sceneView;

#if UNITY_2019_1_OR_NEWER
				geometry.SceneviewLightingState = sceneView.sceneLighting;
				sceneView.sceneLighting = false;
#else
				geometry.SceneviewLightingState = sceneView.m_SceneLighting;
				sceneView.m_SceneLighting = false;
#endif
				geometry.InitializeFromMeshData(source.asset.meshData);


				geometry.selectedTriangles = new BitArray(source.triangleFlags[source.asset.subMeshIndex]);

				geometry.UpdateSelectionMesh();
				SceneView.FrameLastActiveSceneView();
				SceneView.lastActiveSceneView.FrameSelected(); 
			}
		}

 
		private bool GeometrySelectorExists()
		{
			if (GameObject.Find("GeometrySelector") != null)
				return true;

			return false;
		}
			
		public override bool HasPreviewGUI()
		{
			MeshHideAsset source = target as MeshHideAsset;
			if (source.asset == null)
				return false;
			
			return true;
		}

		public override void OnPreviewGUI(Rect r, GUIStyle background)
		{
			if (_meshPreview == null)
				return;

			if( _material == null )
				_material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

			_drag = Drag2D(_drag, r);

			if (_previewRenderUtility == null)
			{
				_previewRenderUtility = new PreviewRenderUtility();
				ResetPreviewCamera();
			}

			if( Event.current.type == EventType.Repaint )
			{
				_previewRenderUtility.BeginPreview(r, background);
				_previewRenderUtility.DrawMesh(_meshPreview, Vector3.zero, Quaternion.identity, _material, 0);

				_previewRenderUtility.camera.transform.position = Vector2.zero;
				_previewRenderUtility.camera.transform.rotation = Quaternion.Euler(new Vector3(-_drag.y, -_drag.x, 0));
				_previewRenderUtility.camera.transform.position = _previewRenderUtility.camera.transform.forward * -6f;
				_previewRenderUtility.camera.transform.position +=  _meshPreview.bounds.center;

				_previewRenderUtility.camera.Render();

				Texture resultRender = _previewRenderUtility.EndPreview();

				GUI.DrawTexture(r, resultRender, ScaleMode.StretchToFill, false);
			}
		}

		public void ResetPreviewCamera()
		{
			if (_previewRenderUtility == null)
				return;

			MeshHideAsset source = target as MeshHideAsset;
			
			_drag = Vector2.zero;
			if( source.asset.meshData.rootBoneHash == UMAUtils.StringToHash("Global"))
				_drag.y = -90;
			
			_previewRenderUtility.camera.transform.position = new Vector3(0, 0, -6);
			_previewRenderUtility.camera.transform.rotation = Quaternion.identity;
		}

		public override void OnPreviewSettings()
		{
			if (GUILayout.Button("Refresh", EditorStyles.whiteMiniLabel))
				UpdateMeshPreview();
		}

		void OnDestroy()
		{
			if( _previewRenderUtility != null )
				_previewRenderUtility.Cleanup();
		}

		public static Vector2 Drag2D(Vector2 scrollPosition, Rect position)
		{
			int controlID = GUIUtility.GetControlID("Slider".GetHashCode(), FocusType.Passive);
			Event current = Event.current;
			switch (current.GetTypeForControl(controlID))
			{
				case EventType.MouseDown:
					if (position.Contains(current.mousePosition) && position.width > 50f)
					{
						GUIUtility.hotControl = controlID;
						current.Use();
						EditorGUIUtility.SetWantsMouseJumping(1);
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlID)
					{
						GUIUtility.hotControl = 0;
					}
					EditorGUIUtility.SetWantsMouseJumping(0);
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlID)
					{
						scrollPosition -= current.delta * (float)((!current.shift) ? 1 : 3) / Mathf.Min(position.width, position.height) * 140f;
						scrollPosition.y = Mathf.Clamp(scrollPosition.y, -90f, 90f);
						current.Use();
						GUI.changed = true;
					}
					break;
			}
			return scrollPosition;
		}

		public void SetRaceLists()
		{
			UMAContextBase ubc = UMAContext.Instance;
			if (ubc != null)
			{
				RaceData[] raceDataArray = ubc.GetAllRaces();
				foundRaces.Clear();
				foundRaceNames.Clear();
				foundRaces.Add(null);
				foundRaceNames.Add("None Set");
				foreach (RaceData race in raceDataArray)
				{
					if (race != null && race.raceName != "RaceDataPlaceholder")
					{
						foundRaces.Add(race);
						foundRaceNames.Add(race.raceName);
					}
				}
			}
		}
	}
}
