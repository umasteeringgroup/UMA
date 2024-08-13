using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA.Editors
{
	public sealed class DictionaryCustomFormatter : IFormatProvider, ICustomFormatter
	{
		#region IFormatProvider Members

		public object GetFormat(Type formatType)
		{
			if (typeof(ICustomFormatter).Equals(formatType))
            {
                return this;
            }

            return null;
		}

		#endregion

		#region ICustomFormatter Members

		public string Format(string format, object arg, IFormatProvider formatProvider)
		{
			if (arg == null)
            {
                throw new ArgumentNullException("arg");
            }

            var dict = arg as Dictionary<string, object>;
			if (format != null && dict != null)
			{
				object o;
				if (dict.TryGetValue(format.Trim(), out o))
				{
					if (o == null)
					{
						Debug.LogError("data was null: " + format.Trim());
					}
					var template = o as CodeGenTemplate;
					if (template != null)
					{
						return template.sb.ToString();
					}
					return o.ToString();
				}
			}

			if (arg is IFormattable)
            {
                return ((IFormattable)arg).ToString(format, formatProvider);
            }
            else
            {
                return arg.ToString();
            }
        }
		#endregion
	}
}
