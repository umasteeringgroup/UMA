using UnityEngine;
using UnityEditor;
using UMA.AssetBundles;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicAssetLoader),true)]
	public class DynamicAssetLoaderEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			//DrawDefaultInspector();
			//draw the script field disabled
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
			EditorGUI.EndDisabledGroup();
			//Draw everything as standard except the AssetBundleEncryption fields
			DrawPropertiesExcluding(serializedObject, new string[] { "m_Script","useEncryptedBundles", "bundleEncryptionPassword", "assetBundlesToPreLoad", "gameObjectsToActivate", "gameObjectsToActivateOnInit", "loadingMessageObject", "loadingMessageText", "loadingMessage", "percentDone", "assetBundlesDownloading", "canCheckDownloadingBundles", "isInitializing", "isInitialized", "placeholderRace", "placeholderWardrobeRecipe", "placeholderSlot", "placeholderOverlay", "downloadingAssets" });
			var useEncryptedBundles = serializedObject.FindProperty("useEncryptedBundles");
			var bundleEncryptionPassword = serializedObject.FindProperty("bundleEncryptionPassword");
			var remoteServerURL = serializedObject.FindProperty("remoteServerURL");
			var remoteServerIndexURL = serializedObject.FindProperty("remoteServerIndexURL");
            EditorGUILayout.PropertyField(useEncryptedBundles);
			if (useEncryptedBundles.boolValue)
			{
				EditorGUI.indentLevel++;
				//we need to notify the user if the password was updated
				var currentEncryptionPassword = UMAABMSettings.GetEncryptionPassword();
				if (currentEncryptionPassword != bundleEncryptionPassword.stringValue)
				{
					bundleEncryptionPassword.stringValue = currentEncryptionPassword;
					EditorGUILayout.HelpBox("Updated Password to match UMAAssetBundleManagerSettings", MessageType.Info);
				}
				//actually dont show this if the settings window is open and the server is started
				string helpMsg = "";
				MessageType helpMsgType = MessageType.Info;
				if (!SimpleWebServer.serverStarted)
					helpMsg = "To test your encrypted bundles you need to build the encrypted versions and start the AssetBundleTesting server in the UMAAssetBundleManager settings window, or upload them and set the 'RemoteServerURL field above.";
				if (currentEncryptionPassword == "" && UMAABMSettings.GetEncryptionEnabled() == false && (remoteServerURL.stringValue == "" && remoteServerIndexURL.stringValue == ""))
				{
					helpMsg = "You need to enable assetBundle encryption in the UMAAssetBundleManager settings window.";
					helpMsgType = MessageType.Warning;
				}
				if(helpMsg != "")
					EditorGUILayout.HelpBox(helpMsg, helpMsgType);
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.PropertyField(bundleEncryptionPassword);
				EditorGUI.EndDisabledGroup();
				GUILayout.BeginHorizontal();
				GUILayout.Space(15);
				if(GUILayout.Button("Open UMAAssetBundleManager Settings"))
				{
					EditorWindow.GetWindow<UMAAssetBundleManagerSettings>();
				}
				GUILayout.EndHorizontal();
				EditorGUI.indentLevel--;
			}

			DrawPropertiesExcluding(serializedObject, new string[] { "m_Script","makePersistent", "remoteServerURL", "useJsonIndex", "remoteServerIndexURL", "useEncryptedBundles", "bundleEncryptionPassword" });
			
			serializedObject.ApplyModifiedProperties();
		}

	}
}
