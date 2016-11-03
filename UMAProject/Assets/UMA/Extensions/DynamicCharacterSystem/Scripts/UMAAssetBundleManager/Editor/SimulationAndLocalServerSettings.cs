using UnityEngine;
using UnityEditor;
using UMAAssetBundleManager;
using System;
using System.IO;

public class LocalServerSettings : EditorWindow
{
    bool _enableLocalAssetBundleServer;
    bool EnableLocalAssetBundleServer
    {
        get { return _enableLocalAssetBundleServer; }
        set
        {
            if (_enableLocalAssetBundleServer == value) return;
            _enableLocalAssetBundleServer = value;
            EditorPrefs.SetBool("LocalAssetBundleServerEnabled", value);
            UpdateServer();
        }
    }
    int _port;
    int Port
    {
        get { return _port; }
        set
        {
            if (_port == value) return;
            _port = value;
            EditorPrefs.SetInt("LocalAssetBundleServerPort", _port);
            UpdateServer();
        }
    }
    //Not needed just access the instance directly
    //SimpleWebServer _webServer;
    string _statusMessage;
    string[] _hosts;

    string _activeHost;
    string ActiveHost
    {
        get { return _activeHost; }
        set
        {
            if (_activeHost == value) return;
            _activeHost = value;
            EditorPrefs.SetString("LocalAssetBundleServerURL", _activeHost);
        }
    }
    bool portError = false;
    bool serverException = false;
    //private BuildOptions buildOptionsMask = BuildOptions.None;
    bool developmentBuild = false;

    [MenuItem("Assets/AssetBundles/Local Asset Bundle Server")]
    static void Init()
    {
        LocalServerSettings window = (LocalServerSettings)EditorWindow.GetWindow<LocalServerSettings>("Local Server");
        window.Show();
    }

    void OnEnable()
    {
        _enableLocalAssetBundleServer = EditorPrefs.GetBool("LocalAssetBundleServerEnabled");
        _port = EditorPrefs.GetInt("LocalAssetBundleServerPort", 7888);
        //When the window is opened we still need to tell the user if the port is available so
        if (!_enableLocalAssetBundleServer)
        {
            UpdateServer(true);
            Stop();
            if (serverException)
                portError = true;
        }
        else
        {
            UpdateServer();
        }
    }

    void OnDisable()
    {
        //DOS MODIFIED We seem to need this else if the winow is closed and reopened we get a 'Listener already in use' error
        Stop();
    }
    void Stop()
    {
        if (SimpleWebServer.Instance != null)
        {
            SimpleWebServer.Instance.Stop();
            SimpleWebServer.ServerURL="";
            _statusMessage = "Server Stopped";
            _hosts = null;
        }
    }
    void Start()
    {
        //using static instance in SimpleWebServer so other things can restart it if necessary- REALLY IMPORTANT THIS DONT DELETE
        //@joen being able to restart the server from another script is VITAL to it working properly since very often it stops working for some unknown reason and needs to be restarted
        SimpleWebServer.Start(_port);
        _statusMessage = "Server Running";
        UpdateHosts();
        if(_activeHost == null)
        {
            ActiveHost = _hosts[0];
        }
        SimpleWebServer.ServerURL = ActiveHost;
    }

