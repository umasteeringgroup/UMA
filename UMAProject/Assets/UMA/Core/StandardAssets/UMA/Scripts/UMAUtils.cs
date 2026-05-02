using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor.Events;
#endif
using System.Buffers;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	/// <summary>
	/// UMA utility class with various static methods.
	/// </summary>
	public static class UMAUtils
	{
		/// <summary>
		/// Hash value for a string.
		/// </summary>
		/// <returns>Hash value.</returns>
		/// <param name="name">String to hash.</param>
		public static int StringToHash(string name) { return Animator.StringToHash(name); }

		/// <summary>
		/// Gaussian random value.
		/// </summary>
		/// <returns>Random value centered on mean.</returns>
		/// <param name="mean">Mean.</param>
		/// <param name="dev">Deviation.</param>
		static public float GaussianRandom(float mean, float dev)
		{
			float u1 = Random.value;
			float u2 = Random.value;

			float rand_std_normal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);

			return mean + dev * rand_std_normal;
		}

		public enum PipelineType
		{
			Unsupported,
			BuiltInPipeline,
			UniversalPipeline,
			HDPipeline,
			NotSet
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void StaticInitializeOnLoad()
		{
			// This method is called after all assemblies are loaded.
		}

		/// <summary>
		/// Returns the type of renderpipeline that is currently running
		/// </summary>
		/// <returns></returns>
		public static PipelineType DetectPipeline()
		{
			if (GraphicsSettings.currentRenderPipeline != null)
			{
				// SRP
				var srpType = GraphicsSettings.currentRenderPipeline.GetType().ToString();
				if (srpType.Contains("HDRender"))
				{
					return PipelineType.HDPipeline;
				}
				else if (srpType.Contains("Universal"))
				{
					return PipelineType.UniversalPipeline;
				}
				else
				{
					return PipelineType.Unsupported;
				}
			}
			// no SRP
			return PipelineType.BuiltInPipeline;
		}

		public static void UDIMAdjustUV(Vector2[] dest, Vector2[] src)
		{
			if (src == null || dest == null)
			{
				return;
			}
			if (src.Length == 0 || dest.Length == 0)
			{
				return;
			}

			int len = (src.Length <= dest.Length) ? src.Length : dest.Length;
			for (int i = 0; i < len; i++)
			{
				float x = Mathf.Abs(src[i].x);
				float y = Mathf.Abs(src[i].y);

				dest[i].x = x - (int)x;
				dest[i].y = y - (int)y;
			}
		}

		public static Material GetDefaultDiffuseMaterial()
		{
			Shader shader = Shader.Find("UMA/Diffuse");
			if (shader == null)
			{
#if UNITY_EDITOR
				Debug.LogWarning("UMA/Diffuse shader not found");
#endif
				return null;
			}
			Material material = new Material(shader);
			return material;
		}

#if UNITY_EDITOR
		static public int CreateLayer(string name)
		{
			//  https://forum.unity.com/threads/adding-layer-by-script.41970/#post-2274824
			UnityEditor.SerializedObject tagManager = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
			UnityEditor.SerializedProperty layers = tagManager.FindProperty("layers");
			bool ExistLayer = false;

			for (int i = 8; i < layers.arraySize; i++)
			{
				UnityEditor.SerializedProperty layerSP = layers.GetArrayElementAtIndex(i);

				if (layerSP.stringValue == name)
				{
					ExistLayer = true;
					return i;
				}

			}
			for (int j = 8; j < layers.arraySize; j++)
			{
				UnityEditor.SerializedProperty layerSP = layers.GetArrayElementAtIndex(j);
				if (layerSP.stringValue == "" && !ExistLayer)
				{
					layerSP.stringValue = name;
					tagManager.ApplyModifiedProperties();

					return j;
				}
			}

			return 0;
		}

		/// <summary>
		/// Returns the first found asset.
		/// </summary>
		/// <param name="searchName"></param>
		/// <returns></returns>
		public static Texture LoadTextureAsset(string searchName)
		{
			string search = "t:texture " + searchName;
			string[] assets = UnityEditor.AssetDatabase.FindAssets(search);
			if (assets != null && assets.Length > 0)
			{
				return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(UnityEditor.AssetDatabase.GUIDToAssetPath(assets[0]));
			}
			else
			{
				if (Debug.isDebugBuild)
				{
					Debug.LogWarning("Could not load " + searchName);
				}
			}
			return null;
		}
#endif

		/// <summary>
		/// Fast way to get the number of bits set to true. Uses ArrayPool to avoid allocations.
		/// </summary>
		public static int GetCardinality(BitArray bitArray)
		{
			if (bitArray == null)
			{
				return 0;
			}

			int bitCount = bitArray.Count;
			int intsLen = (bitCount + 31) >> 5; // number of 32-bit ints needed
			if (intsLen == 0)
			{
				return 0;
			}

			int[] ints = ArrayPool<int>.Shared.Rent(intsLen);
			try
			{
				// Copy only what we need; rented array can be larger.
				bitArray.CopyTo(ints, 0);

				// Mask off unused high bits in the last int if not multiple of 32
				int remainder = (bitCount & 31);
				if (remainder != 0)
				{
					ints[intsLen - 1] &= ~(-1 << remainder);
				}

				int count = 0;
				for (int i = 0; i < intsLen; i++)
				{
					int c = ints[i];

					unchecked
					{
						c = c - ((c >> 1) & 0x55555555);
						c = (c & 0x33333333) + ((c >> 2) & 0x33333333);
						c = ((c + (c >> 4)) & 0x0F0F0F0F) * 0x01010101;
						c >>= 24;
					}

					count += c;
				}

				return count;
			}
			finally
			{
				ArrayPool<int>.Shared.Return(ints, clearArray: false);
			}
		}

		public static string GetAssetFolder(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return "";
			}
			int index = path.LastIndexOf('/');
			if (index > 0)
			{
				return path.Substring(0, index);
			}
			return "";
		}

		public static void DestroyAvatar(Avatar obj)
		{
			if (obj == null)
			{
				return;
			}

			int DestroyInstance = obj.GetInstanceID();
			if (obj is Avatar && !UMAGeneratorBase.CreatedAvatars.Contains(DestroyInstance))
			{
				return;
			}

			UMAGeneratorBase.CreatedAvatars.Remove(DestroyInstance);

#if UNITY_EDITOR
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(obj);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(obj, false);
			}
