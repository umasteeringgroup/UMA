using System;
using System.Net;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.AssetBundles
{
    public class SimpleWebServer
    {
        HttpListener _listener = new HttpListener();
        string _hostedFolder;
        static System.Collections.Generic.List<string> _requestLog = new System.Collections.Generic.List<string>();

        static string _serverURL = "";
        public static string ServerURL {
            get
            {
                if(_serverURL == "")
                {
                    GetServerURL();
                }
                return _serverURL;
            }
            set
            {
                _serverURL = value;
            }
        }
        public static SimpleWebServer Instance;
        public static bool serverStarted = false;
        public int Port { get; private set; }
        public SimpleWebServer(int port)
        {

            _hostedFolder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "AssetBundles");
            Port = port;
            //If the window is closed and then re-opened we get an error that this is already in use so TryCatch
            //The server doesn't run unless the window is open anyway
            try {
                _listener.Prefixes.Add(string.Format("http://*:{0}/", port));
            }
            catch {
				if (Debug.isDebugBuild)
					Debug.LogWarning("[Web Server] Could not add prefix to listener");
            }
            _listener.Start();
            _listener.BeginGetContext(OnGetContext, null);
            serverStarted = true;
        }

        public static SimpleWebServer Start(int port)
        {
            if(Instance != null)
            {
                Instance.Stop();
            }
            Instance = new SimpleWebServer(port);
            return Instance;
        }

        void OnGetContext(IAsyncResult async)
        {
            // start listening for the next request
            _listener.BeginGetContext(OnGetContext, null);
            var context = _listener.EndGetContext(async);
            try
            {
                if (context.Request.RawUrl == "/")
                {
					//if (Debug.isDebugBuild)//isDebugBuild can only be called from the main thread
					Debug.Log("[WebServer] context.Request.RawUrl");
                    context.Response.StatusCode = 200;
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    string msg = string.Format(@"<html><body><h1>UMA Simple Web Server</h1><table>
						<tr><td>Host Application</td><td>{0} (Process Id: {1})</td></tr>
						<tr><td>Working Directory</td><td>{2}</td></tr>
						</table><br><br>{3}</body></html>", process.ProcessName, process.Id, System.IO.Directory.GetCurrentDirectory(), GetLog("<br>"));
                    var data = System.Text.Encoding.UTF8.GetBytes(msg);
                    context.Response.OutputStream.Write(data, 0, data.Length);
                    context.Response.OutputStream.Close();
                    //Tried adding response close aswell like in Adamas original
                    context.Response.Close();
                }
                else
                {
                    var filePath = System.IO.Path.Combine(_hostedFolder, context.Request.RawUrl.Substring(1));
                    if (System.IO.File.Exists(filePath))
                    {
                        using (var file = System.IO.File.Open(filePath, System.IO.FileMode.Open))
                        {
                            var buffer = new byte[file.Length];
                            file.Read(buffer, 0, (int)file.Length);
                            context.Response.ContentLength64 = file.Length;
                            context.Response.StatusCode = 200;
                            context.Response.OutputStream.Write(buffer, 0, (int)file.Length);
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        //if (Debug.isDebugBuild)//isDebugBuild can only be called from the main thread
                            UnityEngine.Debug.LogErrorFormat("Url not served. Have you built your Asset Bundles? Url not served from: {0} '{1}'", context.Request.RawUrl, filePath);
#if UNITY_EDITOR
                        AssetBundleManager.SimulateOverride = true;
                        context.Response.OutputStream.Close();
                        //Tried adding response close aswell like in Adamas original
                        context.Response.Abort();
                        return;
#endif
                    }
                }
                lock (_requestLog)
                {
                    _requestLog.Add(string.Format("{0} {1}", context.Response.StatusCode, context.Request.Url));
                }
                context.Response.OutputStream.Close();
                context.Response.Close();
            }
            catch (HttpListenerException e)
            {
                if (e.ErrorCode == -2147467259)
                {
					// shutdown, terminate silently
					//if (Debug.isDebugBuild)//isDebugBuild can only be called from the main thread
					Debug.LogWarning("[Web Server] ErrorCode -2147467259: terminate silently");
                    context.Response.Abort();
                    return;
                }
				// if (Debug.isDebugBuild)//isDebugBuild can only be called from the main thread
				UnityEngine.Debug.LogException(e);
                context.Response.Abort();
            }
            catch (Exception e)
            {
				//if (Debug.isDebugBuild)//isDebugBuild can only be called from the main thread
				UnityEngine.Debug.LogException(e);
                context.Response.Abort();
            }
        }

        public string GetLog(string lineBreak = "\n")
        {
            var sb = new System.Text.StringBuilder();
            lock (_requestLog)
            {
                if (_requestLog.Count > 50)
                {
                    _requestLog.RemoveRange(0, _requestLog.Count - 50);
                }
                for (int i = 0; i < _requestLog.Count; i++)
                {
                    sb.Append(_requestLog[i]);
                    sb.Append(lineBreak);
                }
            }
            return sb.ToString();
        }

        public void Stop()
        {
            serverStarted = false;
            _listener.Stop();
            _listener.Close();
            Instance = null;
        }
#if UNITY_EDITOR
        //we need one of these included in the builds generated from our 'Build and Run' button
        public static void WriteServerURL()
        {
            if(_serverURL != "")
            {
				string serverUrlPath = Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "localServerURL.bytes");
				UMA.FileUtils.WriteAllText(serverUrlPath, _serverURL);
				AssetDatabase.Refresh();
            }
        }
        //but we dont want it hanging around afterwards
        public static void DestroyServerURLFile()
        {
			string serverUrlPath = Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "localServerURL.bytes");
			string serverUrlMetaPath = Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "localServerURL.bytes.meta");
			File.Delete(serverUrlMetaPath);
			File.Delete(serverUrlPath);
			AssetDatabase.Refresh();
		}
#endif
        //because in the editor we dont want this to set anything
        static void GetServerURL()
        {
            TextAsset urlFile = Resources.Load("localServerURL") as TextAsset;
            string serverURL = (urlFile != null) ? urlFile.text.Trim() : "";
            _serverURL = serverURL;
        }
    }
}
