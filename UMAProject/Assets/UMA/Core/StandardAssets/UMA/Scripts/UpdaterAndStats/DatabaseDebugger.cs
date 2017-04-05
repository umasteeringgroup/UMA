using UnityEngine;

public class DatabaseDebugger : MonoBehaviour
{
	public string version = "2.1.0";
	public bool sendDl = false;
	public bool getStats = false;
	public bool addRow = false;

	private void Update()
	{
		if(sendDl)
		{
			sendDl = false;

			WWWForm form = new WWWForm();
			form.AddField("hash", DatabaseInterface.hash);
			form.AddField("type", "dlStat");
			form.AddField("version", version);
			
			StartCoroutine(DatabaseInterface.DbRequest(form, PrintResult));
		}
		if(getStats)
		{
			getStats = false;

			WWWForm form = new WWWForm();
			form.AddField("hash", DatabaseInterface.hash);
			form.AddField("type", "getStats");

			StartCoroutine(DatabaseInterface.DbRequest(form, PrintResult));
		}

		if(addRow)
		{
			addRow = false;

			WWWForm form = new WWWForm();
			form.AddField("hash", DatabaseInterface.hash);
			form.AddField("type", "addRow");
			form.AddField("version", version);

			StartCoroutine(DatabaseInterface.DbRequest(form, PrintResult));
		}
	}

	private void PrintResult(string result)
	{
		Debug.Log(result);
	}
}