#else
			UnityEngine.Object.Destroy(obj);
#endif
		}

		public static void DestroySceneObject(UnityEngine.Object obj)
		{
#if UNITY_EDITOR
			if (obj == null)
			{
				return;
			}

			int DestroyInstance = obj.GetInstanceID();
			if (obj is Avatar && !UMAGeneratorBase.CreatedAvatars.Contains(DestroyInstance))
			{
				return;
			}

			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(obj);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(obj, false);
			}
#else
			UnityEngine.Object.Destroy(obj);
#endif
		}
	}

	// Extension class for System.Collections.Generic.List<T> to get
	// its backing array field via reflection.
	// Author: Jackson Dunstan, http://JacksonDunstan.com/articles/3066
	public static class ListBackingArrayGetter
	{
		// Name of the backing array field
		private const string FieldName = "_items";

		// Flags passed to Type.GetField to get the backing array field
		private const BindingFlags GetFieldFlags = BindingFlags.NonPublic | BindingFlags.Instance;

		// Cached backing array FieldInfo instances per Type
		private static readonly Dictionary<System.Type, FieldInfo> itemsFields = new Dictionary<System.Type, FieldInfo>();

		// Get a List's backing array
		public static TElement[] GetBackingArray<TElement>(this List<TElement> list)
		{
			// Check if the FieldInfo is already in the cache
			var listType = typeof(List<TElement>);
			FieldInfo fieldInfo;
			if (itemsFields.TryGetValue(listType, out fieldInfo) == false)
			{
				// Generate the FieldInfo and add it to the cache
				fieldInfo = listType.GetField(FieldName, GetFieldFlags);
				itemsFields.Add(listType, fieldInfo);
			}

			// Get the backing array of the given List
			var items = (TElement[])fieldInfo.GetValue(list);
			return items;
		}
	}

	// Extension class for System.Collections.Generic.List<T> to set
	// the value of its active size field via reflection.
	public static class ListSizeSetter
	{
		// Name of the size field
		private const string FieldName = "_size";

		// Flags passed to Type.GetField to get the size field
		private const BindingFlags GetFieldFlags = BindingFlags.NonPublic | BindingFlags.Instance;

		// Cached backing array FieldInfo instances per Type
		private static readonly Dictionary<System.Type, FieldInfo> itemsFields = new Dictionary<System.Type, FieldInfo>();

		// Set a List's active size
		public static void SetActiveSize<TElement>(this List<TElement> list, int size)
		{
			// Check if the FieldInfo is already in the cache
			var listType = typeof(List<TElement>);
			FieldInfo fieldInfo;
			if (itemsFields.TryGetValue(listType, out fieldInfo) == false)
			{
				// Generate the FieldInfo and add it to the cache
				fieldInfo = listType.GetField(FieldName, GetFieldFlags);
				itemsFields.Add(listType, fieldInfo);
			}

			// Set the active size of the given List
			int newSize = size;
			if (newSize < 0)
			{
				newSize = 0;
			}

			if (newSize > list.Capacity)
			{
				newSize = list.Capacity;
			}

			fieldInfo.SetValue(list, newSize);
		}
#if UNITY_EDITOR
		public static void CopyUnityEvents(object sourceObj, string source_UnityEvent, object dest, bool debug = false)
		{
			var allBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

			FieldInfo unityEvent = sourceObj.GetType().GetField(source_UnityEvent, allBindings);
			if (unityEvent == null)
			{
				if (debug) Debug.LogWarning($"Field '{source_UnityEvent}' not found on {sourceObj.GetType().Name}");
				return;
			}
			if (unityEvent.FieldType != dest.GetType())
			{
				if (debug)
				{
					Debug.Log("Source Type: " + unityEvent.FieldType);
					Debug.Log("Dest Type: " + dest.GetType());
					Debug.Log("CopyUnityEvents - Source & Dest types don't match, exiting.");
				}
				return;
			}

			SerializedObject so = new SerializedObject((Object)sourceObj);
			SerializedProperty persistentCalls = so.FindProperty(source_UnityEvent).FindPropertyRelative("m_PersistentCalls.m_Calls");
			for (int i = 0; i < persistentCalls.arraySize; ++i)
			{
				Object target = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target").objectReferenceValue;
				string methodName = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_MethodName").stringValue;
				MethodInfo method = null;
				try
				{
					method = target.GetType().GetMethod(methodName, allBindings);
				}
				catch
				{
					MethodInfo[] methods = target.GetType().GetMethods(allBindings);
					for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
					{
						MethodInfo info = methods[methodIndex];
						if (info.Name != methodName)
						{
							continue;
						}
						ParameterInfo[] _params = info.GetParameters();
						if (_params.Length < 2)
						{
							method = info;
							break;
						}
					}
				}

				if (method == null)
				{
					if (debug) Debug.LogWarning($"Method '{methodName}' not found on '{target?.GetType().Name}'. Skipping.");
					continue;
				}

				ParameterInfo[] parameters = method.GetParameters();
				// zero-parameter event
				if (parameters.Length == 0)
				{
					var voidExecute = System.Delegate.CreateDelegate(typeof(UnityAction), target, methodName) as UnityAction;
					if (voidExecute != null && dest is UnityEvent evt)
					{
						UnityEventTools.AddPersistentListener(evt, voidExecute);
					}
					else if (debug)
					{
						Debug.LogWarning($"Destination event type is not UnityEvent for zero-parameter method '{methodName}'.");
					}
					continue;
				}

				switch (parameters[0].ParameterType.Name)
				{
					case nameof(System.Boolean):
						bool bool_value = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue;
						var bool_execute = System.Delegate.CreateDelegate(typeof(UnityAction<bool>), target, methodName) as UnityAction<bool>;
						UnityEventTools.AddBoolPersistentListener(
							dest as UnityEventBase,
							bool_execute,
							bool_value
						);
						break;
					case nameof(System.Int32):
						int int_value = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_IntArgument").intValue;
						var int_execute = System.Delegate.CreateDelegate(typeof(UnityAction<int>), target, methodName) as UnityAction<int>;
						UnityEventTools.AddIntPersistentListener(
							dest as UnityEventBase,
							int_execute,
							int_value
						);
						break;
					case nameof(System.Single):
						float float_value = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_FloatArgument").floatValue;
						var float_execute = System.Delegate.CreateDelegate(typeof(UnityAction<float>), target, methodName) as UnityAction<float>;
						UnityEventTools.AddFloatPersistentListener(
							dest as UnityEventBase,
							float_execute,
							float_value
						);
						break;
					case nameof(System.String):
						string str_value = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
						var str_execute = System.Delegate.CreateDelegate(typeof(UnityAction<string>), target, methodName) as UnityAction<string>;
						UnityEventTools.AddStringPersistentListener(
							dest as UnityEventBase,
							str_execute,
							str_value
						);
						break;
					case nameof(System.Object):
						Object obj_value = persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue;
						var obj_execute = System.Delegate.CreateDelegate(typeof(UnityAction<Object>), target, methodName) as UnityAction<Object>;
						UnityEventTools.AddObjectPersistentListener(
							dest as UnityEventBase,
							obj_execute,
							obj_value
						);
						break;
					default:
						{
							var void_execute = System.Delegate.CreateDelegate(typeof(UnityAction), target, methodName) as UnityAction;
							if (void_execute != null && dest is UnityEvent evt2)
							{
								UnityEventTools.AddPersistentListener(evt2, void_execute);
							}
							else if (debug)
							{
								Debug.LogWarning($"Unable to bind method '{methodName}' with signature '{parameters[0].ParameterType.Name}'.");
							}
						}
						break;
				}
			}
		}
#endif
	}
}
