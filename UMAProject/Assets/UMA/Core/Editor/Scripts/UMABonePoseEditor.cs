using UnityEngine;
using UnityEditor.IMGUI.Controls;
using UMA.CharacterSystem;
using System;
using UMA.Editors;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA.PoseTools
{
	public class BoneTreeView : TreeView
	{
		public TreeViewItem RootNode;
		public int NodeCount;

		public BoneTreeView(TreeViewState treeViewState)
			: base(treeViewState)
		{

		}

		/*
		public TreeViewItem FindNode(TreeViewItem root, string Name)
		{
			if (root.children == null)
				return null;

			foreach(TreeViewItem ti in root.children)
			{
				if (ti.displayName == Name)
					return ti;
			}
			return null;
		} */

	    public List<string> GetSelectedBones()
		{
			List<string> boneNames = new List<string>();
			IList<int> boneIDs = GetSelection();
			if (boneIDs == null) return boneNames;
			if (boneIDs.Count == 0) return boneNames;

			foreach(int i in boneIDs)
			{
				TreeViewItem tvi = FindItem(i, RootNode);
				if (tvi != null)
				{
					boneNames.Add(tvi.displayName);
				}
			}
			return boneNames;
		}

		public void Initialize(string RootName)
		{
			RootNode = new TreeViewItem(0,-1, RootName);
			NodeCount = 0;
		}

		/*
		public void AddBone(string BoneName,int level)
		{
			string[] Keywords = BoneName.SplitCamelCase();
			if (Keywords.Length == 1)
			{
				TreeViewItem tv = new TreeViewItem(NodeCount++, 1 , BoneName);
				RootNode.AddChild(tv);
				NodeCount++;
				return;
			}

			TreeViewItem FirstLevel = FindNode(RootNode,Keywords[0]);
			if (FirstLevel == null)
			{
				FirstLevel = new TreeViewItem(NodeCount++, 1, Keywords[0]);
				RootNode.AddChild(FirstLevel);
			}

			TreeViewItem childNode = new TreeViewItem(NodeCount++, 2, BoneName);
			FirstLevel.AddChild(childNode);
		}
		*/

		protected override TreeViewItem BuildRoot()
		{
			if (RootNode == null)
			{
				RootNode = new TreeViewItem(0,-1, "Root");
			}
			SetupDepthsFromParentsAndChildren(RootNode);
			return RootNode;
		}
	}

	[CustomEditor(typeof(UMABonePose),true)]
    public class UMABonePoseEditor : Editor
    {
		//When an UMABonePose is inspected at runtime (using the 'Inspect' button drawn by the property drawer)
		//other tools can access the livePopupEditor property to set the 'sourceUMA' so that all the fancy edit tools work
		//The property drawer makes sure there is only ever one of these open at any one time, so you know if you change
		//any fields on the editor defined here you are changing them for that instance of the editor
		private static UMABonePoseEditor _livePopupEditor = null;
		public static int MirrorAxis = 1;
		public static string[] MirrorAxises = {"X Axis (raw)","Y Axis (UMA Internal)", "Z Axis" };
		public static UMAData saveUMAData;
		// HACK for testing
		public UMAData sourceUMA;
		TreeViewState treeState;
		BoneTreeView boneTreeView;

		UMABonePose targetPose = null;
		public UMABonePoseEditorContext context = null;

		const int BAD_INDEX = -1;
		public bool haveValidContext
		{
			get { return ((context != null) && (context.activeUMA != null)); }
		}
		public bool haveEditTarget
		{
			get { return (editBoneIndex != BAD_INDEX); }
		}

		private float previewWeight = 1.0f;

		public bool dynamicDNAConverterMode = false;

		const float addRemovePadding = 20f;
		const float buttonVerticalOffset = 4f; // Can't be calculated because button layout is weird.

		private int drawBoneIndex = BAD_INDEX;
		private int editBoneIndex = BAD_INDEX;
		private int activeBoneIndex = BAD_INDEX;
		private int mirrorBoneIndex = BAD_INDEX;
		private bool mirrorActive = true;

//		private bool inspectorLocked = false;

		private bool doBoneAdd = false;
		private bool doBoneRemove = false;
		private int removeBoneIndex = BAD_INDEX;
		private int addBoneIndex = BAD_INDEX;
		const int minBoneNameLength = 4;
		private string addBoneName = "";
		private List<string> addBoneNames = new List<string>();
		private Vector2 scrollPosition;
		private string filter = "";
		private string lastFilter = "";
		private bool filtered = false;
		private string highlight = "";


		private static Texture warningIcon;
//		private static Texture trashIcon;

		private static GUIContent positionGUIContent = new GUIContent(
			"Position",
			"The change in this bone's local position when pose is applied.");
		private static GUIContent rotationGUIContent = new GUIContent(
			"Rotation",
			"The change in this bone's local rotation when pose is applied.");
		private static GUIContent scaleGUIContent = new GUIContent(
			"Scale",
			"The change in this bone's local scale when pose is applied.");
		private static GUIContent scaleWarningGUIContent = new GUIContent(
			"WARNING: Non-uniform scale.",
			"Non-uniform scaling can cause errors on bones that are animated. Use only with adjustment bones.");
		private static GUIContent removeBoneGUIContent = new GUIContent(
			"Remove Bone",
			"Remove the selected bone from the pose.");
		private static GUIContent addBoneGUIContent = new GUIContent(
			"Add Bone",
			"Add the selected bone into the pose.");
		private static GUIContent previewGUIContent = new GUIContent(
			"Preview Weight",
			"Amount to apply bone pose to preview model. Inactive while editing.");

		/// <summary>
		/// Returns the last UMABonePose editor that was created when the 'Inspect' button next to an UMABonePose object field was pressed
		/// </summary>
		public static UMABonePoseEditor livePopupEditor
		{
			get { return _livePopupEditor; }
		}

		/// <summary>
		/// Sets the UMABoneBoseEditor that should be returned when 'livePopupEditor' is requested. Usually this should only be used by the UMABonePose PropertyDrawer
		/// </summary>
		/// <param name="liveUBPEditor"></param>
		public static void SetLivePopupEditor(UMABonePoseEditor liveUBPEditor)
		{
			if(Application.isPlaying)
				_livePopupEditor = liveUBPEditor;
		}

		public void OnEnable()
		{
			if (saveUMAData != null)
				sourceUMA = saveUMAData;

			if (treeState == null)
				treeState = new TreeViewState();

			boneTreeView = new BoneTreeView(treeState);

			targetPose = target as UMABonePose;
//			inspectorLocked = ActiveEditorTracker.sharedTracker.isLocked;
//			ActiveEditorTracker.sharedTracker.isLocked = true;
			EditorApplication.update += this.OnUpdate;
#if UNITY_2019_1_OR_NEWER
			SceneView.duringSceneGui += this.OnSceneGUI;
#else
			SceneView.onSceneGUIDelegate += this.OnSceneGUI;
#endif

			if (warningIcon == null)
			{
				warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
			}
//			if (trashIcon == null)
//			{
//				trashIcon = EditorGUIUtility.FindTexture("TreeEditor.Trash");
//			}
		}

		public void OnDisable()
		{
#if UNITY_2019_1_OR_NEWER
			SceneView.duringSceneGui -= this.OnSceneGUI;
#else
			EditorApplication.update -= this.OnUpdate;
#endif
//			ActiveEditorTracker.sharedTracker.isLocked = inspectorLocked;
		}

		void OnUpdate()
		{
			if (haveValidContext)
			{

				if (activeBoneIndex != editBoneIndex)
				{
					activeBoneIndex = BAD_INDEX;
					mirrorBoneIndex = BAD_INDEX;
					if (editBoneIndex != BAD_INDEX)
					{
						int boneHash = targetPose.poses[editBoneIndex].hash;
						context.activeTransform = context.activeUMA.skeleton.GetBoneTransform(boneHash);
						if (context.activeTransform != null)
						{
							activeBoneIndex = editBoneIndex;
						}

						if (context.mirrorTransform != null)
						{
							int mirrorHash = UMASkeleton.StringToHash(context.mirrorTransform.name);
							for (int i = 0; i < targetPose.poses.Length; i++)
							{
								if (targetPose.poses[i].hash == mirrorHash)
								{
									mirrorBoneIndex = i;
									break;
								}
							}
						}
					}
					else
					{
						context.activeTransform = null;
					}
				}
				if (!dynamicDNAConverterMode)
				{
					context.activeUMA.skeleton.ResetAll();
					if (context.startingPose != null)
					{
						context.startingPose.ApplyPose(context.activeUMA.skeleton, context.startingPoseWeight);
					}

					foreach (IDNAConverter id in context.activeUMA.umaRecipe.raceData.dnaConverterList)
					{
						if (id is DynamicDNAConverterController)
						{
							DynamicDNAConverterController Dcc = id as DynamicDNAConverterController;
							List<DynamicDNAPlugin> LBpp = Dcc.GetPlugins(typeof(BonePoseDNAConverterPlugin));
							foreach(DynamicDNAPlugin ddp in LBpp)
							{
								BonePoseDNAConverterPlugin bc = ddp as BonePoseDNAConverterPlugin;
								foreach(BonePoseDNAConverterPlugin.BonePoseDNAConverter converter in bc.poseDNAConverters)
								{
									converter.poseToApply.ApplyPose(context.activeUMA.skeleton, converter.startingPoseWeight);
								}
							}
							Dcc.overallModifiers.UpdateCharacter(context.activeUMA, context.activeUMA.skeleton, false);
						}
					}

					if (haveEditTarget)
					{
						targetPose.ApplyPose(context.activeUMA.skeleton, 1f);
					}
					else
					{
						targetPose.ApplyPose(context.activeUMA.skeleton, previewWeight);
					}
				}
				else
				{
					//TODO
					//how do we deal with poses that are not applied? The user will see the character in its current pose and bone positions for that
					//which makes no sense
					//also because this will be hooked up to dna, the dna itself might be causing other changes to happen ('overallScale' for example)
					//So I think the editor for bonePoseConverters, needs to jump in here and ask the user if they want to apply the dna that makes the pose active?
					//OR
					//maybe we create a skeleton how it would be IF the pose was applied to it and the user edits those transforms?
					//If the pose is applied they will see their character change, if its not it might be clearer that is the case
				}
			}
			if (!Application.isPlaying)
				_livePopupEditor = null;
		}

		private void DrawSkeletonBones()
		{
			if (context == null || context.activeUMA == null)
				return;
			var prevHandlesColor = Handles.color;
			if (context.activeUMA.umaRoot != null)
			{
				var Global = context.activeUMA.umaRoot.transform.Find("Global");
				if (Global != null)
				{
					var Position = Global.Find("Position");
					if (Position != null)
					{
						var Hips = Position.Find("Hips");
						if (Hips != null)
						{
							DrawSkeletonBonesRecursive(Hips,Color.white);
						}
					}
				}
			}
			Handles.color = prevHandlesColor;
		}

		private void DrawSkeletonBonesRecursive(Transform parentBone, Color col)
		{
			float leaflen = 0.01f;
			
			for (int i = 0; i < parentBone.childCount; i++)
			{
				Transform child = parentBone.GetChild(i);
				Color NextColor = child == context.activeTransform ? Color.green : (parentBone.GetChild(i) == context.mirrorTransform ? new Color(0, 0.5f, 1) : Color.white);

				Handles.color = col;
				Handles.DrawLine(parentBone.position, parentBone.GetChild(i).position);
				if (parentBone.GetChild(i).childCount > 0)
				{
					DrawSkeletonBonesRecursive(parentBone.GetChild(i),NextColor);
				}
				else
                {
					if (!child.gameObject.name.Contains("Adjust"))
					{
						Vector3 leafpos = child.rotation * (Vector3.one * leaflen);
						Vector3 ends = child.position + leafpos;
						// draw terminator bone
						//Handles.DrawLine(child.position, ends); ?? Not working
					}
				}
			}
		}

		void OnSceneGUI(SceneView scene)
		{
			if (haveValidContext && haveEditTarget)
			{
				serializedObject.Update();
				SerializedProperty poses = serializedObject.FindProperty("poses");
				SerializedProperty activePose = null;
				SerializedProperty mirrorPose = null;

				if (activeBoneIndex != BAD_INDEX) activePose = poses.GetArrayElementAtIndex(activeBoneIndex);
				if (mirrorBoneIndex != BAD_INDEX) mirrorPose = poses.GetArrayElementAtIndex(mirrorBoneIndex);

//				EditorGUI.BeginChangeCheck( );
				Transform activeTrans = context.activeTransform;
				Transform mirrorTrans = context.mirrorTransform;
				if (!mirrorActive || (mirrorBoneIndex == BAD_INDEX))
				{
					mirrorTrans = null;
				}

				if (activeTrans != null)
				{
					if (context.activeTransChanged)
					{
						scene.pivot = activeTrans.position;
						context.activeTransChanged = false;
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Position)
					{
						Vector3 newPos = Handles.PositionHandle(activeTrans.position, activeTrans.rotation);
						if (newPos != activeTrans.position)
						{
							Vector3 newLocalPos = activeTrans.parent.InverseTransformPoint(newPos);
							Vector3 deltaPos = newLocalPos - activeTrans.localPosition;
	//						Debug.Log("Moved active bone by: " + localDelta);
							activeTrans.localPosition += deltaPos;
							if (activePose != null)
							{
								SerializedProperty position = activePose.FindPropertyRelative("position");
								position.vector3Value = position.vector3Value + deltaPos;
							}

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaPos.x = -deltaPos.x;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaPos.y = -deltaPos.y;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaPos.z = -deltaPos.z;
										break;
								}

								mirrorTrans.localPosition += deltaPos;
								if (mirrorPose != null)
								{
									SerializedProperty position = mirrorPose.FindPropertyRelative("position");
									position.vector3Value = position.vector3Value + deltaPos;
								}
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Rotation)
					{
						Quaternion newRotation = Handles.RotationHandle(activeTrans.rotation, activeTrans.position);
						if (newRotation != activeTrans.rotation)
						{
							Quaternion deltaRot = Quaternion.Inverse(activeTrans.rotation) * newRotation;
							activeTrans.localRotation *= deltaRot;
							if (activePose != null)
							{
								SerializedProperty rotation = activePose.FindPropertyRelative("rotation");
								rotation.quaternionValue = rotation.quaternionValue * deltaRot;
							}

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaRot.y = -deltaRot.y;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaRot.x = -deltaRot.x;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaRot.x = -deltaRot.x;
										deltaRot.y = -deltaRot.y;
										break;
								}

								mirrorTrans.localRotation *= deltaRot;
								if (mirrorPose != null)
								{
									SerializedProperty rotation = mirrorPose.FindPropertyRelative("rotation");
									rotation.quaternionValue = rotation.quaternionValue * deltaRot;
								}
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Scale)
					{
						Vector3 newScale = Handles.ScaleHandle(activeTrans.localScale, activeTrans.position, activeTrans.rotation, HandleUtility.GetHandleSize(activeTrans.position));
						if (newScale != activeTrans.localScale)
						{
							activeTrans.localScale = newScale;
							if (activePose != null)
							{
								SerializedProperty scale = activePose.FindPropertyRelative("scale");
								scale.vector3Value = newScale;
							}

							if (mirrorTrans != null)
							{
								mirrorTrans.localScale = activeTrans.localScale;
								if (mirrorPose != null)
								{
									SerializedProperty scale = mirrorPose.FindPropertyRelative("scale");
									scale.vector3Value = newScale;
								}
							}
						}
					}
				}
					
				serializedObject.ApplyModifiedProperties();
			}
			DrawSkeletonBones();
		}

		private void AddABone(SerializedProperty poses, string boneName)
		{
			int addedIndex = poses.arraySize;
			poses.InsertArrayElementAtIndex(addedIndex);
			var pose = poses.GetArrayElementAtIndex(addedIndex);
			SerializedProperty bone = pose.FindPropertyRelative("bone");
			bone.stringValue = boneName;
			SerializedProperty hash = pose.FindPropertyRelative("hash");
			hash.intValue = UMASkeleton.StringToHash(boneName);
			SerializedProperty position = pose.FindPropertyRelative("position");
			position.vector3Value = Vector3.zero;
			SerializedProperty rotation = pose.FindPropertyRelative("rotation");
			rotation.quaternionValue = Quaternion.identity;
			SerializedProperty scale = pose.FindPropertyRelative("scale");
			scale.vector3Value = Vector3.one;
		}

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
			SerializedProperty poses = serializedObject.FindProperty("poses");

			if (doBoneAdd)
			{
				if (addBoneNames!= null && addBoneNames.Count > 0)
				{
					foreach(string s in addBoneNames)
					{
						AddABone(poses, s);
					}
				}
				else if (!string.IsNullOrEmpty(addBoneName))
				{
					AddABone(poses,addBoneName);
				}

				activeBoneIndex = BAD_INDEX;
				editBoneIndex = BAD_INDEX;
				mirrorBoneIndex = BAD_INDEX;
				addBoneIndex = 0;
				addBoneName = "";
				addBoneNames.Clear();
				doBoneAdd = false;
			}
			if (doBoneRemove)
			{
				poses.DeleteArrayElementAtIndex(removeBoneIndex - 1);

				activeBoneIndex = BAD_INDEX;
				editBoneIndex = BAD_INDEX;
				mirrorBoneIndex = BAD_INDEX;
				removeBoneIndex = 0;
				doBoneRemove = false;
			}

			// HACK
			if (!dynamicDNAConverterMode)
			{
				EditorGUILayout.HelpBox("Select a built UMA (DynamicCharacterAvatar, DynamicAvatar, UMAData) to enable editing and addition of new bones.", MessageType.Info);
				sourceUMA = EditorGUILayout.ObjectField("Source UMA", sourceUMA, typeof(UMAData), true) as UMAData;
				saveUMAData = sourceUMA;
			}
			else
			{
				if(sourceUMA != null)
				{
					EditorGUILayout.HelpBox("Switch to 'Scene View' and you will see gizmos to help you edit the positions of the pose bones below that you choose to 'Edit'", MessageType.Info);
				}
			}
			if (sourceUMA != null)
			{
				if (context == null)
				{
					context = new UMABonePoseEditorContext();
				}
				if (context.activeUMA != sourceUMA)
				{
					context.activeUMA = sourceUMA;

					ReloadFullTree();
				}
			}
			

			// Weight of pose on preview model
			if (haveValidContext && !dynamicDNAConverterMode)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(addRemovePadding);
				EditorGUI.BeginDisabledGroup(haveEditTarget);
				previewWeight = EditorGUILayout.Slider(previewGUIContent, previewWeight, 0f, 1f);
				EditorGUI.EndDisabledGroup();
				GUILayout.Space(addRemovePadding);
				EditorGUILayout.EndHorizontal();
			}

			GUILayout.Space(EditorGUIUtility.singleLineHeight / 2f);

			// Toolbar
			GUIHelper.BeginVerticalPadded();
			MirrorAxis = EditorGUILayout.Popup("Mirror Axis",MirrorAxis, MirrorAxises);
			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Find UMA in scene"))
            {
				UMAData data = GameObject.FindObjectOfType<UMAData>();
				if (data != null)
                {
					sourceUMA = data;
					saveUMAData = data;

					var active = Selection.activeObject;

					Selection.activeGameObject = data.gameObject;
					SceneView.FrameLastActiveSceneView();

					Selection.activeObject = active;
				}
            }

			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Convert all Left/Right"))
            {
				for (int i = 0; i < poses.arraySize; i++)
                {
                    FlipBone(poses, i);
                }
            }
			if (GUILayout.Button("Mirror to opposite"))
            {
				// find the opposite bone. 
				// Copy the parms
				// flip it with FlipBone
            }
			GUILayout.EndHorizontal();
			highlight = EditorGUILayout.TextField("Highlight bones containing: ", highlight);

			GUIHelper.EndVerticalPadded();

//			string controlName = GUI.GetNameOfFocusedControl();
//			if ((controlName != null) && (controlName.Length > 0))
//				Debug.Log(controlName);

			// These can get corrupted by undo, so just rebuild them
			string[] removeBoneOptions = new string[targetPose.poses.Length + 1];
			removeBoneOptions[0] = " ";
			for (int i = 0; i < targetPose.poses.Length; i++)
			{
				removeBoneOptions[i + 1] = targetPose.poses[i].bone;
			}
			string[] addBoneOptions = new string[1];
			if (haveValidContext)
			{
				List<string> addList = new List<string>(context.boneList);
				addList.Insert(0, " ");
				for (int i = 0; i < targetPose.poses.Length; i++)
				{
					addList.Remove(targetPose.poses[i].bone);
				}

				addBoneOptions = addList.ToArray();
			}

			if (editBoneIndex != BAD_INDEX)
			{
				SerializedProperty editBone = poses.GetArrayElementAtIndex(editBoneIndex);
				SerializedProperty bone = editBone.FindPropertyRelative("bone");
				string boneName = bone.stringValue;
				string mirrorBoneName = "";
				if (boneName.StartsWith("Left"))
                {
					mirrorBoneName = boneName.Replace("Left", "Right");
					//mirrorBoneIndex = FindMirrorBone(mirrorName);
                }
				if (boneName.StartsWith("Right"))
                {
					mirrorBoneName = boneName.Replace("Right", "Left");
					//mirrorBoneIndex = FindMirrorBone(mirrorName);
				}
			}

			// List of existing bones
			poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Pose Bones ("+poses.arraySize+")");
			if (poses.isExpanded)
			{
				for (int i = 0; i < poses.arraySize; i++)
				{
					SerializedProperty pose = poses.GetArrayElementAtIndex(i);
					drawBoneIndex = i;
					PoseBoneDrawer(pose);
				}
			}

			GUILayout.Space(EditorGUIUtility.singleLineHeight);

			// Controls for adding a new bone
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(addRemovePadding);
			if (haveValidContext)
			{
				EditorGUI.BeginDisabledGroup(addBoneIndex < 1);
				if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
				{
					addBoneName = addBoneOptions[addBoneIndex];
					doBoneAdd = true;
				}
				EditorGUI.EndDisabledGroup();

				EditorGUILayout.BeginVertical();
				GUILayout.Space(buttonVerticalOffset);
				addBoneIndex = EditorGUILayout.Popup(addBoneIndex, addBoneOptions);
				EditorGUILayout.EndVertical();
			}
			else
			{
				EditorGUI.BeginDisabledGroup(addBoneName.Length < minBoneNameLength);
				if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
				{
					doBoneAdd = true;
				}
				EditorGUI.EndDisabledGroup();

				EditorGUILayout.BeginVertical();
				GUILayout.Space(buttonVerticalOffset);
				addBoneName = EditorGUILayout.TextField(addBoneName);
				EditorGUILayout.EndVertical();
			}
			GUILayout.Space(addRemovePadding);
			EditorGUILayout.EndHorizontal();

			// Controls for removing existing bone
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(addRemovePadding);
			EditorGUI.BeginDisabledGroup(removeBoneIndex < 1);
			if (GUILayout.Button(removeBoneGUIContent, GUILayout.Width(90f)))
			{
				doBoneRemove = true;
			}
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.BeginVertical();
			GUILayout.Space(buttonVerticalOffset);
			removeBoneIndex = EditorGUILayout.Popup(removeBoneIndex, removeBoneOptions);
			EditorGUILayout.EndVertical();
			GUILayout.Space(addRemovePadding);
			EditorGUILayout.EndHorizontal();



			if (boneTreeView.RootNode != null)
			{
				EditorGUILayout.BeginHorizontal();

				if (GUILayout.Button("Expand All"))
				{
					boneTreeView.ExpandAll();
				}
				if (GUILayout.Button("Collapse All"))
				{
					boneTreeView.CollapseAll();
				}
				if (GUILayout.Button("Select None"))
				{
					List<int> noselection = new List<int>();
					boneTreeView.SetSelection(noselection);
				}
				EditorGUI.BeginDisabledGroup(!boneTreeView.HasSelection());
				if (GUILayout.Button("Add Selected"))
				{
					addBoneNames = boneTreeView.GetSelectedBones();
					doBoneAdd = true;
				}
				EditorGUI.EndDisabledGroup();
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();

				filter = GUILayout.TextField(filter);
				if (GUILayout.Button("Filter",GUILayout.Width(80)))
				{
					ReloadFilteredTree();
				}
				if (GUILayout.Button("Clear", GUILayout.Width(80)))
				{
					filter = "";
					ReloadFullTree();
				}

				EditorGUILayout.EndHorizontal();

				GUILayout.Space(10);
				string filterstate = "Bone List (No filter)";
				if (filtered)
				{
					filterstate = "Bone List (filter=\"" + lastFilter + "\")";
				}
				EditorGUILayout.LabelField(filterstate, EditorStyles.toolbarButton);

				Rect r = GUILayoutUtility.GetLastRect();
				scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
				r.yMin = 0;//r.yMax + 60;
				r.height = boneTreeView.totalHeight;

				GUILayout.Space(boneTreeView.totalHeight);

				boneTreeView.OnGUI(r);
				GUILayout.EndScrollView();
			}
            serializedObject.ApplyModifiedProperties();
        }

        private static void FlipBone(SerializedProperty poses, int i)
        {
            SerializedProperty pose = poses.GetArrayElementAtIndex(i);
            SerializedProperty bone = pose.FindPropertyRelative("bone");
            SerializedProperty position = pose.FindPropertyRelative("position");
            SerializedProperty rotation = pose.FindPropertyRelative("rotation");
            SerializedProperty scale = pose.FindPropertyRelative("scale");
            SerializedProperty hash = pose.FindPropertyRelative("hash");
            if (bone.stringValue.Contains("Left"))
            {
                bone.stringValue = bone.stringValue.Replace("Left", "Right");
                hash.intValue = UMASkeleton.StringToHash(bone.stringValue);
                FlipSingleBone(position, rotation);
            }
            else if (bone.stringValue.Contains("Right"))
            {
                bone.stringValue = bone.stringValue.Replace("Right", "Left");
                hash.intValue = UMASkeleton.StringToHash(bone.stringValue);
				FlipSingleBone(position, rotation);
            }
        }

        private static void FlipSingleBone(SerializedProperty position, SerializedProperty rotation)
        {
            Quaternion localRot = rotation.quaternionValue;
			Vector3 localPos = position.vector3Value;

			switch (MirrorAxis)
            {
				case 0: // X (all others)
					localRot.x *= -1;
					localRot.w *= -1;
					localPos.x *= -1;
					break;
				case 1: // Y (UMA/Blender models)
					localRot.y *= -1;
					localRot.w *= -1;
					localPos.y *= -1;
					break;
				case 2: // Z - Who knows
					localRot.z *= -1;
					localRot.w *= -1;
					localPos.z *= -1;
					break;
            }

			rotation.quaternionValue = localRot;
			position.vector3Value = localPos;
        }

        private void ReloadFilteredTree()
		{
			filtered = true;
			lastFilter = filter;
			boneTreeView.Initialize("Root");

			var Global = context.activeUMA.umaRoot.transform.Find("Global");
			if (Global != null)
			{
				AddFilteredNodesRecursive(boneTreeView.RootNode, Global, 0, filter);
			}
			boneTreeView.Reload();
			boneTreeView.ExpandAll();
		}



		private void ReloadFullTree()
		{
			filtered = false;
			boneTreeView.Initialize("Root");

			var Global = context.activeUMA.umaRoot.transform.Find("Global");
			if (Global != null)
			{
				AddNodeRecursive(boneTreeView.RootNode, Global);
			}
			boneTreeView.Reload();
			ExpandDepthRecursive(boneTreeView.RootNode, 5);
		}

		private void ExpandDepthRecursive(TreeViewItem theNode, int depth)
		{
			if (theNode.depth <= depth)
			{
				boneTreeView.SetExpanded(theNode.id, true);
				if (theNode.children != null)
				{
					foreach (TreeViewItem ti in theNode.children)
					{
						ExpandDepthRecursive(ti, depth);
					}
				}
			}
		}

		private void AddNodeRecursive(TreeViewItem rootNode, Transform theTransform,int depth=0)
		{
			boneTreeView.NodeCount++;
			TreeViewItem Node = new TreeViewItem(boneTreeView.NodeCount, depth, theTransform.name);
			rootNode.AddChild(Node);
			foreach(Transform t in theTransform)
			{
				AddNodeRecursive(Node, t, depth++); 
			}
		}

		private void AddFilteredNodesRecursive(TreeViewItem rootNode, Transform theTransform, int depth=0, string Filter="")
		{
			boneTreeView.NodeCount++;
			if (theTransform.name.ToLower().Contains(Filter))
			{
				TreeViewItem Node = new TreeViewItem(boneTreeView.NodeCount, depth, theTransform.name);
				rootNode.AddChild(Node);
			}
			foreach (Transform t in theTransform)
			{
				AddFilteredNodesRecursive(rootNode, t, depth++, Filter);
			}
		}

		private void PoseBoneDrawer(SerializedProperty property)
        {
			EditorGUI.indentLevel++;

			SerializedProperty bone = property.FindPropertyRelative("bone");
			GUIContent boneGUIContent = new GUIContent(
				bone.stringValue,
				"The name of the bone being modified by pose.");
			EditorGUILayout.BeginHorizontal();
			bone.isExpanded = EditorGUILayout.Foldout(bone.isExpanded, boneGUIContent);
			Color currentColor = GUI.color;
			if (!string.IsNullOrWhiteSpace(highlight))
            {
				if (bone.stringValue.ToLower().Contains(highlight.ToLower()))
                {
					GUI.color = Color.yellow * 0.7f ;
                }
            }
			if (drawBoneIndex == editBoneIndex)
			{
				GUI.color = Color.green;
				if (GUILayout.Button("Editing", EditorStyles.miniButton, GUILayout.Width(60f)))
				{
					editBoneIndex = BAD_INDEX;
					mirrorBoneIndex = BAD_INDEX;
				}
			}
			else if (drawBoneIndex == mirrorBoneIndex)
			{
				Color lightBlue = Color.Lerp(Color.blue, Color.cyan, 0.66f);
				if (mirrorActive)
				{
					GUI.color = lightBlue;
					if (GUILayout.Button("Mirroring", EditorStyles.miniButton, GUILayout.Width(60f)))
					{
						mirrorActive = false;
					}
				}
				else
				{
					GUI.color = Color.Lerp(lightBlue, Color.white, 0.66f);
					if (GUILayout.Button("Mirror", EditorStyles.miniButton, GUILayout.Width(60f)))
					{
						mirrorActive = true;
					}
				}
			}
			else
			{
				if (GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(60f)))
				{
					editBoneIndex = drawBoneIndex;
				}
				if (GUILayout.Button("x",EditorStyles.miniButton,GUILayout.Width(32)))
				{
					removeBoneIndex = drawBoneIndex+1;
					doBoneRemove = true;
				}
			}
			GUI.color = currentColor;
			EditorGUILayout.EndHorizontal();

			if (bone.isExpanded)
			{
				EditorGUI.BeginDisabledGroup(drawBoneIndex != editBoneIndex);
				EditorGUI.indentLevel++;
				int controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
//				GUI.SetNextControlName("position_" + drawBoneIndex);
				EditorGUILayout.PropertyField(property.FindPropertyRelative("position"), positionGUIContent);
				int controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Position;
				}

				// Show Euler angles for rotation
				SerializedProperty rotation = property.FindPropertyRelative("rotation");
				// Use BeginProperty() with fake rect to enable Undo but keep layout correct
				Rect rotationRect = new Rect(0, 0, 0, 0);
				EditorGUI.BeginProperty(rotationRect, GUIContent.none, rotation);

				Vector3 currentRotationEuler = ((Quaternion)rotation.quaternionValue).eulerAngles;
				Vector3 newRotationEuler = currentRotationEuler;
				EditorGUI.BeginChangeCheck();
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
//				GUI.SetNextControlName("rotation_" + drawBoneIndex);
				newRotationEuler = EditorGUILayout.Vector3Field(rotationGUIContent, newRotationEuler);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Rotation;
				}
				if (EditorGUI.EndChangeCheck())
				{
					if(newRotationEuler != currentRotationEuler)
					{
						rotation.quaternionValue = Quaternion.Euler(newRotationEuler);
					}

				}
				EditorGUI.EndProperty();

				SerializedProperty scaleProperty = property.FindPropertyRelative("scale");
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
//				GUI.SetNextControlName("scale_" + drawBoneIndex);
				EditorGUILayout.PropertyField(scaleProperty, scaleGUIContent);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Scale;
				}

				// Warn if there's a non-uniform scale
				Vector3 scaleValue = scaleProperty.vector3Value;
				if (!Mathf.Approximately(scaleValue.x, scaleValue.y) || !Mathf.Approximately(scaleValue.y, scaleValue.z))
				{
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUIUtility.labelWidth / 2f);
					if (warningIcon != null)
					{
						scaleWarningGUIContent.image = warningIcon;
						EditorGUILayout.LabelField(scaleWarningGUIContent, GUILayout.MinHeight(warningIcon.height + 4f));
					}
					else
					{
						EditorGUILayout.LabelField(scaleWarningGUIContent);
					}
					EditorGUILayout.EndHorizontal();
				}
					
				EditorGUI.indentLevel--;
				EditorGUI.EndDisabledGroup();

			}

			EditorGUI.indentLevel--;
        }
    }
}
