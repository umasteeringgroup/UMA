using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Utility to rename animation targets
	/// From: https://answers.unity.com/questions/38941/how-to-rebind-animated-variable.html
	/// renamed so as to avoid possible conflicts
	/// </summary>
	public class UMAAnimationPathRenamer : EditorWindow
	{
		private string prependPath;
		private string from, to;
		private int removeLeft = 0;
		private Vector2 scrollPosition = new Vector2();
		public List<AnimationClip> selectedClips = new List<AnimationClip>();
		public List<List<RemapperCurveData>> CurveDatasList = new List<List<RemapperCurveData>>();

		public AnimationClip selectedClip;
		public List<RemapperCurveData> CurveDatas;
		private bool initialized;

		[MenuItem("UMA/Animation Clip Target Renamer")]
		public static void OpenWindow()
		{
			UMAAnimationPathRenamer renamer = GetWindow<UMAAnimationPathRenamer>("Animation Clip Target Renamer");
			renamer.Clear();
		}

		private void Initialize()
		{
			foreach (AnimationClip clip in selectedClips)
			{
				CurveDatas = new List<RemapperCurveData>();
				var curveBindings = AnimationUtility.GetCurveBindings(clip);

				foreach (EditorCurveBinding curveBinding in curveBindings)
				{
					RemapperCurveData cd = new RemapperCurveData();
					cd.theClip = clip;
					cd.Binding = curveBinding;
					cd.OldPath = curveBinding.path + "";
					cd.NewPath = curveBinding.path + "";
					cd.Curve = new AnimationCurve(AnimationUtility.GetEditorCurve(clip, curveBinding).keys);
					CurveDatas.Add(cd);
				}

				CurveDatasList.Add(CurveDatas);
			}


			initialized = true;
		}

		private void Clear()
		{
			foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
			{
				oneCurveData.Clear();
			}
			CurveDatasList.Clear();

			CurveDatas = null;
			initialized = false;
		}

		void OnGUIShowTargetsList()
		{
			float labelWidth = EditorGUIUtility.labelWidth;

			if (CurveDatasList.Count == 0)
			{
				Initialize();
			}

			foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
			{
				HashSet<string> unique = new HashSet<string>();
				List<RemapperCurveData> uniqueCurveDatas = new List<RemapperCurveData>();
				foreach (RemapperCurveData remap in oneCurveData)
				{
					if (!unique.Contains(remap.Binding.path))
					{
						unique.Add(remap.Binding.path);
						uniqueCurveDatas.Add(remap);
					}
				}

				if (uniqueCurveDatas != null && uniqueCurveDatas.Count > 0)
				{
					EditorGUILayout.Space();
					EditorGUIUtility.labelWidth = 250;
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Clip: " + uniqueCurveDatas[0].theClip.name + " Unique Paths");
					EditorGUILayout.EndHorizontal();
					for (int i = 0; i < uniqueCurveDatas.Count; i++)
					{
						string newName = EditorGUILayout.TextField(uniqueCurveDatas[i].OldPath, uniqueCurveDatas[i].NewPath);

						if (uniqueCurveDatas[i].OldPath != newName)
						{
							var j = i;
							oneCurveData.ForEach(x =>
							{
								if (x.OldPath == uniqueCurveDatas[j].OldPath)
								{
									x.NewPath = newName;
								}
							});
						}
					}
				}
			}
			EditorGUIUtility.labelWidth = labelWidth;
		}

		private void RenameTargets()
		{
			for (int i = 0; i < selectedClips.Count; i++)
			{
				CurveDatasList[i].ForEach(x =>
				{
					if (x.Binding.path != "" && x.OldPath != x.NewPath)
					{
						x.Binding.path = x.NewPath;
						x.OldPath = x.NewPath;
					}
				});

				selectedClips[i].ClearCurves();

				foreach (var curveData in CurveDatasList[i])
				{
					selectedClips[i].SetCurve(curveData.Binding.path, curveData.Binding.type, curveData.Binding.propertyName, curveData.Curve);
					EditorUtility.SetDirty(selectedClips[i]);
				}
			}

			AssetDatabase.SaveAssets();

			Clear();
			Initialize();
		}

		public class RemapperCurveData
		{
			public AnimationClip theClip;
			public EditorCurveBinding Binding;
			public AnimationCurve Curve;
			public string OldPath;
			public string NewPath;
		}



		void Add()
		{
			foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
			{
				foreach (RemapperCurveData remCurveData in oneCurveData)
				{
					string newName = EditorGUILayout.TextField(remCurveData.OldPath, remCurveData.NewPath);
					remCurveData.NewPath = prependPath + newName;
				}
			}
		}


		void Remove()
		{
			foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
			{
				foreach (RemapperCurveData remCurveData in oneCurveData)
				{
					string newName = "";

					if (remCurveData.NewPath.Length < removeLeft)
					{
						newName = remCurveData.NewPath.Substring(removeLeft);
					}
					remCurveData.NewPath = newName;
				}
			}
		}

		void Replace(string oldstr, string newstr)
		{
			foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
			{
				foreach (RemapperCurveData remCurveData in oneCurveData)
				{
					string newName = remCurveData.NewPath.Replace(oldstr, newstr);
					remCurveData.NewPath = newName;
				}
			}
		}


		void OnGUI()
		{
			float labelWidth = EditorGUIUtility.labelWidth;

			EditorGUIUtility.labelWidth = 100;

			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition,
				GUILayout.Width(position.width), GUILayout.Height(position.height)); // Scroll //

			int beforeCount = selectedClips.Count;
			DropGUIArea();
			int afterCount = selectedClips.Count;
			if (beforeCount != afterCount)
			{
				EditorGUILayout.EndScrollView();
				return;
			}
			for (int i = 0; i < selectedClips.Count; i++)
			{
				GUILayout.BeginHorizontal();

				AnimationClip previous = selectedClips[i];
				selectedClips[i] = EditorGUILayout.ObjectField("Animation Clip", selectedClips[i], typeof(AnimationClip), true) as AnimationClip;

				if (selectedClips[i] != previous)
				{
					Clear();
				}

				if (selectedClips[i] != null)
				{
					if (!initialized)
					{
						Initialize();
					}
				}
				else
				{
					return;
				}

				if (GUILayout.Button("Remove", GUILayout.ExpandWidth(false)))
				{
					selectedClips.RemoveAt(i);
					Clear();
					Initialize();
				}
				GUILayout.EndHorizontal();
			}

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			prependPath = EditorGUILayout.TextField("Prepend Path", prependPath);
			if (GUILayout.Button("Add", GUILayout.Width(80)))
			{
				Clear();
				Initialize();
				Add();
				prependPath = "";
			}
			EditorGUILayout.EndHorizontal();


			EditorGUILayout.BeginHorizontal();
			removeLeft = EditorGUILayout.IntField("Remove at Start", removeLeft);
			if (GUILayout.Button("Remove", GUILayout.Width(80)))
			{
				Clear();
				Initialize();
				Remove();
				removeLeft = 0;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("From", GUILayout.Width(100));
			from = EditorGUILayout.TextField(from);
			EditorGUILayout.LabelField("To ===>", GUILayout.Width(100));
			to = EditorGUILayout.TextField(to);
			if (GUILayout.Button("Replace", GUILayout.Width(80)))
			{
				Clear();
				Initialize();
				Replace(from, to);
				removeLeft = 0;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh"))
			{
				Clear();
				Initialize();
			}
			EditorGUILayout.EndHorizontal();

			OnGUIShowTargetsList();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Apply"))
			{
				RenameTargets();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndScrollView();

			EditorGUIUtility.labelWidth = labelWidth;
		}


		void DropGUIArea()
		{
			/// Drag Here //
			Event evt = Event.current;

			EditorGUILayout.Space();

			Rect drop_area = GUILayoutUtility.GetRect(0.0f, 40.0f, GUILayout.ExpandWidth(true));
			drop_area.x += 15;
			drop_area.width -= (15 + 18);
			GUIStyle estilo = new GUIStyle(GUI.skin.box);
			estilo.normal.textColor = Color.black;
			estilo.alignment = TextAnchor.MiddleCenter;
			GUI.Box(drop_area, "To start, drag broken animation clips here. Then choose the operations below to update the paths.", estilo);

			EditorGUILayout.Space();

			switch (evt.type)
			{
				case EventType.DragUpdated:
				case EventType.DragPerform:
					if (!drop_area.Contains(evt.mousePosition))
					{
						return;
					}

					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

					if (evt.type == EventType.DragPerform)
					{
						DragAndDrop.AcceptDrag();

						foreach (UnityEngine.Object dragged_object in DragAndDrop.objectReferences)
						{
							AnimationClip draggedAnimation = (AnimationClip)dragged_object;
							selectedClips.Add(draggedAnimation);
						}
						Clear();
						Initialize();
					}
					break;
			}
		}
	}
}