using UnityEngine;
using System.Collections;
using System;

public static class DatabaseInterface : object
{
	private static string URL = "http://umawiki.secretanorak.com/databaseConnection.php"; //change for your URL
    public static string hash = "umaDatabaseInterface"; //change your secret code, and remember to change into the PHP file too

    public delegate void MyDelegate(string parameter);

	public static IEnumerator DbRequest(WWWForm form, MyDelegate myFunction)
    {
        string result;
        WWW w = new WWW(URL, form);
        
        yield return w;

        if (!String.IsNullOrEmpty(w.error))// || String.IsNullOrEmpty(w.text))
        {
            result = "Connection error.";
        }
        else
        {
            result = w.text;
        }
        w.Dispose();

		if(myFunction != null)
        	myFunction(result);
    }

	public static void DbRequestNoResponse(WWWForm form)
	{
		new WWW(URL, form);
	}
}
