using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA data assets that can be stored in a context and instantiated
	/// by hash or name.
	/// </summary>
	public abstract class UMADataAsset : ScriptableObject
	{
		public abstract string umaName { get; }
		public abstract int umaHash { get; }

		protected UMAContextBase _parentContext;
		public UMAContextBase parentContext
		{
			get { return _parentContext; }
			set { _parentContext = value; }
		}
	}
}