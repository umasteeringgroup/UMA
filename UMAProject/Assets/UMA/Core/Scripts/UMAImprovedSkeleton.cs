using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Class links UMA's bone data with Unity's transform hierarchy.
	/// </summary>
	public class UMAImprovedSkeleton : UMASkeleton
	{
		/// <summary>
		/// Internal class for storing bone and transform information.
		/// </summary>
		public class BoneDataBoneBaking
		{
			public int boneNameHash;
			public int parentBoneNameHash;
			public Transform boneTransform;
			public UMATransform umaTransform;
			public Quaternion rotation;
			public Vector3 position;
			public Vector3 scale;
			public int accessedFrame;

			public int matrixFrame;
			public Quaternion grotation;
			public Vector3 gposition;
			public Vector3 gscale;
			public Matrix4x4 localToWorld;
			public bool preserved;


			internal void ReadUMATransform()
			{
				rotation = umaTransform.rotation;
				position = umaTransform.position;
				scale = umaTransform.scale;
			}
			internal void RestoreUMATransform()
			{
				rotation = umaTransform.rotation;
				position = umaTransform.position;
				scale = umaTransform.scale;
			}
		}

		/// <value>The bone count.</value>
		public override int boneCount { get { return boneHashData.Count; } }

		new Dictionary<int, BoneDataBoneBaking> boneHashData;
		public override void BeginSkeletonUpdate()
		{
			updating = true;
			frame++;
		}

		public override void EndSkeletonUpdate()
		{
			updating = false;

			// Gather the chain of bone hashes from root up to the external parent.
			// These bones anchor the skeleton in the scene hierarchy and must never
			// be destroyed. Typically this includes "Global" (root) and "Root".
			var protectedBones = new HashSet<int>();
			int currentHash = rootBoneHash;
			while (currentHash != 0 && boneHashData.ContainsKey(currentHash))
			{
				protectedBones.Add(currentHash);
				BoneDataBoneBaking bd;
				if (boneHashData.TryGetValue(currentHash, out bd) && bd.parentBoneNameHash != currentHash)
					currentHash = bd.parentBoneNameHash;
				else
					break;
			}

			foreach (var entry in boneHashData)
			{
				// Never destroy bones that anchor the skeleton to the scene hierarchy
				if (protectedBones.Contains(entry.Value.boneNameHash))
					continue;

				if (!entry.Value.preserved && entry.Value.boneTransform != null)
				{
					if (entry.Value.boneTransform.childCount > 0)
					{
						var parent = FindPreservedParent(entry.Value.parentBoneNameHash);
						for (int i = 0; i < entry.Value.boneTransform.childCount; i++)
						{
							entry.Value.boneTransform.GetChild(i).parent = parent.boneTransform;
						}
					}
					UMAUtils.DestroySceneObject(entry.Value.boneTransform.gameObject);
					entry.Value.boneTransform = null;
				}
			}
		}

		/// <summary>
		/// Initializes a new UMASkeleton from a transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		public UMAImprovedSkeleton(Transform rootBone)
		{
			rootBoneHash = UMAUtils.StringToHash(rootBone.name);
			this.boneHashData = new Dictionary<int, BoneDataBoneBaking>(300);
			AddBonesRecursive(rootBone);
		}

		private void AddBonesRecursive(Transform transform)
		{
			var hash = UMAUtils.StringToHash(transform.name);
			var parentHash = transform.parent != null ? UMAUtils.StringToHash(transform.parent.name) : 0;
			AddBone(parentHash, hash, transform);
			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		/// <summary>
		/// Does this skeleton contains bone with specified name hash?
		/// </summary>
		/// <returns><c>true</c> if this instance has bone the specified name hash; otherwise, <c>false</c>.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override bool HasBone(int nameHash)
		{
			return boneHashData.ContainsKey(nameHash);
		}

		public bool BoneAddedThisUpdate(int nameHash)
		{
			BoneDataBoneBaking boneData;
			if (boneHashData.TryGetValue(nameHash, out boneData))
			{
				return boneData.accessedFrame == frame;
			}
			return false;
		}

		/// <summary>
		/// Check if the bone exists and is valid.
		/// </summary>
		/// <param name="nameHash">the namehash of the bone to check</param>
		/// <returns>true if the bone exists and is valid</returns>
		public override bool BoneExists(int nameHash)
		{
			BoneDataBoneBaking boneData;
			if (boneHashData.TryGetValue(nameHash, out boneData))
			{
				return boneData.preserved;
			}
			return false;
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="parentHash">Hash of parent transform name.</param>
		/// <param name="hash">Hash of transform name.</param>
		/// <param name="transform">Transform.</param>
		public override void AddBone(int parentHash, int hash, Transform transform)
		{
			BoneDataBoneBaking boneData;
			if (boneHashData.TryGetValue(hash, out boneData))
			{
				boneData.parentBoneNameHash = parentHash;
				boneData.accessedFrame = frame;
				boneData.boneTransform = transform;
				boneData.umaTransform.position = transform.localPosition;
				boneData.umaTransform.rotation = transform.localRotation;
				boneData.umaTransform.scale = transform.localScale;
				boneData.ReadUMATransform();
			}
			else
			{
				var umaTransform = new UMATransform(transform, hash, parentHash);
				var newBone = new BoneDataBoneBaking()
				{
					parentBoneNameHash = parentHash,
					boneNameHash = hash,
					accessedFrame = frame,
					boneTransform = transform,
					umaTransform = umaTransform.Duplicate(),
				};
				newBone.ReadUMATransform();
				boneHashData.Add(hash, newBone);
			}
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="umaTransform">Transform.</param>
		public override void AddBone(UMATransform umaTransform)
		{
			BoneDataBoneBaking boneData;
			if (boneHashData.TryGetValue(umaTransform.hash, out boneData))
			{
				boneData.accessedFrame = frame;
				boneData.parentBoneNameHash = umaTransform.parent;
				boneData.umaTransform.position = umaTransform.position;
				boneData.umaTransform.rotation = umaTransform.rotation;
				boneData.umaTransform.scale = umaTransform.scale;
				boneData.ReadUMATransform();
			}
			else
			{
				var newBone = new BoneDataBoneBaking()
				{
					accessedFrame = -1,
					parentBoneNameHash = umaTransform.parent,
					boneNameHash = umaTransform.hash,
					umaTransform = umaTransform.Duplicate(),
				};
				newBone.ReadUMATransform();
				boneHashData.Add(umaTransform.hash, newBone);
			}
		}

		/// <summary>
		/// Removes the bone with the given name hash.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public override void RemoveBone(int nameHash)
		{
			boneHashData.Remove(nameHash);
		}

		/// <summary>
		/// Tries to find bone transform in skeleton.
		/// </summary>
		/// <returns><c>true</c>, if transform was found, <c>false</c> otherwise.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="boneTransform">Bone transform.</param>
		/// <param name="transformDirty">Transform is dirty.</param>
		/// <param name="parentBoneNameHash">Name hash of parent bone.</param>
		public override bool TryGetBoneTransform(int nameHash, out Transform boneTransform, out bool transformDirty, out int parentBoneNameHash)
		{
			BoneDataBoneBaking res;
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				transformDirty = res.accessedFrame != frame;
				res.accessedFrame = frame;
				boneTransform = res.boneTransform;
				parentBoneNameHash = res.parentBoneNameHash;
				return true;
			}
			transformDirty = false;
			boneTransform = null;
			parentBoneNameHash = 0;
			return false;
		}

		/// <summary>
		/// Gets the Transform for a bone using this skeleton's boneHashData dictionary.
		/// Overrides the base class which accesses a separate dictionary.
		/// </summary>
		public override Transform GetBoneTransform(int nameHash)
		{
			BoneDataBoneBaking res;
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				res.accessedFrame = frame;
				return res.boneTransform;
			}
			return null;
		}

		/// <summary>
		/// Gets the game object for a transform in the skeleton.
		/// </summary>
		/// <returns>The game object or null, if not found.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override GameObject GetBoneGameObject(int nameHash)
		{
			BoneDataBoneBaking res;
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				res.accessedFrame = frame;

				return res.boneTransform == null ? null : res.boneTransform.gameObject;
			}
			return null;
		}

		protected override IEnumerable<int> GetBoneHashes()
		{
			foreach (int hash in boneHashData.Keys)
			{
				yield return hash;
			}
		}

		public override void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.position = position;
					db.rotation = rotation;
					db.scale = scale;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localPosition = position;
						db.boneTransform.localRotation = rotation;
						db.boneTransform.localScale = scale;
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to Set on non animated bone: " + db.umaTransform.name);
					}
				}
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Ensures the bone has a Transform GameObject created, properly parented
		/// and positioned within the preserved skeleton hierarchy. Overrides the base
		/// to set up parent linkage via FindPreservedParent, matching what
		/// EnsureBoneHierarchy does for preserved bones.
		/// </summary>
		public override void EnsureBoneTransform(int nameHash)
		{
			BoneDataBoneBaking bd;
			if (!boneHashData.TryGetValue(nameHash, out bd) || bd == null) return;
			if (bd.boneTransform != null) return;

			// Create the Transform
			var go = new GameObject(bd.umaTransform.name);
			bd.boneTransform = go.transform;

			// Ensure the matrix is computed before setting up local transform
			UpdateMatrix(bd);

			if (bd.boneNameHash == rootBoneHash)
			{
				bd.boneTransform.localRotation = bd.rotation;
				bd.boneTransform.localScale = Vector3.one;
				bd.boneTransform.localPosition = bd.position;
			}
			else
			{
				var parent = FindPreservedParent(bd.parentBoneNameHash);
				bd.boneTransform.parent = parent.boneTransform;
				bd.boneTransform.localRotation = Quaternion.Inverse(parent.grotation) * bd.grotation;
				bd.boneTransform.localScale = new Vector3(
					bd.gscale.x / parent.gscale.x,
					bd.gscale.y / parent.gscale.y,
					bd.gscale.z / parent.gscale.z);
				bd.boneTransform.localPosition = parent.localToWorld.inverse.MultiplyPoint(bd.gposition);
			}
		}

		/// <summary>
		/// Returns true if the bone has a Transform GameObject created.
		/// Uses the local boneHashData dictionary (not the base class one).
		/// </summary>
		public override bool HasBoneTransform(int nameHash)
		{
			BoneDataBoneBaking bd;
			if (boneHashData.TryGetValue(nameHash, out bd) && bd != null)
			{
				return bd.boneTransform != null;
			}
			return false;
		}

		/// <summary>
		/// Sets the position of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="position">Position.</param>
		public override void SetPosition(int nameHash, Vector3 position)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.position = position;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localPosition = position;
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetPosition on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

        /// <summary>
        /// Sets the position of a bone relative to it's old position.
        /// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
        /// </summary>
        /// <param name="nameHash">Name hash.</param>
        /// <param name="delta">Position delta.</param>

        public override void SetPositionRelative(int nameHash, Vector3 delta, float weight = 1)
		{
        	BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.position = db.position + delta * weight;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localPosition = db.boneTransform.localPosition + delta * weight;
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetPositionRelative on non animated bone: " + db.umaTransform.name);
					}
				}

			}
		}

		/// <summary>
		/// Sets the scale of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="scale">Scale.</param>
		public override void SetScale(int nameHash, Vector3 scale)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.scale = scale;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localScale = scale;
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetScale on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

		public override void SetScaleRelative(int nameHash, Vector3 scale, float weight = 1f)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					scale.Scale(db.scale);
					db.scale = Vector3.Lerp(db.scale, scale, weight);
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						scale.Scale(db.boneTransform.localScale);
						db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetScaleRelative on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

		/// <summary>
		/// Sets the rotation of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="rotation">Rotation.</param>
		public override void SetRotation(int nameHash, Quaternion rotation)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.rotation = rotation;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localRotation = rotation;
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetRotation on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

		public override void SetRotationRelative(int nameHash, Quaternion rotation, float weight)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					Quaternion fullRotation = db.rotation * rotation;
					db.rotation = Quaternion.Slerp(db.rotation, fullRotation, weight);
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						Quaternion fullRotation = db.boneTransform.localRotation * rotation;
						db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to SetRotationRelative on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}


		public override void Lerp(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.position = Vector3.Lerp(db.position, position, weight);
					db.rotation = Quaternion.Slerp(db.rotation, db.boneTransform.localRotation, weight);
					db.scale = Vector3.Lerp(db.scale, scale, weight);
					db.accessedFrame = frame;
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localPosition = Vector3.Lerp(db.boneTransform.localPosition, position, weight);
						db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, db.boneTransform.localRotation, weight);
						db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to Morph on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

		/// <summary>
		/// Lerp the specified bone toward a new position, rotation, and scale.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="position">Position.</param>
		/// <param name="scale">Scale.</param>
		/// <param name="rotation">Rotation.</param>
		/// <param name="weight">Weight.</param>
		public override void Morph(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (updating)
				{
					db.accessedFrame = frame;
					db.position += position * weight;
					Quaternion fullRotation = db.rotation * rotation;
					db.rotation = Quaternion.Slerp(db.rotation, fullRotation, weight);
					var fullScale = scale;
					fullScale.Scale(db.scale);
					db.scale = Vector3.Lerp(db.scale, fullScale, weight);
					InvalidateMatrix(db);
				}
				else
				{
					if (db.boneTransform != null)
					{
						db.boneTransform.localPosition += position * weight;
						Quaternion fullRotation = db.boneTransform.localRotation * rotation;
						db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
						var fullScale = scale;
						fullScale.Scale(db.boneTransform.localScale);
						db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, fullScale, weight);
						InvalidateMatrix(db);
					}
					else
					{
						throw new Exception("Attempting to Morph on non animated bone: " + db.umaTransform.name);
					}
				}
			}
		}

		/// <summary>
		/// Reset the specified transform to the pre-dna state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public override bool Reset(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.preserved = false;
				db.RestoreUMATransform();
				InvalidateMatrix(db);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Reset all transforms to the pre-dna state.
		/// </summary>
		public override void ResetAll()
		{
			foreach (BoneDataBoneBaking db in boneHashData.Values)
			{
				db.preserved = false;
				db.RestoreUMATransform();
				db.matrixFrame = -1;
			}
			if (updating)
			{
				boneHashData[rootBoneHash].preserved = true;
			}
		}

		/// <summary>
		/// Reset the specified transform to the post-dna state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public override bool Restore(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.boneTransform.localPosition = db.position;
				db.boneTransform.localRotation = db.rotation;
				db.boneTransform.localScale = db.scale;
				InvalidateMatrix(db);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Reset all transforms to the post-dna state.
		/// </summary>
		public override void RestoreAll()
		{
			foreach (BoneDataBoneBaking db in boneHashData.Values)
			{
				if (db.boneTransform != null)
				{
					db.boneTransform.localPosition = db.position;
					db.boneTransform.localRotation = db.rotation;
					db.boneTransform.localScale = db.scale;
				}
			}
		}

		/// <summary>
		/// Gets the position of a bone.
		/// </summary>
		/// <returns>The position.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override Vector3 GetPosition(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return db.position;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the global position of a bone.
		/// </summary>
		/// <returns>The position.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override Vector3 GetRelativePosition(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				UpdateMatrix(db);
				db.accessedFrame = frame;
				return db.gposition;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the scale of a bone.
		/// </summary>
		/// <returns>The scale.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override Vector3 GetScale(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return db.scale;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the rotation of a bone.
		/// </summary>
		/// <returns>The rotation.</returns>
		/// <param name="nameHash">Name hash.</param>
		public override Quaternion GetRotation(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return db.rotation;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		public override Transform[] HashesToTransforms(int[] boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Length];
			for (int i = 0; i < boneNameHashes.Length; i++)
			{
				// Direct lookup first (matching base UMASkeleton behavior).
				// This ensures the SkinnedMeshRenderer receives the same bone Transforms
				// that the default combiner would provide, not preserved ancestors.
				BoneDataBoneBaking bd;
				if (boneHashData.TryGetValue(boneNameHashes[i], out bd) && bd != null && bd.boneTransform != null)
				{
					res[i] = bd.boneTransform;
				}
				else
				{
					// Fall back to preserved ancestor when bone has no Transform of its own
					res[i] = FindPreservedParent(boneNameHashes[i]).boneTransform;
				}
			}
			return res;
		}

		public override Transform[] HashesToTransforms(List<int> boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Count];
			for (int i = 0; i < boneNameHashes.Count; i++)
			{
				// Direct lookup first (matching base UMASkeleton behavior).
				BoneDataBoneBaking bd;
				if (boneHashData.TryGetValue(boneNameHashes[i], out bd) && bd != null && bd.boneTransform != null)
				{
					res[i] = bd.boneTransform;
				}
				else
				{
					// Fall back to preserved ancestor when bone has no Transform of its own
					res[i] = FindPreservedParent(boneNameHashes[i]).boneTransform;
				}
			}
			return res;
		}


		/// <summary>
		/// Ensures the bone exists in the skeleton.
		/// </summary>
		/// <param name="umaTransform">UMA transform.</param>
		public override void EnsureBone(UMATransform umaTransform)
		{
			BoneDataBoneBaking res;
			if (!boneHashData.TryGetValue(umaTransform.hash, out res))
			{
				AddBone(umaTransform);
				res = boneHashData[umaTransform.hash];
			}
			else
			{
				res.umaTransform.Assign(umaTransform);
			}
			res.ReadUMATransform();
		}

		/// <summary>
		/// Ensures all bones are properly initialized and parented.
		/// </summary>
		public override void EnsureBoneHierarchy()
		{
			foreach (var entry in boneHashData.Values)
			{
				if (entry.preserved)
				{
					if (entry.boneTransform == null)
					{
						var go = new GameObject(entry.umaTransform.name);
						entry.boneTransform = go.transform;
					}
				}
			}

			foreach (var entry in boneHashData.Values)
			{
				if (entry.preserved)
				{
					UpdateMatrix(entry);
					if (entry.boneNameHash == rootBoneHash)
					{
						var root = boneHashData[rootBoneHash];
						root.boneTransform.localRotation = root.rotation;
						root.boneTransform.localScale = Vector3.one;
						root.boneTransform.localPosition = root.position;
					}
					else
					{
						var parent = FindPreservedParent(entry.parentBoneNameHash);
						entry.boneTransform.parent = parent.boneTransform;
						entry.boneTransform.localRotation = Quaternion.Inverse(parent.grotation) * entry.grotation;
						entry.boneTransform.localScale = new Vector3(entry.gscale.x / parent.gscale.x, entry.gscale.y / parent.gscale.y, entry.gscale.z / parent.gscale.z);
						entry.boneTransform.localPosition = parent.localToWorld.inverse.MultiplyPoint(entry.gposition);
					}
				}
			}
		}

		public int ResolvePreservedHash(int hash)
		{
			return FindPreservedParent(hash).boneNameHash;
		}

		private BoneDataBoneBaking FindPreservedParent(int parentHash)
		{
			BoneDataBoneBaking boneData;
			if (!boneHashData.TryGetValue(parentHash, out boneData))
			{
				// sometimes the root bone have strange names, if parent bone name is unknown it must be root.
				return boneHashData[rootBoneHash];
			}

			while (!boneData.preserved)
			{
				if (!boneHashData.TryGetValue(boneData.parentBoneNameHash, out boneData))
				{
					// sometimes the root bone have strange names, if parent bone name is unknown it must be root.
					return boneHashData[rootBoneHash];
				}
			}
			return boneData;
		}

		public virtual Matrix4x4 GetLocalToWorldMatrix(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				UpdateMatrix(db);
				return db.localToWorld;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		private void CalculateMatrix(BoneDataBoneBaking db)
		{
			if (db.boneNameHash == rootBoneHash)
			{
				// Read parent rotation from the actual Transform hierarchy instead of
				// hardcoding Euler(-90,0,0). This correctly handles both FixupRotations
				// true (Root at 270,0,0) and false (Root at 0,0,0).
				Quaternion parentRotation = Quaternion.identity;
				if (db.boneTransform != null && db.boneTransform.parent != null)
					parentRotation = db.boneTransform.parent.localRotation;
				db.gposition = parentRotation * db.position;
				db.grotation = parentRotation * db.rotation;
				db.gscale = db.scale;
			}
			else
			{
				var parentdb = boneHashData[db.parentBoneNameHash];
				db.gposition = parentdb.localToWorld.MultiplyPoint(db.position);
				db.gscale = Vector3.Scale(parentdb.gscale, db.scale);
				db.grotation = parentdb.grotation * db.rotation;
			}
			db.localToWorld = Matrix4x4.TRS(db.gposition, db.grotation, db.gscale);
		}

		private void InvalidateMatrix(BoneDataBoneBaking db)
		{
			if (db == null)
			{
				return;
			}

			int changedHash = db.boneNameHash;
			foreach (var bone in boneHashData.Values)
			{
				if (bone.boneNameHash == changedHash || IsDescendantOf(bone, changedHash))
				{
					bone.matrixFrame = -1;
				}
			}
		}

		private bool IsDescendantOf(BoneDataBoneBaking bone, int parentHash)
		{
			int currentHash = bone.parentBoneNameHash;
			while (currentHash != 0 && currentHash != bone.boneNameHash)
			{
				if (currentHash == parentHash)
				{
					return true;
				}

				BoneDataBoneBaking parent;
				if (!boneHashData.TryGetValue(currentHash, out parent))
				{
					return false;
				}
				currentHash = parent.parentBoneNameHash;
			}

			return false;
		}

		public void ForceUpdateMatrices()
		{
			var hashes = new HashSet<int>();
			foreach (var bone in boneHashData.Values)
			{
				ForceUpdateMatrix(bone, hashes);
			}
		}

		private void ForceUpdateMatrix(BoneDataBoneBaking db, HashSet<int> hashes)
		{
			if (hashes.Contains(db.boneNameHash)) return;

			if (db.boneNameHash != rootBoneHash)
				ForceUpdateMatrix(boneHashData[db.parentBoneNameHash], hashes);

			CalculateMatrix(db);
			hashes.Add(db.boneNameHash);
		}

		private void UpdateMatrix(BoneDataBoneBaking db)
		{
			if (db.matrixFrame == frame) return;

			if (db.boneNameHash != rootBoneHash)
				UpdateMatrix(boneHashData[db.parentBoneNameHash]);

			CalculateMatrix(db);
			db.matrixFrame = frame;
		}

		public override void SetAnimatedBone(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.preserved = true;
			}
			else
			{
				Debug.LogError("Bone not found! Hash: " + nameHash);
			}
		}

		public override void SetAnimatedBoneHierachy(int nameHash)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (!db.preserved)
				{
					db.preserved = true;
					SetAnimatedBoneHierachy(db.parentBoneNameHash);
				}
			}
			else
			{
				Debug.LogError("Bone not found! Hash: " + nameHash);
			}
		}

		public override void ClearAnimatedBoneHierachy(int nameHash, bool recursive)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				if (db.preserved)
				{
					db.preserved = false;
					foreach (var entry in boneHashData)
					{
						if (entry.Value.parentBoneNameHash == nameHash)
						{

						}
					}
					if (recursive)
					{
						foreach (var entry in boneHashData)
						{
							if (entry.Value.parentBoneNameHash == nameHash)
							{
								ClearAnimatedBoneHierachy(entry.Key, recursive);
							}
						}
					}
				}
			}
			else
			{
				Debug.LogError("Bone not found! Hash: " + nameHash);
			}
		}

		public override Quaternion GetTPoseCorrectedRotation(int nameHash, Quaternion tPoseRotation)
		{
			BoneDataBoneBaking db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				//bool debug = db.umaTransform.name == "Spine";
				var parentHash = ResolvePreservedHash(db.parentBoneNameHash);
				if (parentHash != db.parentBoneNameHash)
				{
					Quaternion parentOffset = Quaternion.identity;
					BoneDataBoneBaking immediateParent;
					if (boneHashData.TryGetValue(db.parentBoneNameHash, out immediateParent))
					{
						BoneDataBoneBaking parent = boneHashData[parentHash];
						parentOffset = Quaternion.Inverse(parent.grotation) * immediateParent.grotation;
						var normalRotationToTPoseRotation = Quaternion.Inverse(tPoseRotation) * db.rotation;
						var resultingRotation = normalRotationToTPoseRotation * parentOffset;
						return resultingRotation;
					}
				}
				return tPoseRotation;
			}
			else
			{
				Debug.LogError("Bone not found! Hash: " + nameHash);
				return tPoseRotation;
			}
		}

		public void DrawDebug(Color color, float duration)
		{
			ForceUpdateMatrices();
			if (!updating)
			{
				foreach (var bone in boneHashData.Values)
				{
					if (bone.preserved)
					{
						var parent = FindPreservedParent(bone.parentBoneNameHash);
						Debug.DrawLine(parent.boneTransform.position, bone.boneTransform.position, color, duration);
					}
				}
			}
			else
			{
				foreach (var bone in boneHashData.Values)
				{
					BoneDataBoneBaking parent;
					if (boneHashData.TryGetValue(bone.parentBoneNameHash, out parent))
					{
						Debug.DrawLine(parent.gposition, bone.gposition, color, duration);
					}
				}
			}
		}
	}
}
