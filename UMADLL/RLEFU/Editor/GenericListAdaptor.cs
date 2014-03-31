// Copyright (c) 2012-2013 Rotorz Limited. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using UnityEngine;

using System.Collections.Generic;

namespace UMA.ReorderableList
{

	/// <summary>
	/// Reorderable list adaptor for generic list.
	/// </summary>
	/// <remarks>
	/// <para>This adaptor can be subclassed to add special logic to item height calculation.
	/// You may want to implement a custom adaptor class where specialised functionality
	/// is needed.</para>
	/// </remarks>
	/// <typeparam name="T">Type of list element.</typeparam>
	public class GenericListAdaptor<T> : IReorderableListAdaptor {

		private IList<T> _list;

		private ReorderableListControl.ItemDrawer<T> _itemDrawer;

		/// <summary>
		/// Fixed height of each list item.
		/// </summary>
		public float fixedItemHeight;

		/// <summary>
		/// Gets the underlying list data structure.
		/// </summary>
		public IList<T> List {
			get { return _list; }
		}

		/// <summary>
		/// Gets element from list.
		/// </summary>
		/// <param name="index">Zero-based index of element.</param>
		/// <returns>
		/// The element.
		/// </returns>
		public T this[int index] {
			get { return _list[index]; }
		}

		#region Construction

		/// <summary>
		/// Initializes a new instance of <see cref="GenericListAdaptor{T}"/>.
		/// </summary>
		/// <param name="list">The list which can be reordered.</param>
		/// <param name="itemDrawer">Callback to draw list item.</param>
		/// <param name="itemHeight">Height of list item in pixels.</param>
		public GenericListAdaptor(IList<T> list, ReorderableListControl.ItemDrawer<T> itemDrawer, float itemHeight) {
			this._list = list;
			this._itemDrawer = itemDrawer ?? ReorderableListGUI.DefaultItemDrawer;
			this.fixedItemHeight = itemHeight;
		}

		#endregion

		#region IReorderableListAdaptor - Implementation

		/// <inheritdoc/>
		public int Count {
			get { return _list.Count; }
		}

		/// <inheritdoc/>
		public virtual bool CanDrag(int index) {
			return true;
		}
		/// <inheritdoc/>
		public virtual bool CanRemove(int index) {
			return true;
		}

		/// <inheritdoc/>
		public void Add() {
			_list.Add(default(T));
		}
		/// <inheritdoc/>
		public void Insert(int index) {
			_list.Insert(index, default(T));
		}
		/// <inheritdoc/>
		public void Duplicate(int index) {
			_list.Insert(index + 1, _list[index]);
		}
		/// <inheritdoc/>
		public void Remove(int index) {
			_list.RemoveAt(index);
		}
		/// <inheritdoc/>
		public void Move(int sourceIndex, int destIndex) {
			if (destIndex > sourceIndex)
				--destIndex;

			T item = _list[sourceIndex];
			_list.RemoveAt(sourceIndex);
			_list.Insert(destIndex, item);
		}
		/// <inheritdoc/>
		public void Clear() {
			_list.Clear();
		}

		/// <inheritdoc/>
		public virtual void DrawItem(Rect position, int index) {
			_list[index] = _itemDrawer(position, _list[index]);
		}

		/// <inheritdoc/>
		public virtual float GetItemHeight(int index) {
			return fixedItemHeight;
		}

		#endregion

	}

}