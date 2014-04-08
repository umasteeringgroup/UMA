// Copyright (c) 2012-2013 Rotorz Limited. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using UnityEngine;
using UnityEditor;

using System;

namespace UMA.ReorderableList {

	/// <summary>
	/// Reorderable list adaptor for serialized array property.
	/// </summary>
	/// <remarks>
	/// <para>This adaptor can be subclassed to add special logic to item height calculation.
	/// You may want to implement a custom adaptor class where specialised functionality
	/// is needed.</para>
	/// </remarks>
	public class SerializedPropertyAdaptor : IReorderableListAdaptor {

		private SerializedProperty _arrayProperty;

		/// <summary>
		/// Fixed height of each list item.
		/// </summary>
		/// <remarks>
		/// <para>Non-zero value overrides property drawer height calculation
		/// which is more efficient.</para>
		/// </remarks>
		public float fixedItemHeight;

		/// <summary>
		/// Gets element from list.
		/// </summary>
		/// <param name="index">Zero-based index of element.</param>
		/// <returns>
		/// Serialized property wrapper for array element.
		/// </returns>
		public SerializedProperty this[int index] {
			get { return _arrayProperty.GetArrayElementAtIndex(index); }
		}

		/// <summary>
		/// Gets the underlying serialized array property.
		/// </summary>
		public SerializedProperty arrayProperty {
			get { return _arrayProperty; }
		}

		#region Construction

		/// <summary>
		/// Initializes a new instance of <see cref="SerializedPropertyAdaptor"/>.
		/// </summary>
		/// <param name="arrayProperty">Serialized property for entire array.</param>
		/// <param name="fixedItemHeight">Non-zero height overrides property drawer height calculation.</param>
		public SerializedPropertyAdaptor(SerializedProperty arrayProperty, float fixedItemHeight) {
			if (arrayProperty == null)
				throw new ArgumentNullException("Array property was null.");
			if (!arrayProperty.isArray)
				throw new InvalidOperationException("Specified serialized propery is not an array.");

			this._arrayProperty = arrayProperty;
			this.fixedItemHeight = fixedItemHeight;
		}

		/// <summary>
		/// Initializes a new instance of <see cref="SerializedPropertyAdaptor"/>.
		/// </summary>
		/// <param name="arrayProperty">Serialized property for entire array.</param>
		public SerializedPropertyAdaptor(SerializedProperty arrayProperty) : this(arrayProperty, 0f) {
		}

		#endregion

		#region IReorderableListAdaptor - Implementation

		/// <inheritdoc/>
		public int Count {
			get { return _arrayProperty.arraySize; }
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
			int newIndex = _arrayProperty.arraySize;
			++_arrayProperty.arraySize;
			ResetValue(_arrayProperty.GetArrayElementAtIndex(newIndex));
		}
		/// <inheritdoc/>
		public void Insert(int index) {
			_arrayProperty.InsertArrayElementAtIndex(index);
			ResetValue(_arrayProperty.GetArrayElementAtIndex(index));
		}
		/// <inheritdoc/>
		public void Duplicate(int index) {
			_arrayProperty.InsertArrayElementAtIndex(index);
		}
		/// <inheritdoc/>
		public void Remove(int index) {
			_arrayProperty.DeleteArrayElementAtIndex(index);
		}
		/// <inheritdoc/>
		public void Move(int sourceIndex, int destIndex) {
			if (destIndex > sourceIndex)
				--destIndex;
			_arrayProperty.MoveArrayElement(sourceIndex, destIndex);
		}
		/// <inheritdoc/>
		public void Clear() {
			_arrayProperty.ClearArray();
		}

		/// <inheritdoc/>
		public virtual void DrawItem(Rect position, int index) {
			EditorGUI.PropertyField(position, this[index], GUIContent.none, false);
		}

		/// <inheritdoc/>
		public virtual float GetItemHeight(int index) {
			return fixedItemHeight != 0f
				? fixedItemHeight
				: EditorGUI.GetPropertyHeight(this[index], GUIContent.none, false)
				;
		}

		#endregion

		#region Methods

		/// <summary>
		/// Reset value of array element.
		/// </summary>
		/// <param name="element">Serializd property for array element.</param>
		private void ResetValue(SerializedProperty element) {
			switch (element.type) {
				case "string":
					element.stringValue = "";
					break;
				case "Vector2f":
					element.vector2Value = Vector2.zero;
					break;
				case "Vector3f":
					element.vector3Value = Vector3.zero;
					break;
				case "Rectf":
					element.rectValue = new Rect();
					break;
				case "Quaternionf":
					element.quaternionValue = Quaternion.identity;
					break;
				case "int":
					element.intValue = 0;
					break;
				case "float":
					element.floatValue = 0f;
					break;
				case "UInt8":
					element.boolValue = false;
					break;
				case "ColorRGBA":
					element.colorValue = Color.black;
					break;

				default:
					if (element.type.StartsWith("PPtr"))
						element.objectReferenceValue = null;
					break;
			}
		}

		#endregion

	}

}