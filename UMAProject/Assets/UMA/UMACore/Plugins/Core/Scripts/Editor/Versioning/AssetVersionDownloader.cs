using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace kode80.Versioning
{
	public class AssetVersionDownloader 
	{
		public delegate void RemoteVersionDownloadFinished( AssetVersion local, AssetVersion remote);
		public RemoteVersionDownloadFinished remoteVersionDownloadFinished;

		public delegate void RemoteVersionDownloadFailed( AssetVersion local);
		public RemoteVersionDownloadFailed remoteVersionDownloadFailed;

		private WebClient _webClient;
		private List<AssetVersion> _queue;
		private AssetVersion _currentLocalVersion;
		private List<Action> _mainThreadDelegates;

		public AssetVersionDownloader()
		{
			_queue = new List<AssetVersion>();
			_mainThreadDelegates = new List<Action>();
			EditorApplication.update += MainThreadUpdate;
            ServicePointManager.ServerCertificateValidationCallback += HandleServerCertificateValidation;
        }

		~AssetVersionDownloader()
		{
			EditorApplication.update -= MainThreadUpdate;
            ServicePointManager.ServerCertificateValidationCallback -= HandleServerCertificateValidation;
            CancelAll();
		}

        private static bool HandleServerCertificateValidation(object sender, X509Certificate cert, X509Chain chain, System.Net.Security.SslPolicyErrors error)
        {
            return true;
        }


        public void Add( AssetVersion local)
		{
			_queue.Add( local);
			AttemptNextDownload();
		}

		public void CancelAll()
		{
			if( _webClient != null) {
				_webClient.CancelAsync();
			}

			_mainThreadDelegates.Clear();
			_queue.Clear();
		}

		private void MainThreadUpdate()
		{
			if( _mainThreadDelegates.Count > 0)
			{
				Action action = _mainThreadDelegates[0];
				_mainThreadDelegates.RemoveAt( 0);
				action.Invoke();
			}
		}

		private void AttemptNextDownload()
		{
			if( _webClient == null && _queue.Count > 0)
			{
				_currentLocalVersion = _queue[0];
				_queue.RemoveAt( 0);

				using( _webClient = new WebClient())
				{
					_webClient.DownloadStringCompleted += WebClientCompleted;
                    
                    try {
						_webClient.DownloadStringAsync( _currentLocalVersion.versionURI);
					}
					catch( Exception e) {
                        Debug.Log("dl exception: " + e);
						HandleFailedDownload();
					}
				}
			}
		}

		private void WebClientCompleted(object sender, DownloadStringCompletedEventArgs e)
		{
			if( e.Cancelled || e.Error != null) 
			{
                if( e.Error != null)
                {
                    Debug.Log("dl complete error: " + e.Error);
                }
				HandleFailedDownload();
			}
			else 
			{
				AssetVersion remote = AssetVersion.ParseXML( e.Result);
				if( remote == null) 
				{
					HandleFailedDownload();
				}
				else 
				{
					HandleFinishedDownload( remote);
				}
			}
		}

		private void HandleFinishedDownload( AssetVersion remote)
		{
			if( remoteVersionDownloadFinished != null) {
				_mainThreadDelegates.Add( new Action( () => {
					remoteVersionDownloadFinished( _currentLocalVersion, remote);

					_currentLocalVersion = null;
					_webClient = null;
					AttemptNextDownload();
				}));
			}
		}

		private void HandleFailedDownload()
		{
			if( remoteVersionDownloadFailed != null) {
				_mainThreadDelegates.Add( new Action( () => {
					remoteVersionDownloadFailed( _currentLocalVersion);

					_currentLocalVersion = null;
					_webClient = null;
					AttemptNextDownload();
				}));
			}
		}
	}
}
