using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[CreateAssetMenu(menuName ="UMA/Location Set")]
	public class UMALocationSet : ScriptableObject 
	{
		public UMALocation[] Locations;
	}
}