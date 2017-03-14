//  Copyright (c) 2016, Ben Hopkins (kode80)
//  All rights reserved.
//  
//  Redistribution and use in source and binary forms, with or without modification, 
//  are permitted provided that the following conditions are met:
//  
//  1. Redistributions of source code must retain the above copyright notice, 
//     this list of conditions and the following disclaimer.
//  
//  2. Redistributions in binary form must reproduce the above copyright notice, 
//     this list of conditions and the following disclaimer in the documentation 
//     and/or other materials provided with the distribution.
//  
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY 
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
//  MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL 
//  THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT 
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) 
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT 
//  (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, 
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using UnityEngine;
using System.Collections;

namespace kode80.GUIWrapper
{
	public class GUIBase 
	{
		public delegate void OnGUIPreAction( GUIBase sender);
		public OnGUIPreAction onGUIPreAction;

		public delegate void OnGUIAction( GUIBase sender);
		public OnGUIAction onGUIAction;

		public bool isHidden;
		public bool isEnabled;
		public bool shouldStoreLastRect;
		public int tag;
		public string controlName;

		private Rect _lastRect;
		public Rect lastRect { get { return _lastRect; } }

		public GUIBase()
		{
			isEnabled = true;
		}

		public void OnGUI()
		{
			if( isHidden == false)
			{
				bool oldGUIEnabled = GUI.enabled;
				GUI.enabled = isEnabled;
				if( controlName != null && controlName.Length > 0)
				{
					GUI.SetNextControlName( controlName);
				}
				CustomOnGUI();
				GUI.enabled = oldGUIEnabled;

				if( shouldStoreLastRect && Event.current.type == EventType.Repaint)
				{
					_lastRect = GUILayoutUtility.GetLastRect();
				}
			}
		}

		protected virtual void CustomOnGUI()
		{
			// Subclasses override this to implement OnGUI
		}

		protected void CallGUIPreAction()
		{
			if( onGUIPreAction != null)
			{
				onGUIPreAction( this);
			}
		}

		protected void CallGUIAction()
		{
			if( onGUIAction != null)
			{
				onGUIAction( this);
			}
		}
	}
}