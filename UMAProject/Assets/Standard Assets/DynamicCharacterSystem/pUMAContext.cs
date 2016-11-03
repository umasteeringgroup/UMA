using System;
using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;

/// <summary>
/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
/// </summary>
public partial class UMAContext : MonoBehaviour
{
	/// <summary>
	/// The DynamicCharacterSystem
	/// </summary>
	public UMACharacterSystem.DynamicCharacterSystemBase dynamicCharacterSystem;
}
