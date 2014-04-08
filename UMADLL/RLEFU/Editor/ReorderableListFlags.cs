// Copyright (c) 2012-2013 Rotorz Limited. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;

namespace UMA.ReorderableList {

	/// <summary>
	/// Additional flags which can be passed into reorderable list field.
	/// </summary>
	/// <example>
	/// <para>Multiple flags can be specified if desired:</para>
	/// <code language="csharp"><![CDATA[
	/// var flags = ReorderableListFlags.HideAddButton | ReorderableListFlags.HideRemoveButtons;
	/// ReorderableListGUI.ListField(list, flags);
	/// ]]></code>
	/// </example>
	[Flags]
	public enum ReorderableListFlags {
		/// <summary>
		/// Hide grab handles and disable reordering of list items.
		/// </summary>
		DisableReordering = 0x01,
		/// <summary>
		/// Hide add button at base of control.
		/// </summary>
		HideAddButton = 0x02,
		/// <summary>
		/// Hide remove buttons from list items.
		/// </summary>
		HideRemoveButtons = 0x04,
		/// <summary>
		/// Do not display context menu upon right-clicking grab handle.
		/// </summary>
		DisableContextMenu = 0x08,
		/// <summary>
		/// Hide "Duplicate" option from context menu.
		/// </summary>
		DisableDuplicateCommand = 0x10,
		/// <summary>
		/// Do not automatically focus first control of newly added items.
		/// </summary>
		DisableAutoFocus = 0x20,
		/// <summary>
		/// Show zero-based index of array elements.
		/// </summary>
		ShowIndices = 0x40,
		/// <summary>
		/// Do not attempt to clip items which are out of view.
		/// </summary>
		/// <remarks>
		/// <para>Clipping helps to boost performance, though may lead to issues on
		/// some interfaces.</para>
		/// </remarks>
		DisableClipping = 0x80,
	}

}