#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace UMA.AssetBundles
{

	public class UMAAssetBundleManagerSettings : EditorWindow
	{
		#region PUBLIC FIELDS

		public const string  DEFAULT_ENCRYPTION_SUFFIX = "encrypted";

		#endregion

		#region PRIVATE FIELDS

		//AssetBundle settings related
		string currentEncryptionPassword = "";
		string newEncryptionPassword = "";
		string currentEncryptionSuffix = "";
		string newEncryptionSuffix = "";
		bool currentEncodeNamesSetting = false;
#if ENABLE_IOS_APP_SLICING
		bool currentAppSlicingSetting = false;
#endif
		bool newEncodeNamesSetting = false;

		//DOS MODIFIED 14/11/2017 added a ability to set the new bundlesPlayerVersion value
		bool _enableBundleIndexVersioning;
		//either automatically use the Buildversion from player settings or specify a value here
		UMAABMSettingsStore.BundleIndexVersioningOpts _bundleIndexVersioningMethod = UMAABMSettingsStore.BundleIndexVersioningOpts.UseBuildVersion;
		//if useBundlesVersioning is set to Custom this value is used when the bundles are built
		string _bundleIndexCustomValue = "0.0";

		//server related
		bool _enableLocalAssetBundleServer;
		int _port;
		string _statusMessage;
		string[] _hosts;
		string _activeHost;
		bool portError = false;
		bool serverException = false;

		//Testing Build related
		bool developmentBuild = false;

		//GUI related
		Vector2 scrollPos;
		bool serverRequestLogOpen = true;
		bool manualEditEncryptionKey = false;
		bool encryptionSaveButEnabled = false;
		bool manualEditEncryptionSuffix = false;
		bool encryptionKeysEnabled = false;

		#endregion

		#region PUBLIC PROPERTIES

		#endregion

		#region PRIVATE PROPERTIES

		//server related
		bool EnableLocalAssetBundleServer
		{
			get { return _enableLocalAssetBundleServer; }
			set
			{
				if (_enableLocalAssetBundleServer == value) return;
				_enableLocalAssetBundleServer = value;
				EditorPrefs.SetBool(Application.dataPath+"LocalAssetBundleServerEnabled", value);
				UpdateServer();
			}
		}
		int Port
		{
			get { return _port; }
			set
			{
				if (_port == value) return;
				_port = value;
				EditorPrefs.SetInt(Application.dataPath+"LocalAssetBundleServerPort", _port);
				UpdateServer();
			}
		}
		string ActiveHost
		{
			get { return _activeHost; }
			set
			{
				if (_activeHost == value) return;
				_activeHost = value;
				EditorPrefs.SetString(Application.dataPath+"LocalAssetBundleServerURL", _activeHost);
			}
		}
		#endregion

		#region BASE METHODS

		[MenuItem("Assets/AssetBundles/UMA Asset Bundle Manager Settings")]
		[MenuItem("UMA/UMA Asset Bundle Manager Settings")]
		static void Init()
		{
			UMAAssetBundleManagerSettings window = (UMAAssetBundleManagerSettings)EditorWindow.GetWindow<UMAAssetBundleManagerSettings>("UMA AssetBundle Manager");
			window.Show();
		}

		void OnFocus()
		{
			encryptionKeysEnabled = UMAABMSettings.GetEncryptionEnabled();
			currentEncryptionPassword = newEncryptionPassword = UMAABMSettings.GetEncryptionPassword();
			currentEncryptionSuffix = newEncryptionSuffix = UMAABMSettings.GetEncryptionSuffix();
			if (currentEncryptionSuffix == "")
				currentEncryptionSuffix = newEncryptionSuffix = DEFAULT_ENCRYPTION_SUFFIX;
			currentEncodeNamesSetting = newEncodeNamesSetting = UMAABMSettings.GetEncodeNames();
#if ENABLE_IOS_APP_SLICING
			currentAppSlicingSetting = UMAABMSettings.GetBuildForSlicing();
#endif
			_enableBundleIndexVersioning = UMAABMSettings.EnableBundleIndexVersioning;
        }

		void OnEnable()
		{
			encryptionKeysEnabled = UMAABMSettings.GetEncryptionEnabled();
			currentEncryptionPassword = newEncryptionPassword = UMAABMSettings.GetEncryptionPassword();
			currentEncryptionSuffix = newEncryptionSuffix = UMAABMSettings.GetEncryptionSuffix();
			if (currentEncryptionSuffix == "")
				currentEncryptionSuffix = newEncryptionSuffix = DEFAULT_ENCRYPTION_SUFFIX;
			currentEncodeNamesSetting = newEncodeNamesSetting = UMAABMSettings.GetEncodeNames();
#if ENABLE_IOS_APP_SLICING
			currentAppSlicingSetting = UMAABMSettings.GetBuildForSlicing();
#endif
			_enableBundleIndexVersioning = UMAABMSettings.EnableBundleIndexVersioning;
			//localAssetBundleServer status
			_enableLocalAssetBundleServer = EditorPrefs.GetBool(Application.dataPath+"LocalAssetBundleServerEnabled");
			_port = EditorPrefs.GetInt(Application.dataPath + "LocalAssetBundleServerPort", 7888);
			//When the window is opened we still need to tell the user if the port is available so
			if (!_enableLocalAssetBundleServer)
			{
				UpdateServer(true);
				ServerStop();
				if (serverException)
					portError = true;
			}
			else
			{
				UpdateServer();
			}
		}

		void Start()
		{

			ServerStart();
		}

		void OnDisable()
		{
			//Makes the Local server stop when the window is closed 
			//also prevents the 'Listener already in use' error when the window is closed and opened
			ServerStop();
		}

		#endregion

		#region SERVER RELATED METHODS

		void ServerStart()
		{
			SimpleWebServer.Start(_port);
			_statusMessage = "Server Running";
			UpdateHosts();
			if (_activeHost == null)
			{
				ActiveHost = _hosts[0];
			}
			SimpleWebServer.ServerURL = ActiveHost;
		}

		void ServerStop()
		{
			if (SimpleWebServer.Instance != null)
			{
				SimpleWebServer.Instance.Stop();
				SimpleWebServer.ServerURL = "";
				_statusMessage = "Server Stopped";
				_hosts = null;
			}
		}

		void UpdateHosts()
		{
			var strHostName = System.Net.Dns.GetHostName();
			var list = new System.Collections.Generic.List<string>();
			try
			{
				var ipEntry = System.Net.Dns.GetHostEntry(strHostName);
						
				foreach (var addr in ipEntry.AddressList)
				{
					if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
					{
						list.Add(string.Format("http://{0}:{1}/", addr, Port));
					}
				}
			}
			catch(Exception ex)
			{
				if (Debug.isDebugBuild)
					Debug.Log(ex.Message);
				strHostName = "localhost";
			}
			if (list.Count == 0)
			{
				list.Add(string.Format("http://localhost:{0}/", Port));
			}
			portError = false;
			_hosts = list.ToArray();
		}

		private void UpdateServer(bool test = false)
		{
			serverException = false;
			try
			{
				if (SimpleWebServer.Instance != null)
				{
					if (!EnableLocalAssetBundleServer)
					{
						ServerStop();
						if (Debug.isDebugBuild)
							Debug.Log("Server Stopped");
					}
					else if (SimpleWebServer.Instance.Port != _port)
					{
						ServerStop();
						ServerStart();
						if (Debug.isDebugBuild)
							Debug.Log("Server Started");
					}
				}
				else if (EnableLocalAssetBundleServer || test)
				{
					ServerStart();
					if (!test)
					{
						if (Debug.isDebugBuild)
							Debug.Log("Server Started");
					}
				}
			}
			catch (Exception e)
			{
				_statusMessage = string.Format("Simple Webserver Exception: {0}\nStack Trace\n{1}", e.ToString(), e.StackTrace);
				if (Debug.isDebugBuild)
					Debug.LogException(e);
				EditorPrefs.SetBool(Application.dataPath + "LocalAssetBundleServerEnabled", false);
				EnableLocalAssetBundleServer = false;
				serverException = true;
				ServerStop();
			}
		}

		#endregion
		void OnGUI()
		{
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, true);
			EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth - 20f));
			EditorGUILayout.Space();
			GUILayout.Label("UMA AssetBundle Manager", EditorStyles.boldLabel);

			BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));

			GUILayout.Label("AssetBundle Options", EditorStyles.boldLabel);
			//AssetBundle Build versioning
			EditorGUI.BeginChangeCheck();
			_enableBundleIndexVersioning = EditorGUILayout.ToggleLeft("Enable AssetBundle Index Versioning", _enableBundleIndexVersioning);
			if (EditorGUI.EndChangeCheck())
			{
				UMAABMSettings.EnableBundleIndexVersioning = _enableBundleIndexVersioning;
            }
			if (_enableBundleIndexVersioning)
			{
				BeginVerticalIndented(10, new Color(0.75f, 0.875f, 1f));
				EditorGUILayout.HelpBox("Sets the 'bundlesPlayerVersion' value of the AssetBundleIndex when you build your bundles. You can use this to determine if your app needs to force the user to go online to update their bundles and/or application (its up to you how you do that though!). ", MessageType.Info);
				EditorGUI.BeginChangeCheck();
				_bundleIndexVersioningMethod = (UMAABMSettingsStore.BundleIndexVersioningOpts)EditorGUILayout.EnumPopup("Bundles Versioning Method", _bundleIndexVersioningMethod);
				if (EditorGUI.EndChangeCheck())
				{
					UMAABMSettings.BundleIndexVersioningMethod = _bundleIndexVersioningMethod;
				}
				if(_bundleIndexVersioningMethod == UMAABMSettingsStore.BundleIndexVersioningOpts.Custom)
				{
					EditorGUI.BeginChangeCheck();
					_bundleIndexCustomValue = EditorGUILayout.TextField("Bundles Index Player Version", _bundleIndexCustomValue);
					if (EditorGUI.EndChangeCheck())
					{
						UMAABMSettings.BundleIndexCustomValue = _bundleIndexCustomValue;
                    }
                }
				else
				{
					var currentBuildVersion = Application.version;
					if (string.IsNullOrEmpty(currentBuildVersion))
					{
						EditorGUILayout.HelpBox("Please be sure to set a 'Version' number (eg 1.0, 2.1.0) in Edit->ProjectSettings->Player->Version", MessageType.Warning);
					}
					else
					{
						EditorGUILayout.HelpBox("Current 'Version' number ("+currentBuildVersion+ ") will be used. You can change this in Edit->ProjectSettings->Player->Version.", MessageType.Info);
					}
				}
				EditorGUILayout.Space();
				EndVerticalIndented();
			}
			//Asset Bundle Encryption
			//defined here so we can modify the message if encryption settings change
			string buildBundlesMsg = "";
			MessageType buildBundlesMsgType = MessageType.Info;
			EditorGUI.BeginChangeCheck();
			encryptionKeysEnabled = EditorGUILayout.ToggleLeft("Enable AssetBundle Encryption", encryptionKeysEnabled);
			if (EditorGUI.EndChangeCheck())
			{
				//If encryption was turned ON generate the encryption password if necessary
				if (encryptionKeysEnabled)
				{
					if (currentEncryptionPassword == "")
					{
						if (UMAABMSettings.GetEncryptionPassword() != "")
							currentEncryptionPassword = UMAABMSettings.GetEncryptionPassword();
						else
							currentEncryptionPassword = EncryptionUtil.GenerateRandomPW();
					}
					UMAABMSettings.SetEncryptionPassword(currentEncryptionPassword);
					buildBundlesMsg = "You have turned on encryption and need to Rebuild your bundles to encrypt them.";
					buildBundlesMsgType = MessageType.Warning;
				}
				else
				{
					UMAABMSettings.DisableEncryption();
					currentEncryptionPassword = "";
				}
			}
			if (encryptionKeysEnabled)
			{
				BeginVerticalIndented(10, new Color(0.75f, 0.875f, 1f));
				//tip
				EditorGUILayout.HelpBox("Make sure you turn on 'Use Encrypted Bundles' in the 'DynamicAssetLoader' components in your scenes.", MessageType.Info);
				//Encryption key
				//If we can work out a way for people to download a key we can use this tip and the 'EncryptionKeyURL' field
				//string encryptionKeyToolTip = "This key is used to generate the required encryption keys used when encrypting your bundles. If you change this key you will need to rebuild your bundles otherwise they wont decrypt. If you use the 'Encryption Key URL' field below you MUST ensure this field is set to the same key the url will return.";
				string encryptionKeyToolTip = "This key is used to generate the required encryption keys used when encrypting your bundles. If you change this key you will need to rebuild your bundles otherwise they wont decrypt.";
				EditorGUILayout.LabelField(new GUIContent("Bundle Encryption Password", encryptionKeyToolTip));
				EditorGUILayout.BeginHorizontal();
				if (!manualEditEncryptionKey)
				{
					if(GUILayout.Button(new GUIContent("Edit", encryptionKeyToolTip)))
					{
						manualEditEncryptionKey = true;
                    }
					EditorGUI.BeginDisabledGroup(!manualEditEncryptionKey);
					EditorGUILayout.TextField("", UMAABMSettings.GetEncryptionPassword());//THis bloody field WILL NOT update when you click edit, then canel, the value stays
					EditorGUI.EndDisabledGroup();
				}
				else
				{
                    EditorGUI.BeginChangeCheck();
					newEncryptionPassword = EditorGUILayout.TextArea(newEncryptionPassword);
					if (EditorGUI.EndChangeCheck())
					{
						encryptionSaveButEnabled = EncryptionUtil.PasswordValid(newEncryptionPassword);
					}
					if (encryptionSaveButEnabled)
					{
						if (GUILayout.Button(new GUIContent("Save"), GUILayout.MaxWidth(60)))
						{
							currentEncryptionPassword = newEncryptionPassword;
							UMAABMSettings.SetEncryptionPassword(newEncryptionPassword);
							EditorGUIUtility.keyboardControl = 0;
							manualEditEncryptionKey = false;
						}
					}
					else
					{
						GUI.enabled = false;
						if (GUILayout.Button(new GUIContent("Save", "Your Encryptiom Password should be at least 16 characters long"), GUILayout.MaxWidth(60)))
						{
							//Do nothing
						}
						GUI.enabled = true;
					}
					if (GUILayout.Button(new GUIContent("Cancel", "Reset to previous value: "+ currentEncryptionPassword), GUILayout.MaxWidth(60)))
					{
						manualEditEncryptionKey = false;
						newEncryptionPassword = currentEncryptionPassword = UMAABMSettings.GetEncryptionPassword();
						encryptionSaveButEnabled = false;
						EditorGUIUtility.keyboardControl = 0;

					}
				}	
				EditorGUILayout.EndHorizontal();
				//EncryptionKey URL
				//not sure how this would work- the delivered key would itself need to be encrypted probably
				//Encrypted bundle suffix
				string encryptionSuffixToolTip = "This suffix is appled to the end of your encrypted bundle names when they are built. Must be lower case and alphaNumeric. Cannot be empty. Defaults to "+DEFAULT_ENCRYPTION_SUFFIX;
                EditorGUILayout.LabelField(new GUIContent("Encrypted Bundle Suffix", encryptionSuffixToolTip));
				EditorGUILayout.BeginHorizontal();
				if (!manualEditEncryptionSuffix)
				{
					if (GUILayout.Button(new GUIContent("Edit", encryptionSuffixToolTip)))
					{
						manualEditEncryptionSuffix = true;
					}
					EditorGUI.BeginDisabledGroup(!manualEditEncryptionSuffix);
					EditorGUILayout.TextField(new GUIContent("", encryptionSuffixToolTip), currentEncryptionSuffix);
					EditorGUI.EndDisabledGroup();
				}
				else
				{
					newEncryptionSuffix = EditorGUILayout.TextArea(newEncryptionSuffix);
					if (GUILayout.Button(new GUIContent("Save")))
					{
						if (newEncryptionSuffix != "")
						{
							Regex rgx = new Regex("[^a-zA-Z0-9 -]");
							var suffixToSend = rgx.Replace(newEncryptionSuffix, "");
							currentEncryptionSuffix = suffixToSend;
							UMAABMSettings.SetEncryptionSuffix(suffixToSend.ToLower());
							EditorGUIUtility.keyboardControl = 0;
							manualEditEncryptionSuffix = false;
						}
					}
				}
				EditorGUILayout.EndHorizontal();

				//Encode Bundle Names
				string encodeBundleNamesTooltip = "If true encrypted bundle names will be base64 encoded";
				EditorGUI.BeginChangeCheck();
				newEncodeNamesSetting = EditorGUILayout.ToggleLeft(new GUIContent("Encode Bundle Names", encodeBundleNamesTooltip), currentEncodeNamesSetting);
				if (EditorGUI.EndChangeCheck())
				{
					currentEncodeNamesSetting = newEncodeNamesSetting;
                    UMAABMSettings.SetEncodeNames(newEncodeNamesSetting);
				}

				EndVerticalIndented();
			}
