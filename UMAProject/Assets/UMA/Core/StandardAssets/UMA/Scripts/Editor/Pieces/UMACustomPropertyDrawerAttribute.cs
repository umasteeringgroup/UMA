using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace UMA
{
	public class UMACustomPropertyDrawerAttribute : Attribute 
	{
		public Type type;
		
		public UMACustomPropertyDrawerAttribute(Type type)
		{
			this.type = type;
		}
	}
}