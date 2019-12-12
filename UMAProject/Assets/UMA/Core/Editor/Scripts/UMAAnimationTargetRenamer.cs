using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Utility to rename animation targets
/// From: https://answers.unity.com/questions/38941/how-to-rebind-animated-variable.html
/// renamed so as to avoid possible conflicts
/// </summary>
public class UMAAnimationPathRenamer : EditorWindow
{
	private string prependPath;
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

		if (CurveDatasList.Count == 0)
			Initialize();

		foreach (List<RemapperCurveData> oneCurveData in CurveDatasList)
		{
			List<string> unique = new List<string>();
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
			}
		}




		Clear();
		Initialize();
	}

	public class RemapperCurveData
	{
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
				string newName = remCurveData.NewPath.Substring(removeLeft);
				remCurveData.NewPath = newName;
			}
		}
	}


	void OnGUI()
	{
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
				Initialize();
			}

			GUILayout.EndHorizontal();
		}

		EditorGUILayout.Space();

		prependPath = EditorGUILayout.TextField("Prepend Path", prependPath);
		if (GUILayout.Button("Add"))
		{
			Clear();
			Initialize();
			Add();
			prependPath = "";
		}

		removeLeft = EditorGUILayout.IntField("Remove at Start", removeLeft);
		if (GUILayout.Button("Remove"))
		{
			Clear();
			Initialize();
			Remove();
			removeLeft = 0;
		}


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



	}


	void DropGUIArea()
	{
		/// Drag Here //
		Event evt = Event.current;

		EditorGUILayout.Space();

		Rect drop_area = GUILayoutUtility.GetRect(0.0f, 20.0f, GUILayout.ExpandWidth(true));
		drop_area.x += 15;
		drop_area.width -= (15 + 18);
		GUIStyle estilo = new GUIStyle(GUI.skin.box);
		estilo.normal.textColor = Color.black;
		GUI.Box(drop_area, "Drag Here", estilo);

		EditorGUILayout.Space();

		switch (evt.type)
		{
			case EventType.DragUpdated:
			case EventType.DragPerform:
				if (!drop_area.Contains(evt.mousePosition))
					return;

				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

				if (evt.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();

					foreach (UnityEngine.Object dragged_object in DragAndDrop.objectReferences)
					{
						AnimationClip draggedAnimation = (AnimationClip)dragged_object;
						selectedClips.Add(draggedAnimation);
					}

					Initialize();
				}
				break;
		}
	}
}