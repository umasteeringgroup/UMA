using System.Collections;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
	/// <summary>
	/// Inspector for a Mesh Hide Asset: says which slot it hides and how much, and previews that slot
	/// in white with the hidden triangles in red.
	/// </summary>
	[CustomEditor(typeof(MeshHideAsset))]
	public class MeshHideInspector : Editor
	{
		private static readonly Color VisibleColor = new Color(0.92f, 0.92f, 0.92f, 1f);
		private static readonly Color HiddenColor = new Color(0.95f, 0.08f, 0.06f, 1f);
		private static readonly Vector2 DefaultRotation = new Vector2(8f, 180f);   // the front, from slightly above

		// Bone pairs whose direction is "up" on a humanoid rig, tried in order.
		private static readonly string[][] UpBonePairs =
		{
			new[] { "Hips", "Head" }, new[] { "Hips", "Neck" }, new[] { "Hips", "Spine" },
			new[] { "LeftFoot", "Hips" }, new[] { "RightFoot", "Hips" }, new[] { "LeftLeg", "LeftUpLeg" }, new[] { "RightLeg", "RightUpLeg" },
		};

		private PreviewRenderUtility previewUtility;
		private Mesh previewMesh;
		private Material visibleMaterial, hiddenMaterial;
		private Vector2 previewRotation = DefaultRotation;
		private float previewZoom = 1f;
		private string previewKey;
		private string previewProblem;
		private int previewHidden, previewTotal;

		public override void OnInspectorGUI()
		{
			var hide = (MeshHideAsset)target;
			EditorGUILayout.HelpBox("This asset stores which triangles of a slot are hidden. It is not edited here: in a DynamicCharacterAvatar inspector, open Editing tools and use Open Face Editor (Mesh Hide Assets), or drop this asset into the edit drop box there, or use Edit with first character in scene below. The preview below shows the slot in white with the hidden triangles in red.", MessageType.Info);
			if (targets.Length == 1)
			{
				SlotDataAsset slot = hide.asset;
				if (slot == null)
				{
					EditorGUILayout.HelpBox(string.IsNullOrEmpty(hide.AssetSlotName)
						? "No slot is set."
						: $"The slot '{hide.AssetSlotName}' is not in the Global Library, so it cannot be previewed.", MessageType.Warning);
				}
				else
				{
					int total = hide.TriangleCount, hidden = hide.HiddenCount;
					float percent = total > 0 ? 100f * hidden / total : 0f;
					EditorGUILayout.LabelField("Slot", slot.slotName);
					EditorGUILayout.LabelField("Hidden", $"{hidden:N0} of {total:N0} triangles ({percent:0.#}%)");
					if (!hide.IsCompatibleWithSlot(slot, out string reason))
					{
						EditorGUILayout.HelpBox($"This hide does not match the slot's mesh: {reason}.", MessageType.Warning);
					}
				}
			}
			GUILayout.Space(10);
			DrawDefaultInspector();
			if (targets.Length == 1)
			{
				GUILayout.Space(10);
				DrawEditInSceneButton(hide);
			}
		}

		/// <summary>Opens the Face Editor on this hide with the first character in the scene, as dropping it on that character's Editing tools does.</summary>
		private static void DrawEditInSceneButton(MeshHideAsset hide)
		{
			DynamicCharacterAvatar avatar = FindFirstSceneAvatar();
			if (avatar == null)
			{
				EditorGUILayout.HelpBox("To edit this, you need a DynamicCharacterAvatar in your scene.", MessageType.Info);
				return;
			}

			bool built = Application.isPlaying || avatar.editorTimeGeneration;
			using (new EditorGUI.DisabledScope(!built))
			{
				if (GUILayout.Button(new GUIContent("Edit with first character in scene",
					$"Opens the Face Editor on this Mesh Hide Asset with '{avatar.name}', as dropping it on that character's Editing tools does.")))
				{
					avatar.ignoreMeshHideAssets = true;
					avatar.GenerateNow();
					FaceEditorStage.ShowStage(avatar, hide);
					GUIUtility.ExitGUI();
				}
			}
			EditorGUILayout.LabelField(built
				? $"Uses '{avatar.name}'."
				: $"'{avatar.name}' has Editor Time Generation off, so it is not built. Turn it on in its inspector to edit with it.",
				EditorStyles.wordWrappedMiniLabel);
		}

		/// <summary>
		/// The first active DynamicCharacterAvatar in the open scenes, in Hierarchy order. Characters that are not
		/// saved with their scene (other editor tools' temporary previews) are skipped.
		/// </summary>
		public static DynamicCharacterAvatar FindFirstSceneAvatar()
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded)
				{
					continue;
				}

				foreach (GameObject root in scene.GetRootGameObjects())
				{
					foreach (DynamicCharacterAvatar avatar in root.GetComponentsInChildren<DynamicCharacterAvatar>(false))
					{
						if (avatar != null && avatar.isActiveAndEnabled && UMAAssetIndexer.IsSavedWithScene(avatar.gameObject))
						{
							return avatar;
						}
					}
				}
			}

			return null;
		}

		public override bool HasPreviewGUI() => targets.Length == 1 && target is MeshHideAsset;

		public override GUIContent GetPreviewTitle() => new GUIContent("Hidden triangles");

		public override string GetInfoString() =>
			previewProblem ?? (previewTotal > 0 ? $"{previewHidden:N0} of {previewTotal:N0} triangles hidden (red)" : string.Empty);

		public override void OnPreviewSettings()
		{
			if (GUILayout.Button("Front", EditorStyles.toolbarButton)) { previewRotation = DefaultRotation; Repaint(); }
			if (GUILayout.Button("Back", EditorStyles.toolbarButton)) { previewRotation = new Vector2(DefaultRotation.x, 0f); Repaint(); }
			if (GUILayout.Button("Side", EditorStyles.toolbarButton)) { previewRotation = new Vector2(DefaultRotation.x, 90f); Repaint(); }
			if (GUILayout.Button("Reset", EditorStyles.toolbarButton)) { previewRotation = DefaultRotation; previewZoom = 1f; Repaint(); }
		}

		public override void OnPreviewGUI(Rect rect, GUIStyle background) => OnInteractivePreviewGUI(rect, background);

		public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
		{
			if (rect.width < 2 || rect.height < 2)
			{
				return;
			}
			HandlePreviewInput(rect);
			if (Event.current.type != EventType.Repaint)
			{
				return;
			}
			EnsurePreviewMesh();
			if (previewMesh == null)
			{
				EditorGUI.HelpBox(rect, previewProblem ?? "Nothing to preview.", MessageType.Info);
				return;
			}
			EnsurePreviewUtility();

			Bounds bounds = previewMesh.bounds;
			float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
			float aspect = rect.width / rect.height;
			float halfFov = previewUtility.cameraFieldOfView * 0.5f * Mathf.Deg2Rad;
			float distance = radius / Mathf.Sin(halfFov) * previewZoom / Mathf.Min(1f, aspect);
			Camera camera = previewUtility.camera;
			camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
			camera.farClipPlane = distance + radius * 2f;
			camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -distance), Quaternion.identity);
			Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(previewRotation.x, previewRotation.y, 0f)) * Matrix4x4.Translate(-bounds.center);

			previewUtility.BeginPreview(rect, background);
			Texture result;
			try
			{
				previewUtility.DrawMesh(previewMesh, matrix, visibleMaterial, 0);
				if (previewMesh.subMeshCount > 1 && previewMesh.GetIndexCount(1) > 0)
				{
					previewUtility.DrawMesh(previewMesh, matrix, hiddenMaterial, 1);
				}
				previewUtility.Render(true);
			}
			finally
			{
				result = previewUtility.EndPreview();
			}
			GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
		}

		private void HandlePreviewInput(Rect rect)
		{
			int control = GUIUtility.GetControlID("UMAMeshHidePreview".GetHashCode(), FocusType.Passive, rect);
			Event evt = Event.current;
			switch (evt.GetTypeForControl(control))
			{
				case EventType.MouseDown:
					if (evt.button == 0 && rect.Contains(evt.mousePosition)) { GUIUtility.hotControl = control; evt.Use(); }
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == control)
					{
						previewRotation += new Vector2(evt.delta.y, -evt.delta.x) * 0.7f;
						previewRotation.x = Mathf.Clamp(previewRotation.x, -89f, 89f);
						evt.Use();
						Repaint();
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == control) { GUIUtility.hotControl = 0; evt.Use(); }
					break;
				case EventType.ScrollWheel:
					if (rect.Contains(evt.mousePosition))
					{
						previewZoom = Mathf.Clamp(previewZoom * Mathf.Exp(evt.delta.y * 0.05f), 0.15f, 3f);
						evt.Use();
						Repaint();
					}
					break;
			}
		}

		/// <summary>Builds the preview mesh again when the hide, its slot or the slot's mesh changed.</summary>
		private void EnsurePreviewMesh()
		{
			var hide = (MeshHideAsset)target;
			SlotDataAsset slot = hide != null ? hide.asset : null;
			string key = slot == null ? "none" :
				$"{AssetDatabase.GetAssetPath(slot)}|{slot.meshData?.vertices?.Length}|{EditorUtility.GetDirtyCount(hide)}|{hide.HiddenCount}";
			if (key == previewKey && (previewMesh != null || previewProblem != null))
			{
				return;
			}
			previewKey = key;
			DestroyPreviewMesh();
			previewMesh = BuildPreviewMesh(hide, slot, out previewHidden, out previewTotal, out previewProblem);
		}

		/// <summary>
		/// The hide's slot as a mesh standing upright at its rest pose: submesh 0 holds the visible
		/// triangles and submesh 1 the hidden ones. When the hide does not fit the slot, every triangle
		/// is in submesh 0 and <paramref name="problem"/> says why. Returns null when there is no mesh.
		/// </summary>
		public static Mesh BuildPreviewMesh(MeshHideAsset hide, SlotDataAsset slot, out int hiddenTriangles, out int totalTriangles, out string problem)
		{
			hiddenTriangles = 0;
			totalTriangles = 0;
			problem = null;
			if (hide == null)
			{
				problem = "No Mesh Hide Asset.";
				return null;
			}
			if (slot == null)
			{
				problem = string.IsNullOrEmpty(hide.AssetSlotName) ? "No slot is set." : $"The slot '{hide.AssetSlotName}' is not in the Global Library.";
				return null;
			}
			UMAMeshData meshData = slot.meshData;
			if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.submeshes == null)
			{
				problem = $"The slot '{slot.slotName}' has no mesh data.";
				return null;
			}

			GetUprightPositions(slot, out Vector3[] positions, out Vector3[] normals);
			BitArray[] flags = hide.IsCompatibleWithSlot(slot, out string reason) ? hide.triangleFlags : null;
			if (flags == null)
			{
				problem = $"The hide does not match the slot's mesh ({reason}); the slot is shown without hidden triangles.";
			}

			var visible = new System.Collections.Generic.List<int>();
			var hidden = new System.Collections.Generic.List<int>();
			for (int s = 0; s < meshData.submeshes.Length; s++)
			{
				int[] triangles = meshData.submeshes[s] != null ? meshData.submeshes[s].getManagedTriangles(0) : null;
				if (triangles == null)
				{
					continue;
				}
				BitArray submeshFlags = flags != null && s < flags.Length ? flags[s] : null;
				for (int t = 0; t + 2 < triangles.Length; t += 3)
				{
					bool isHidden = submeshFlags != null && t / 3 < submeshFlags.Count && submeshFlags[t / 3];
					var list = isHidden ? hidden : visible;
					list.Add(triangles[t]);
					list.Add(triangles[t + 1]);
					list.Add(triangles[t + 2]);
				}
			}
			hiddenTriangles = hidden.Count / 3;
			totalTriangles = (visible.Count + hidden.Count) / 3;

			var mesh = new Mesh
			{
				name = "Mesh hide preview: " + slot.slotName,
				hideFlags = HideFlags.HideAndDontSave,
				indexFormat = positions.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
			};
			mesh.vertices = positions;
			mesh.subMeshCount = 2;
			mesh.SetTriangles(visible, 0, false);
			mesh.SetTriangles(hidden, 1, false);
			if (normals != null && normals.Length == positions.Length)
			{
				mesh.normals = normals;
			}
			else
			{
				mesh.RecalculateNormals();
			}
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>
		/// The slot skinned to its own rest pose and turned so the skeleton's up points along +Y. UMA 2
		/// and UMA 3 rigs lie along different axes at the rig root; the skeleton says which. Falls back
		/// to the raw vertices when the slot is not skinned.
		/// </summary>
		private static void GetUprightPositions(SlotDataAsset slot, out Vector3[] positions, out Vector3[] normals)
		{
			UMAMeshData meshData = slot.meshData;
			UMARigRestPose rig = null;
			if (meshData.umaBones != null && meshData.umaBones.Length > 0)
			{
				rig = UMARigRestPose.FromSlots(new[] { slot }, meshData.RootBoneName, Matrix4x4.identity);
			}
			if (rig == null || !UMARigBodySurface.TryBakeToAvatar(meshData, rig, out positions, out normals))
			{
				positions = (Vector3[])meshData.vertices.Clone();
				normals = meshData.normals != null && meshData.normals.Length == positions.Length ? (Vector3[])meshData.normals.Clone() : null;
				return;
			}

			Quaternion upright = GetUprightRotation(rig);
			for (int i = 0; i < positions.Length; i++)
			{
				positions[i] = upright * positions[i];
			}
			if (normals != null)
			{
				for (int i = 0; i < normals.Length; i++)
				{
					normals[i] = upright * normals[i];
				}
			}
		}

		/// <summary>
		/// The rotation that turns the rig's skeleton upright (its up along +Y). UMA 2 and UMA 3 rigs lie
		/// along different axes at the rig root; the skeleton says which. Identity when it cannot tell.
		/// </summary>
		internal static Quaternion GetUprightRotation(UMARigRestPose rig)
		{
			if (rig == null)
			{
				return Quaternion.identity;
			}
			foreach (string[] pair in UpBonePairs)
			{
				if (rig.TryGetAvatarMatrix(UMAUtils.StringToHash(pair[0]), out Matrix4x4 from) &&
				    rig.TryGetAvatarMatrix(UMAUtils.StringToHash(pair[1]), out Matrix4x4 to))
				{
					Vector3 up = to.GetColumn(3) - from.GetColumn(3);
					if (up.sqrMagnitude > 1e-6f)
					{
						return Quaternion.FromToRotation(SnapToAxis(up), Vector3.up);
					}
				}
			}
			return Quaternion.identity;
		}

		private static Vector3 SnapToAxis(Vector3 direction)
		{
			Vector3 abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
			if (abs.x >= abs.y && abs.x >= abs.z) return new Vector3(Mathf.Sign(direction.x), 0f, 0f);
			if (abs.y >= abs.z) return new Vector3(0f, Mathf.Sign(direction.y), 0f);
			return new Vector3(0f, 0f, Mathf.Sign(direction.z));
		}

		private void EnsurePreviewUtility()
		{
			if (previewUtility == null)
			{
				previewUtility = new PreviewRenderUtility();
				previewUtility.cameraFieldOfView = 30f;
				previewUtility.camera.clearFlags = CameraClearFlags.Color;
				previewUtility.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
				previewUtility.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);
				previewUtility.lights[0].intensity = 1.4f;
				previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
				previewUtility.lights[1].intensity = 0.8f;
				previewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 0f);
			}
			if (visibleMaterial == null) visibleMaterial = CreatePreviewMaterial(VisibleColor, "Mesh hide preview (visible)");
			if (hiddenMaterial == null) hiddenMaterial = CreatePreviewMaterial(HiddenColor, "Mesh hide preview (hidden)");
		}

		/// <summary>A plain lit material for the active render pipeline, in one color.</summary>
		internal static Material CreatePreviewMaterial(Color color, string name)
		{
			Shader shader = null;
			var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
			if (pipeline != null)
			{
				string pipelineName = pipeline.GetType().Name;
				shader = Shader.Find(pipelineName.Contains("HDRenderPipeline") ? "HDRP/Lit" : "Universal Render Pipeline/Lit");
			}
			if (shader == null) shader = Shader.Find("Standard");
			if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
			var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
			material.color = color;
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
			if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);
			if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);   // the inside of a sleeve is visible too
			return material;
		}

		private void DestroyPreviewMesh()
		{
			if (previewMesh != null) DestroyImmediate(previewMesh);
			previewMesh = null;
		}

		private void OnDisable()
		{
			DestroyPreviewMesh();
			previewUtility?.Cleanup();
			previewUtility = null;
			if (visibleMaterial != null) DestroyImmediate(visibleMaterial);
			if (hiddenMaterial != null) DestroyImmediate(hiddenMaterial);
			visibleMaterial = hiddenMaterial = null;
			previewKey = null;
		}
	}
}