#if ENABLE_IOS_APP_SLICING
			string AppSlicingTooltip = "If true will build bundles uncompressed for use with iOS Resources Catalogs";
			EditorGUI.BeginChangeCheck();
			bool newAppSlicingSetting = EditorGUILayout.ToggleLeft(new GUIContent("Build for iOS App Slicing", AppSlicingTooltip), currentAppSlicingSetting);
			if (EditorGUI.EndChangeCheck())
			{
				currentAppSlicingSetting = newAppSlicingSetting;
				UMAABMSettings.SetBuildForSlicing(newAppSlicingSetting);
			}

#endif

			//Asset Bundle Building
			EditorGUILayout.Space();
			string buttonBuildAssetBundlesText = "Build AssetBundles";
			//Now defined above the encryption
			//string buildBundlesText = "Click the button below to build your bundles if you have not done so already.";
			string fullPathToBundles = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Utility.AssetBundlesOutputPath);
			string fullPathToPlatformBundles = Path.Combine(fullPathToBundles, Utility.GetPlatformName());
			
			//if we have not built any asset bundles there wont be anything in the cache to clear
			bool showClearCache = false;

			if (Directory.Exists(fullPathToPlatformBundles))
			{
				buttonBuildAssetBundlesText = "Rebuild AssetBundles";
				buildBundlesMsg = buildBundlesMsg == "" ? "Rebuild your assetBundles to reflect your latest changes" : buildBundlesMsg;
				showClearCache = true;
			}
			else
			{
				buildBundlesMsg = "You have not built your asset bundles for " + EditorUserBuildSettings.activeBuildTarget.ToString() + " yet. Click this button to build them.";
				buildBundlesMsgType = MessageType.Warning;
				showClearCache = false;
			}
			EditorGUILayout.HelpBox(buildBundlesMsg, buildBundlesMsgType);
			if (GUILayout.Button(buttonBuildAssetBundlesText))
			{
				BuildScript.BuildAssetBundles();

#if UNITY_2017_1_OR_NEWER
				Caching.ClearCache ();
#else
				Caching.CleanCache();          
#endif
				GUIUtility.ExitGUI();
			}
			EndVerticalPadded(5);
			EditorGUILayout.Space();

			//Local AssetBundleServer
			BeginVerticalPadded(5f, new Color(0.75f, 0.875f, 1f));
			GUILayout.Label("AssetBundle Testing Server", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Once you have built your bundles this local Testing Server can be enabled and it will load those AssetBundles rather than the files inside the project.", MessageType.Info);

			if (!BuildScript.CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
			{
				EditorGUILayout.HelpBox("Builds for " + EditorUserBuildSettings.activeBuildTarget.ToString() + " cannot access this local server, but you can still use it in the editor.", MessageType.Warning);
			}

			bool updateURL = false;
			EnableLocalAssetBundleServer = EditorGUILayout.Toggle("Start Server", EnableLocalAssetBundleServer);

			//If the server is off we need to show the user a message telling them that they will have to have uploaded their bundles to an external server
			//and that they need to set the address of that server in DynamicAssetLoader
			int newPort = Port;
			EditorGUI.BeginChangeCheck();
			newPort = EditorGUILayout.IntField("Port", Port);
			if (EditorGUI.EndChangeCheck())
			{
				if (newPort != Port)
				{
					if (_activeHost != null && _activeHost != "")
						ActiveHost = _activeHost.Replace(":" + Port.ToString(), ":" + newPort.ToString());
					Port = newPort;
					UpdateHosts();
					//we need to start the server to see if it works with this port- regardless of whether it is turned on or not.
					if (!EnableLocalAssetBundleServer)
					{
						UpdateServer(true);
					}
					else
					{
						UpdateServer();
					}
					if (serverException == false)
					{
						//We can use the set IP with this port so update it
						if (EnableLocalAssetBundleServer)
							SimpleWebServer.ServerURL = ActiveHost;
					}
					else
					{
						//We CANT use the set IP with this port so set the saved URL to "" and tell the user the Port is in use elsewhere
						SimpleWebServer.ServerURL = "";
						EnableLocalAssetBundleServer = false;
						portError = true;
					}
				}
			}
			if (!EnableLocalAssetBundleServer)
			{
				if (portError)
					EditorGUILayout.HelpBox("There are no hosts available for that port. Its probably in use by another application. Try another.", MessageType.Warning);
				else
				{
					EditorGUILayout.HelpBox("When the local server is not running the game will play in Simulation Mode OR if you have set the 'RemoteServerURL' for each DynamicAssetLoader, bundles will be downloaded from that location.", MessageType.Warning);
					if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
					{
						EditorGUILayout.HelpBox("WARNING: AssetBundles in WebGL builds that you run locally WILL NOT WORK unless the local server is turned on, and you build using the button below!", MessageType.Warning);
					}
				}
			}

			EditorGUILayout.Space();

			if (_hosts != null && _hosts.Length > 0 && EnableLocalAssetBundleServer)
			{
				if (_activeHost == null || _activeHost == "")
				{
					ActiveHost = _hosts[0];
				}
				int activeHostInt = 0;
				string[] hostsStrings = new string[_hosts.Length];
				for (int i = 0; i < _hosts.Length; i++)
				{
					hostsStrings[i] = _hosts[i].Replace("http://", "").TrimEnd(new char[] { '/' });
					if (_hosts[i] == _activeHost)
					{
						activeHostInt = i;
					}
				}
				EditorGUI.BeginChangeCheck();
				int newActiveHostInt = EditorGUILayout.Popup("Host Address:  http://", activeHostInt, hostsStrings);
				if (EditorGUI.EndChangeCheck())
				{
					if (newActiveHostInt != activeHostInt)
					{
						ActiveHost = _hosts[newActiveHostInt];
						updateURL = true;
					}
				}
			}
			EditorGUILayout.Space();

			if (showClearCache)//no point in showing a button for bundles that dont exist - or is there? The user might be using a remote url to download assetbundles without the localserver?
			{
				EditorGUILayout.HelpBox("You can clear the cache to force asset bundles to be redownloaded.", MessageType.Info);

				if (GUILayout.Button("Clean the Cache"))
				{
#if UNITY_2017_1_OR_NEWER
					_statusMessage = Caching.ClearCache() ? "Cache Cleared." : "Error clearing cache.";
#else
					_statusMessage = Caching.CleanCache() ? "Cache Cleared." : "Error clearing cache.";
#endif
				}
				EditorGUILayout.Space();
			}

			EditorGUILayout.Space();
			GUILayout.Label("Server Status");
			if (_statusMessage != null)
			{
				EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
			}

			if (SimpleWebServer.Instance != null)
			{
				//GUILayout.Label("Server Request Log");
				serverRequestLogOpen = EditorGUILayout.Foldout(serverRequestLogOpen, "Server Request Log");
				if(serverRequestLogOpen)
                EditorGUILayout.HelpBox(SimpleWebServer.Instance.GetLog(), MessageType.Info);
			}
			if (updateURL)
			{
				SimpleWebServer.ServerURL = ActiveHost;
			}

			EndVerticalPadded(5);
			EditorGUILayout.Space();

			//Testing Build- only show this if we can run a build for the current platform (i.e. if its not iOS or Android)
			if (BuildScript.CanRunLocally (EditorUserBuildSettings.activeBuildTarget)) 
			{
				BeginVerticalPadded (5, new Color (0.75f, 0.875f, 1f));
				GUILayout.Label ("Local Testing Build", EditorStyles.boldLabel);
				//if the bundles are built and the server is turned on then the user can use this option otherwise there is no point
				//But we will show them that this option is available even if this is not the case
				if (!showClearCache || !EnableLocalAssetBundleServer) {
					EditorGUI.BeginDisabledGroup (true);
				}
				EditorGUILayout.HelpBox ("Make a testing Build that uses the Local Server using the button below.", MessageType.Info);

				developmentBuild = EditorGUILayout.Toggle ("Development Build", developmentBuild);
				if (GUILayout.Button("Build and Run!"))
				{
					BuildScript.BuildAndRunPlayer(developmentBuild);
					GUIUtility.ExitGUI();
				}
				else
				{
					if (!showClearCache || !EnableLocalAssetBundleServer)
					{
						EditorGUI.EndDisabledGroup();
					}
				}
			}
			EditorGUILayout.Space();
			EndVerticalPadded(5);
			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();

		}

		#region GUI HELPERS
		//these are copied from UMAs GUI Helper- but that is in an editor folder

		public static void BeginVerticalPadded(float padding, Color backgroundColor)
		{
			GUI.color = backgroundColor;
			GUILayout.BeginHorizontal(EditorStyles.textField);
			GUI.color = Color.white;
			GUILayout.Space(padding);
			GUILayout.BeginVertical();
			GUILayout.Space(padding);
		}

		public static void EndVerticalPadded(float padding)
		{
			GUILayout.Space(padding);
			GUILayout.EndVertical();
			GUILayout.Space(padding);
			GUILayout.EndHorizontal();
		}

		public static void BeginVerticalIndented(float indentation, Color backgroundColor)
		{
			GUI.color = backgroundColor;
			GUILayout.BeginHorizontal();
			GUILayout.Space(indentation);
			GUI.color = Color.white;
			GUILayout.BeginVertical();
		}

		public static void EndVerticalIndented()
		{
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}
		#endregion
	}


	[System.Serializable]
	public class UMAABMSettingsStore
	{
		public enum BundleIndexVersioningOpts { UseBuildVersion, Custom }

		public bool encryptionEnabled = false;
		public string encryptionPassword = "";
		public string encryptionSuffix = "";
		public bool encodeNames = false;
		[Tooltip("If true will build uncompressed assetBundles for use with iOS resource catalogs")]
		public bool buildForAppSlicing = false;

		public bool enableBundleIndexVersioning;
		//either automatically use the Buildversion from player settings or specify a value here
		public BundleIndexVersioningOpts bundleIndexVersioningMethod = BundleIndexVersioningOpts.UseBuildVersion;
		//if useBundlesVersioning is set to Custom this value is used when the bundles are built
		public string bundleIndexCustomValue = "0.0";

		public UMAABMSettingsStore() { }

		public UMAABMSettingsStore(bool _encryptionEnabled,  string _encryptionPassword, string _encryptionSuffix, bool _encodeNames)
		{
			_encryptionEnabled = encryptionEnabled;
			encryptionPassword = _encryptionPassword;
			encryptionSuffix = _encryptionSuffix;
			encodeNames = _encodeNames;
		}
	}

	public static class UMAABMSettings
	{
		#region PUBLIC FIELDS

		//This is not just for Encryption settings any more so it has the wrong name- its editor only so we can change it...
		public const string SETTINGS_FILENAME = "UMAABMSettings-DoNotDelete.txt";
		public const string SETTINGS_OLD_FILENAME = "UMAEncryptionSettings-DoNotDelete.txt";

		private static UMAABMSettingsStore thisSettings;
		#endregion

		#region PROPERTIES
		//pretty much all of these should have been properties- sorry I didn't uderstand those very well at the time

		//This is better but we dont want to be doing GetThisSettings() either we want another property that does it once
		/// <summary>
		/// Is BundlesIndexVersioning enabled
		/// </summary>
		public static bool EnableBundleIndexVersioning
		{
			get { return GetThisSettings().enableBundleIndexVersioning; }
			set{ GetThisSettings().enableBundleIndexVersioning = value;
				File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(thisSettings));
				AssetDatabase.Refresh();
			}
		}

		/// <summary>
		/// Does BundlesIndexversioning use a custom value or the Player build Value
		/// </summary>
		public static UMAABMSettingsStore.BundleIndexVersioningOpts BundleIndexVersioningMethod
		{
			get { return GetThisSettings().bundleIndexVersioningMethod; }
			set { GetThisSettings().bundleIndexVersioningMethod = value;
				File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(thisSettings));
				AssetDatabase.Refresh();
			}
		}
		/// <summary>
		/// A custom value for bundleIndexVersioning
		/// </summary>
		public static string BundleIndexCustomValue
		{
			get { return GetThisSettings().bundleIndexCustomValue; }
			set {
				GetThisSettings().bundleIndexCustomValue = value;
				File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(thisSettings));
				AssetDatabase.Refresh();
			}
		}


		#endregion

		#region STATIC LOAD SAVE Methods
		private static UMAABMSettingsStore GetThisSettings()
		{
			thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				thisSettings = new UMAABMSettingsStore();
			return thisSettings;
		}

		private static string GetSettingsFolderPath()
		{
			return FileUtils.GetInternalDataStoreFolder(false,true);
		}

		//we are saving the encryptions settings to a text file so that teams working on the same project/ github etc/ can all use the same settings
		public static UMAABMSettingsStore GetEncryptionSettings()
		{
			if(File.Exists(Path.Combine(GetSettingsFolderPath(), SETTINGS_OLD_FILENAME)))
			{
				//we need to write the data from the old file into the new file and delete the old one...
				File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), File.ReadAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_OLD_FILENAME)));
				File.Delete(Path.Combine(GetSettingsFolderPath(), SETTINGS_OLD_FILENAME));
            }
			if (!File.Exists(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME)))
				return null;
			else
				return JsonUtility.FromJson<UMAABMSettingsStore>(File.ReadAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME)));
		}

		public static bool GetEncryptionEnabled()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				return false;
			else if (thisSettings.encryptionEnabled && thisSettings.encryptionPassword != "")
				return true;
			else
				return false;
		}
		public static string GetEncryptionPassword()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				return "";
			else
				return thisSettings.encryptionPassword;
		}
		public static string GetEncryptionSuffix()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				return "";
			else
				return thisSettings.encryptionSuffix;
		}
		public static bool GetEncodeNames()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				return false;
			else
				return thisSettings.encodeNames;
		}
		/// <summary>
		/// Should the bundles be built for use with iOS app slicing
		/// </summary>
		public static bool GetBuildForSlicing()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				return false;
			else
				return thisSettings.buildForAppSlicing;
		}

		public static void ClearEncryptionSettings()
		{
			var thisSettings = GetEncryptionSettings();
			var newSettings = new UMAABMSettingsStore();
			newSettings.buildForAppSlicing = thisSettings.buildForAppSlicing;
			newSettings.bundleIndexCustomValue = thisSettings.bundleIndexCustomValue;
			newSettings.bundleIndexVersioningMethod = thisSettings.bundleIndexVersioningMethod;
			newSettings.enableBundleIndexVersioning = thisSettings.enableBundleIndexVersioning;
			File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(newSettings));
		}

		public static void SetEncryptionSettings(bool encryptionEnabled, string encryptionPassword = "", string encryptionSuffix = "", bool? encodeNames = null)
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				thisSettings = new UMAABMSettingsStore();
			thisSettings.encryptionEnabled = encryptionEnabled;
			thisSettings.encryptionPassword = encryptionPassword != "" ? encryptionPassword : thisSettings.encryptionPassword;
			thisSettings.encryptionSuffix = encryptionSuffix != "" ? encryptionSuffix : thisSettings.encryptionSuffix;
			thisSettings.encodeNames = encodeNames != null ? (bool)encodeNames : thisSettings.encodeNames;
			File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(thisSettings));
			AssetDatabase.Refresh();
		}
		/// <summary>
		/// Turns encryption OFF
		/// </summary>
		public static void DisableEncryption()
		{
			SetEncryptionSettings(false);
		}
		/// <summary>
		/// Turns encryption ON ands sets the given password (cannot be blank)
		/// </summary>
		public static void SetEncryptionPassword(string encryptionPassword)
		{
			if (encryptionPassword != "")
			{
                SetEncryptionSettings(true, encryptionPassword);
			}
		}
		/// <summary>
		/// Turns encryption OFF ands unsets any existing password
		/// </summary>
		public static void UnsetEncryptionPassword()
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				thisSettings = new UMAABMSettingsStore();
			thisSettings.encryptionEnabled = false;
			thisSettings.encryptionPassword = "";
			File.WriteAllText(Path.Combine(GetSettingsFolderPath(), SETTINGS_FILENAME), JsonUtility.ToJson(thisSettings));
			AssetDatabase.Refresh();
		}
		/// <summary>
		/// Turns encryption ON ands sets the given encryption suffix (cannot be blank)
		/// </summary>
		public static void SetEncryptionSuffix(string encryptionSuffix)
		{
			if (encryptionSuffix != "")
			{
				SetEncryptionSettings(true, "", encryptionSuffix);
			}
		}
		/// <summary>
		/// Turns encryption ON ands sets the encode names setting
		/// </summary>
		public static void SetEncodeNames(bool encodeNames)
		{
			SetEncryptionSettings(true,"", "", encodeNames);
		}

		/// <summary>
		/// Should the bundles be built for use with iOS app slicing
		/// </summary>
		public static void SetBuildForSlicing(bool enabled)
		{
			var thisSettings = GetEncryptionSettings();
			if (thisSettings == null)
				thisSettings = new UMAABMSettingsStore();
			thisSettings.buildForAppSlicing = enabled;
		}

		#endregion
	}
}
#endif
