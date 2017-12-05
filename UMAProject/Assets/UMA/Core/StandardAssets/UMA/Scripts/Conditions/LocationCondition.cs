using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public class LocationCondition : BaseCondition 
	{
		public UMALocation Location;
		public ConditionRule Condition;
		public enum ConditionRule
		{
			Empty,
			Set,
			Active
		}
		
		public override bool ConditionMet()
		{
			return true;
		}
	}
}