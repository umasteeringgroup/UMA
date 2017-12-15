using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public class PropertyTypeAttribute : Attribute 
	{
		public readonly Type propertyType;
		public readonly string propertyFieldName;
		public PropertyTypeAttribute(Type type, string fieldName)
		{
			propertyType = type;
			propertyFieldName = fieldName;
		}
	}
}