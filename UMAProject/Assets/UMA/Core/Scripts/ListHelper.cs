using System.Collections.Generic;
using System.Reflection;

namespace UMA
{
	public static class ListHelper<T>
	{
		static FieldInfo _listFieldInfo;
		static FieldInfo _sizeFieldInfo;
		static ListHelper()
		{
			var type = typeof(List<T>);
			_listFieldInfo = type.GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			_sizeFieldInfo = type.GetField("_size", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		}

		public static T[] GetRawList(List<T> list)
		{
			return _listFieldInfo.GetValue(list) as T[];
		}

		public static void SetCount(List<T> list, int size)
		{
			_sizeFieldInfo.SetValue(list, size);
		}

		public static void AllocateList(ref List<T> list, int size)
		{
			if (list == null)
			{
				list = new List<T>(size);
			}
			else if (list.Capacity < size)
			{
				list.Clear();
				list.Capacity = size;
			}
			ListHelper<T>.SetCount(list, size);
		}

		public static void AllocateArray(ref List<T> list, out T[] array, int size)
		{
			if (list == null)
			{
				list = new List<T>(size);
			}
			else if (list.Capacity < size)
			{
				list.Clear();
				list.Capacity = size;
			}
			ListHelper<T>.SetCount(list, size);
			array = ListHelper<T>.GetRawList(list);
		}
	}
}