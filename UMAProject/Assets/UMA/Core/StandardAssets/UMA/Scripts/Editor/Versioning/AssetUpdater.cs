using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace kode80.Versioning
{
	public class AssetUpdater
	{
		public delegate void RemoteVersionDownloadFinished( AssetUpdater updater, int assetIndex);
		public RemoteVersionDownloadFinished remoteVersionDownloadFinished;

		public delegate void RemoteVersionDownloadFailed( AssetUpdater updater, int assetIndex);
		public RemoteVersionDownloadFailed remoteVersionDownloadFailed;

		private static AssetUpdater _instance;
		public static AssetUpdater Instance {
			get {
				if( _instance == null) {
					_instance = new AssetUpdater();
				}
				return _instance;
			}
		}

		private List<AssetVersion> _localVersions;
		private Dictionary<AssetVersion,AssetVersion> _localToRemoteVersions;
		private AssetVersionDownloader _downloader;

		public int AssetCount { get { return _localVersions.Count; } }

		private AssetUpdater()
		{
			_localVersions = new List<AssetVersion>();
			_localToRemoteVersions = new Dictionary<AssetVersion, AssetVersion>();
		}

		public void Refresh( bool forceRefresh = false)
		{
			List<AssetVersion> localVersions = FindLocalVersions();
			if( forceRefresh || VersionListsAreEqual( localVersions, _localVersions) == false)
			{
				if( _downloader != null) {
					_downloader.CancelAll();
					_downloader.remoteVersionDownloadFinished -= RemoteVersionDownloaderFinished;
					_downloader.remoteVersionDownloadFailed -= RemoteVersionDownloaderFailed;
				}

				_downloader = new AssetVersionDownloader();
				_downloader.remoteVersionDownloadFinished += RemoteVersionDownloaderFinished;
				_downloader.remoteVersionDownloadFailed += RemoteVersionDownloaderFailed;

				_localVersions = localVersions;
				foreach( AssetVersion local in _localVersions) {
					_downloader.Add( local);
				}
			}
		}

		public AssetVersion GetLocalVersion( int index) {
			bool validIndex = index >= 0 && index < _localVersions.Count;
			return validIndex ? _localVersions[ index] : null;
		}

		public AssetVersion GetRemoteVersion( int index) {
			AssetVersion localVersion = GetLocalVersion( index);

			if( _localToRemoteVersions.ContainsKey( localVersion)) {
				return _localToRemoteVersions[ localVersion];
			}

			return null;
		}

		#region AssetVersionDownloader delegate

		private void RemoteVersionDownloaderFinished( AssetVersion local, AssetVersion remote)
		{
			_localToRemoteVersions[ local] = remote;

			if( remoteVersionDownloadFinished != null)
			{
				remoteVersionDownloadFinished( this, _localVersions.IndexOf( local));
			}
		}

		private void RemoteVersionDownloaderFailed( AssetVersion local)
		{
			if( remoteVersionDownloadFailed != null)
			{
				remoteVersionDownloadFailed( this, _localVersions.IndexOf( local));
			}
		}

		#endregion

		private List<AssetVersion> FindLocalVersions()
		{
			List<AssetVersion> versions = new List<AssetVersion>();
			string[] paths = Directory.GetFiles( Application.dataPath, "AssetVersion.xml", SearchOption.AllDirectories);

			foreach( string path in paths)
			{
				string localXML = File.ReadAllText( path);
				AssetVersion version = AssetVersion.ParseXML( localXML);

				if( version != null) {
					versions.Add( version);
				}
			}

			return versions;
		}

		private bool VersionListsAreEqual( List<AssetVersion> a, List<AssetVersion> b)
		{
			if( a == b) { return true; }
			if( a.Count != b.Count) { return false; }

			Dictionary<string,bool> hash = new Dictionary<string, bool>();

			foreach( AssetVersion version in a) { 
				hash[ version.ToString()] = true; 
			}

			foreach( AssetVersion version in b) { 
				if( hash.ContainsKey( version.ToString()) == false) { 
					return false; 
				} 
			}

			return true;
		}
	}
}
