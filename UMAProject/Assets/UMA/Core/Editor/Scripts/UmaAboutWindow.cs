using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA
{
    public class UmaAboutWindow : EditorWindow 
    {
        public static string umaVersion { get { return _version; } }
        private static string _version = "2.10";
        private string windowTitle = "UMA About";
        private string wikiLink = "http://umadocs.secretanorak.com/doku.php";
        private string githubLink = "https://github.com/umasteeringgroup";
        private string unityThreadLink = "https://forum.unity.com/threads/uma-unity-multipurpose-avatar-on-the-asset-store.219175/";
        private string umaForumLink = "http://umacommunity.boards.net/forum";
        private string slackLink = "https://uma-community.slack.com";
        private string discordLink = "https://discord.gg/KdteVKd";

        private Vector2 size = new Vector2(400, 300);

        private Texture _BannerTexture;
        private Rect _BannerRect = new Rect(0,0,200,53);

        private Texture _IconTexture;

        private bool initialized = false;

        private GUIStyle centeredStyle = new GUIStyle();

        [MenuItem("UMA/About", false, 0)]
        static void Init()
        {
            // Get existing open window or if none, make a new one:
            UmaAboutWindow window = (UmaAboutWindow)EditorWindow.GetWindow(typeof(UmaAboutWindow));
            window.Show();
        }

        void OnGUI()
        {
            Initialize();

            if (!initialized)
                return;

            Rect centered = _BannerRect;
            centered.center = new Vector2(size.x *0.5f, _BannerRect.yMax*0.5f);
            GUI.DrawTexture(centered, _BannerTexture);
            GUILayout.Space(60);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format("Version: {0}", umaVersion), centeredStyle);
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Wiki Documentation"))
            {
                Application.OpenURL(wikiLink);
            }
                
            if (GUILayout.Button("Github"))
            {
                Application.OpenURL(githubLink);
            }

            GUILayout.Space(30);
            EditorGUILayout.LabelField("For Help", centeredStyle);

            if (GUILayout.Button("Unity Forum Thread"))
            {
                Application.OpenURL(unityThreadLink);
            }
            if(GUILayout.Button("UMA Forum"))
            {
                Application.OpenURL(umaForumLink);
            }
            if (GUILayout.Button("Slack Channel"))
            {
                Application.OpenURL(slackLink);
            }
            if(GUILayout.Button("Secret Anorak's Discord Channel"))
            {
                Application.OpenURL(discordLink);
            }
        }

        void Initialize()
        {
            if (_BannerTexture == null)
                _BannerTexture = UMAUtils.LoadTextureAsset("UmaBanner");

            if (_IconTexture == null)
                _IconTexture = UMAUtils.LoadTextureAsset("UMA32");

            if (!initialized)
            {
                minSize = size;
                maxSize = size;

                titleContent.text = windowTitle;
                titleContent.image = _IconTexture;

                initialized = true;

                centeredStyle.alignment = TextAnchor.MiddleCenter;
            }                
        }
    }
}
