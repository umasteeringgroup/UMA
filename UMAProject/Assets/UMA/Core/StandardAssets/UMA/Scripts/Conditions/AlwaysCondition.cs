using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public class AlwaysCondition : BaseCondition 
	{
		public override bool ConditionMet()
		{
			return true;
		}
	}
}