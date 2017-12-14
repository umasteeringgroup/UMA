using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public class UMAGlobal
	{
		private static UMACompoundContext _context = new UMACompoundContext();
		public static UMACompoundContext Context
		{
			get
			{
				return _context;
			}
		}
	}
}
