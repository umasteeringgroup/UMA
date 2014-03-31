// Copyright (c) 2012-2013 Rotorz Limited. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using UnityEngine;

namespace UMA.ReorderableList {

	/// <summary>
	/// Adaptor allowing reorderable list control to interface with list data.
	/// </summary>
	public interface IReorderableListAdaptor {

		/// <summary>
		/// Gets count of elements in list.
		/// </summary>
		int Count { get; }

		/// <summary>
		/// Determines whether an item can be reordered by dragging mouse.
		/// </summary>
		/// <remarks>
		/// <para>This should be a light-weight method since it will be used to determine
		/// whether grab handle should be included for each item in a reorderable list.</para>
		/// <para>Please note that returning a value of <c>false</c> does not prevent movement
		/// on list item since other draggable items can be moved around it.</para>
		/// </remarks>
		/// <param name="index">Zero-based index for list element.</param>
		/// <returns>
		/// A value of <c>true</c> if item can be dragged; otherwise <c>false</c>.
		/// </returns>
		bool CanDrag(int index);
		/// <summary>
		/// Determines whether an item can be removed from list.
		/// </summary>
		/// <remarks>
		/// <para>This should be a light-weight method since it will be used to determine
		/// whether remove button should be included for each item in list.</para>
		/// <para>This is redundant when <see cref="ReorderableListFlags.HideRemoveButtons"/>
		/// is specified.</para>
		/// </remarks>
		/// <param name="index">Zero-based index for list element.</param>
		/// <returns>
		/// A value of <c>true</c> if item can be removed; otherwise <c>false</c>.
		/// </returns>
		bool CanRemove(int index);

		/// <summary>
		/// Add new element at end of list.
		/// </summary>
		void Add();
		/// <summary>
		/// Insert new element at specified index.
		/// </summary>
		/// <param name="index">Zero-based index for list element.</param>
		void Insert(int index);
		/// <summary>
		/// Duplicate existing element.
		/// </summary>
		/// <param name="index">Zero-based index of list element.</param>
		void Duplicate(int index);
		/// <summary>
		/// Remove element at specified index.
		/// </summary>
		/// <param name="index">Zero-based index of list element.</param>
		void Remove(int index);
		/// <summary>
		/// Move element from source index to destination index.
		/// </summary>
		/// <param name="sourceIndex">Zero-based index of source element.</param>
		/// <param name="destIndex">Zero-based index of destination element.</param>
		void Move(int sourceIndex, int destIndex);
		/// <summary>
		/// Clear all elements from list.
		/// </summary>
		void Clear();

		/// <summary>
		/// Draw interface for list element.
		/// </summary>
		/// <param name="position">Position in GUI.</param>
		/// <param name="index">Zero-based index of array element.</param>
		void DrawItem(Rect position, int index);
		/// <summary>
		/// Gets height of list item in pixels.
		/// </summary>
		/// <param name="index">Zero-based index of array element.</param>
		/// <returns>
		/// Measurement in pixels.
		/// </returns>
		float GetItemHeight(int index);

	}

}