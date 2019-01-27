using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UMA;
using UMA.AssetBundles;

namespace UMA.CharacterSystem.Examples
{
	public class WardrobeCollectionDemoBtn : MonoBehaviour
	{

		public WardrobeCollectionDemoUI thisUI;
		public Button thisBtn;
		public Image thisImage;
		public Text thisBtnTxt;
		public GameObject thisDownloaded;
		public string thisUWCName = "";

		public void Setup(string recipeName, Sprite img, string text, WardrobeCollectionDemoUI ui, bool downloaded = false)
		{
			thisUI = ui;
			thisUWCName = recipeName;
			thisImage.sprite = img;
			thisBtnTxt.text = text;
			thisDownloaded.SetActive(downloaded);
			thisBtn.onClick.AddListener(GetWardrobeCollection);
		}
		public void GetWardrobeCollection()
		{
			thisUI.LoadSelectedCollection(thisUWCName);
		}
	}
}
