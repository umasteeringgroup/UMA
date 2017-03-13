using UnityEditor;
using UnityEngine;
using kode80.Versioning;

[InitializeOnLoad]
public class UpdateChecker
{
    static UpdateChecker()
    {
        EditorApplication.update += RunOnce;
    }
    private static void RunOnce()
    {
        float startupTime = EditorPrefs.GetFloat("UMA_EditorTime");
        
        if(startupTime < 90)
            startupTime = 90;
            
        if(startupTime > EditorApplication.timeSinceStartup)
        {
            EditorPrefs.SetFloat("UMA_EditorTime", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= RunOnce;

            AssetUpdater.Instance.Refresh();

            AssetUpdater.Instance.remoteVersionDownloadFinished += RemoteVersionDownloadFinished;
            AssetUpdater.Instance.remoteVersionDownloadFailed += RemoteVersionDownloadFailed;
        }
    }

    private static void RemoteVersionDownloadFinished( AssetUpdater updater, int assetIndex)
    {
        AssetVersion remote = updater.GetRemoteVersion(0);
        AssetVersion local = updater.GetLocalVersion(0);

        AssetUpdater.Instance.remoteVersionDownloadFinished -= RemoteVersionDownloadFinished;
        AssetUpdater.Instance.remoteVersionDownloadFailed -= RemoteVersionDownloadFailed;

        if(remote.Version.ToString() != local.Version.ToString())
        {
            AssetUpdateWindow.Init();
        }
    }

    private static void RemoteVersionDownloadFailed( AssetUpdater updater, int assetIndex)
    {
        AssetUpdater.Instance.remoteVersionDownloadFinished -= RemoteVersionDownloadFinished;
        AssetUpdater.Instance.remoteVersionDownloadFailed -= RemoteVersionDownloadFailed;
        //Debug.Log("Failed to get remote UMA version.");
    }
}