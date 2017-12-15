using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	public abstract class BaseCondition : InspectableAsset
	{
		public virtual string ConditionType()
		{
			return GetType().Name.Replace("Condition", "");
		}
		
		public abstract bool ConditionMet();

#if UNITY_EDITOR


		public static BaseCondition CreateCondition(Type propertyType)
		{
			var result = ScriptableObject.CreateInstance(propertyType) as BaseCondition;
			result.name = propertyType.Name;
			return result;
		}

		static readonly Type baseConditionType = typeof(BaseCondition);
		public static Type[] ConditionTypes
		{
			get 
			{
				if (_conditionTypes == null)
				{
					var results = new List<Type>();
					foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						foreach(var type in assembly.GetTypes())
						{
							if (type.IsAbstract) continue;
							if (DerivesFrom(type, baseConditionType))
							{
								results.Add(type);
							}
						}
					}
					_conditionTypes = results.ToArray();
				}
				return _conditionTypes;
			}
		}
		
		static bool DerivesFrom(Type type, Type baseType)
		{
			while (type != null)
			{
				if (type == baseType)
					return true;
				type = type.BaseType;
			}
			return false;
		}
		
		static Type[] _conditionTypes;
		
		#endif
	}
}