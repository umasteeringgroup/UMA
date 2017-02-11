using UnityEngine;
using System.Collections;

public class UMAGenericCrowd : UMACrowd 
{
	protected override void SetUMAData()
	{
		umaData.atlasResolutionScale = atlasResolutionScale;
		umaData.OnCharacterUpdated += umaData_OnCharacterUpdated;
	}

	void umaData_OnCharacterUpdated(UMA.UMAData obj)
	{
		var bc = obj.myRenderer.GetComponent<BoxCollider>();
		if (bc != null)
		{
			var mesh = new Mesh();
			obj.myRenderer.BakeMesh(mesh);
			mesh.RecalculateBounds();
			bc.center = mesh.bounds.center;
			bc.size = mesh.bounds.size;
			var posi = obj.myRenderer.transform.parent.localPosition;
			//posi.y = -mesh.bounds.min.y;
			obj.myRenderer.transform.parent.localPosition = posi;
		}
	}

	protected override void GenerateUMAShapes()
	{
		var allDna = umaData.umaRecipe.GetAllDna();
		foreach (var dna in allDna)
		{
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields)
			{
				field.SetValue(dna, Random.Range(0.25f,0.75f));
			}
		}
	}

	void OnGUI()
	{
		if (GUI.Button(new Rect(10, Screen.height-40, 80, 30), "Randomize"))
		{
			for (int i = transform.childCount - 1; i >= 0; i--)
			{
				var child = transform.GetChild(i);
				child.GetComponent<UMADynamicAvatar>().Hide();
				Destroy(child.gameObject);
			}
			generateLotsUMA = true;
		}
		GUI.color = Color.black;
		GUI.Label(new Rect(100, Screen.height - 35, 400, 30), "Right Click to Select and Customize!");
	}
}