    void UpdateHosts()
    {
        var strHostName = System.Net.Dns.GetHostName();
        var ipEntry = System.Net.Dns.GetHostEntry(strHostName);
        var list = new System.Collections.Generic.List<string>();
        foreach (var addr in ipEntry.AddressList)
        {
            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                list.Add(string.Format("http://{0}:{1}/", addr, Port));
            }
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
                    Stop();
                    Debug.Log("Server Stopped");
                }
                else if (SimpleWebServer.Instance.Port != _port)
                {
                    Stop();
                    Start();
                    Debug.Log("Server Started");
                }
            }
            else if (EnableLocalAssetBundleServer || test)
            {
                Start();
                if(!test)
                    Debug.Log("Server Started");
            }
        }
        catch (Exception e)
        {
            _statusMessage = string.Format("Simple Webserver Exception: {0}\nStack Trace\n{1}", e.ToString(), e.StackTrace);
            Debug.LogException(e);
            EditorPrefs.SetBool("LocalAssetBundleServerEnabled", false);
            EnableLocalAssetBundleServer = false;
            serverException = true;
            Stop();
        }
    }

    void OnGUI()
    {
        bool updateURL = false;
        GUILayout.Label("Local Asset Bundle Server", EditorStyles.boldLabel);
        if (!BuildScript.CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
        {
            EditorGUILayout.HelpBox("Builds for "+ EditorUserBuildSettings.activeBuildTarget.ToString()+" cannot access this local server, but you can still use it in the editor.", MessageType.Warning);
        }
        EnableLocalAssetBundleServer = EditorGUILayout.Toggle("Start Server", EnableLocalAssetBundleServer);
        //DOS MODIFIED
        //If the server is off we need to show the user a message telling them that they will have to have uploaded their bundles to an external server
        //and that they need to set the address of that server in DynamicAssetLoader
        int newPort = Port;
        EditorGUI.BeginChangeCheck();
        newPort = EditorGUILayout.IntField("Port", Port);
        if (EditorGUI.EndChangeCheck())
        {
            if(newPort != Port)
            {
                if(_activeHost != null && _activeHost != "")
                ActiveHost = _activeHost.Replace(":"+Port.ToString(), ":"+newPort.ToString());
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
                    if(EnableLocalAssetBundleServer)
                        SimpleWebServer.ServerURL = ActiveHost;
                }
                else
                {
                    //We CANT use the set IP with this port so set the saved URL to "" and tell the user the Port is in use elsewhere
                    SimpleWebServer.ServerURL ="";
                    EnableLocalAssetBundleServer = false;
                    portError = true;
                }
            }
        }
        if (!EnableLocalAssetBundleServer)
        {
            if(portError)
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
            for(int i = 0; i < _hosts.Length; i++)
            {
                hostsStrings[i] = _hosts[i].Replace("http://", "").TrimEnd(new char[] {'/'});
                if (_hosts[i] == _activeHost)
                {
                    activeHostInt = i;
                }
            }
            EditorGUI.BeginChangeCheck();
            int newActiveHostInt = EditorGUILayout.Popup("Host Address:  http://",activeHostInt, hostsStrings);
            if (EditorGUI.EndChangeCheck())
            {
                if(newActiveHostInt != activeHostInt)
                {
                    ActiveHost = _hosts[newActiveHostInt];
                    updateURL = true;
                }
            }
        }
        EditorGUILayout.Space();
        string buttonBuildAssetBundlesText = "Build AssetBundles";
        string buildBundlesText = "Click the button below to build your bundles if you have not done so already.";
        string fullPathToBundles =  Path.Combine(Directory.GetParent(Application.dataPath).FullName, Utility.AssetBundlesOutputPath);
        string fullPathToPlatformBundles = Path.Combine(fullPathToBundles, Utility.GetPlatformName());
        bool showClearCache = false;
        MessageType AssetBundleBuildInfoStyle = MessageType.Info;
        if (Directory.Exists(fullPathToPlatformBundles))
        {
            buttonBuildAssetBundlesText = "Rebuild AssetBundles";
            buildBundlesText = "Rebuild your assetBundles to reflect your latest changes";
            showClearCache = true;
        }
        else
        {
            buildBundlesText = "You have not built your asset bundles for "+ EditorUserBuildSettings.activeBuildTarget.ToString()+" yet. Click this button to build them.";
            AssetBundleBuildInfoStyle = MessageType.Warning;
            showClearCache = false;
        }
        EditorGUILayout.HelpBox(buildBundlesText, AssetBundleBuildInfoStyle);
        if (GUILayout.Button(buttonBuildAssetBundlesText))
        {
            try
            {
                BuildScript.BuildAssetBundles();
                _statusMessage = "Asset Bundles Built";
            }
            catch (Exception e)
            {
                _statusMessage = string.Format("Building Asset Bundle Exception: {0}\nStack Trace\n{1}", e.ToString(), e.StackTrace);
                Debug.LogException(e);
            }
        }
        if (showClearCache)//no point in showing a button for bundles that dont exist - or is there? The user might be using a remote url to download assetbundles without the localserver?
        {
            EditorGUILayout.HelpBox("You can clear the cache to force asset bundles to be redownloaded.", MessageType.Info);

            if (GUILayout.Button("Clean the Cache"))
            {
                _statusMessage = Caching.CleanCache() ? "Cache Cleared." : "Error clearing cache.";
            }
            EditorGUILayout.Space();
        }
        //if the bundles are build and the server is turned on then the user can use this option otherwise there is no point
        //But we will show them that this option is available even if this is not the case
        if (!showClearCache || !EnableLocalAssetBundleServer)
        {
            EditorGUILayout.HelpBox("Once you have built your bundles the local server (if enabled) will load those AssetBundles rather than the files inside the project.", MessageType.Info);

            EditorGUI.BeginDisabledGroup(true);
        }
        GUILayout.Label("Testing Build", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Make a testing Build that uses the Local Server using the button below.", MessageType.Info);
        developmentBuild = EditorGUILayout.Toggle("Development Build", developmentBuild);
        if (GUILayout.Button("Build and Run!"))
        {
            BuildScript.BuildAndRunPlayer(developmentBuild);
        }
        if (!showClearCache || !EnableLocalAssetBundleServer)//
        {
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.Space();
        EditorGUILayout.Space();
        if (_statusMessage != null)
        {
            GUILayout.Label(_statusMessage);
            EditorGUILayout.Space();
            EditorGUILayout.Space();
        }

        if (SimpleWebServer.Instance != null)
        {
            GUILayout.Label("Server Request Log");
            EditorGUILayout.HelpBox(SimpleWebServer.Instance.GetLog(), MessageType.Info);
        }
        if (updateURL)
        {
            SimpleWebServer.ServerURL = ActiveHost;
        }
    }
}