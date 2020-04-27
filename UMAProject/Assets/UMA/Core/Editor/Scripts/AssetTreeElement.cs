using System;
using UnityEngine;


namespace UMA.Controls
{
	public enum Amount
	{
		NotSet = -1,
		None,
		All,
		Mixed
	}

	[Serializable]
	internal class AssetTreeElement : TreeElement
	{

		// parent item
		public Amount AmountChecked;
		public System.Type type;
		public int index;
		public int IsResourceCount;
		public int IsAddrCount;
		public int Keepcount;


		// detail item
		public AssetItem ai;
		public bool Checked;
	
		public void SetAmountChecked(Amount val)
		{
			AmountChecked = val;
		}
		public void SetChecked(bool val)
		{
			Checked = val;
		}

		public AssetTreeElement (string name, int depth, int id) : base (name, depth, id)
		{
			AmountChecked = Amount.None;
			Checked = false;
		}
	}
}
