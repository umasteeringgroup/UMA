using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.AssetBundles
{
	[CustomEditor(typeof(AssetBundleManager),true)]
	public class AssetBundleManagerInspector : Editor
	{
		protected AssetBundleManager thisABM;
		protected AssetBundleManager.EditorHelper _editorhelper;
		public void OnEnable()
		{
			thisABM = target as AssetBundleManager;
		}

		public void Update()
		{
			if (_editorhelper != null)
			{
                _editorhelper.Update();
            }
		}
		public void OnDestroy()
		{
			EditorApplication.update -= Update;
		}

		public override void OnInspectorGUI()
		{
			EditorUtility.SetDirty(target);//this makes the editor helper update every frame
			DrawDefaultInspector();
			if (_editorhelper == null)
			{
				_editorhelper = new AssetBundleManager.EditorHelper();
				_editorhelper.Update();
				EditorApplication.update += Update;
			}
			DrawEditorHelper();
		}

		private void DrawEditorHelper()
		{
			EditorGUILayout.LabelField("In Progress Operations", EditorStyles.boldLabel);
			for (int i = 0; i < _editorhelper.inProgressOperations.Count; i++)
				EditorGUILayout.TextField("", _editorhelper.inProgressOperations[i]);
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Downloading Bundles", EditorStyles.boldLabel);
			for (int i = 0; i < _editorhelper.downloadingBundles.Count; i++)
				EditorGUILayout.TextField("", _editorhelper.downloadingBundles[i]);
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Loaded Bundles", EditorStyles.boldLabel);
			for (int i = 0; i < _editorhelper.loadedBundles.Count; i++)
				EditorGUILayout.TextField("", _editorhelper.loadedBundles[i]);
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Failed Downloads", EditorStyles.boldLabel);
			for (int i = 0; i < _editorhelper.failedDownloads.Count; i++)
				EditorGUILayout.TextField("", _editorhelper.failedDownloads[i]);
			EditorGUILayout.Space();
			EditorGUILayout.ToggleLeft("Is Using Cached Index", _editorhelper.isUsingCachedIndex);
			EditorGUILayout.HelpBox("Bundles Index Player version: " + _editorhelper.bundleIndexPlayerversion, MessageType.None);
		}
	}
}
