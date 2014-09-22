using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;

namespace UMAEditor
{
	public sealed class DictionaryCustomFormatter : IFormatProvider, ICustomFormatter
	{
	    #region IFormatProvider Members

	    public object GetFormat(Type formatType)
	    {
	        if (typeof(ICustomFormatter).Equals(formatType)) return this;
	        return null;
	    }

	    #endregion

	    #region ICustomFormatter Members

	    public string Format(string format, object arg, IFormatProvider formatProvider)
	    {
	        if (arg == null) throw new ArgumentNullException("arg");

	        var dict = arg as Dictionary<string, object>;
	        if (format != null && dict != null)
	        {
	            object o;
	            if (dict.TryGetValue(format.Trim(), out o))
	            {
	                return o.ToString();
	            }
	        }

	        if (arg is IFormattable)
	            return ((IFormattable)arg).ToString(format, formatProvider);
	        else return arg.ToString();
	    }
	    #endregion
	}
}