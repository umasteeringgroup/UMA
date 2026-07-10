using System.Collections.Generic;
using System.Reflection;

namespace UMA
{
	public static class ListHelper<T>
	{
		private static FieldInfo _listFieldInfo;
		private static FieldInfo _sizeFieldInfo;

		private static FieldInfo ListFieldInfo
		{
			get
			{
				if (_listFieldInfo == null)
				{
					_listFieldInfo = typeof(List<T>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
					if (_listFieldInfo == null)
					{
						throw new System.MissingFieldException(typeof(List<T>).FullName, "_items");
					}
				}

				return _listFieldInfo;
			}
		}

		private static FieldInfo SizeFieldInfo
		{
			get
			{
				if (_sizeFieldInfo == null)
				{
					_sizeFieldInfo = typeof(List<T>).GetField("_size", BindingFlags.Instance | BindingFlags.NonPublic);
					if (_sizeFieldInfo == null)
					{
						throw new System.MissingFieldException(typeof(List<T>).FullName, "_size");
					}
				}

				return _sizeFieldInfo;
			}
		}

		public static T[] GetRawList(List<T> list)
		{
			return ListFieldInfo.GetValue(list) as T[];
		}

		public static void SetCount(List<T> list, int size)
		{
			SizeFieldInfo.SetValue(list, size);
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