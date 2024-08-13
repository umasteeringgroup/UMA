using UnityEngine;
using System.Text;
using UnityEngine.UI;

namespace UMA.CharacterSystem.Examples
{
    public class LogToText : MonoBehaviour
	{
		StringBuilder buffer = new StringBuilder();
		bool changed;

		// Use this for initialization
		void Start()
		{
			Application.logMessageReceivedThreaded += Application_logMessageReceivedThreaded;
		}

		private void Application_logMessageReceivedThreaded(string condition, string stackTrace, LogType type)
		{
			lock (buffer)
			{
				switch (type)
				{
					case LogType.Log:
						buffer.AppendFormat("{0}\n", condition);
						break;
					case LogType.Warning:
						buffer.AppendFormat("<color=yellow>{0}</color>\n", condition);
						break;
					default:
						buffer.AppendFormat("<color=red>{0}</color>\n", condition);
						break;
				}
			}
			changed = true;
		}

		// Update is called once per frame
		void Update()
		{
			if (changed)
			{
				changed = false;
				lock (buffer)
				{
					var text = buffer.ToString();
					var lines = text.Split('\n');
					if (lines.Length > 65)
					{
						buffer.Length = 0;
						for (int i = lines.Length - 65; i < lines.Length; i++)
                        {
                            buffer.AppendFormat("{0}", lines[i]);
                        }

                        text = buffer.ToString();
					}
					GetComponent<Text>().text = text;
				}
			}
		}
	}
}
