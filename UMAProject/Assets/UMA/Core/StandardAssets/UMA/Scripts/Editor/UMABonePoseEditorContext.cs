using UnityEngine;
#if UNITY_EDITOR
#endif
using System;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    public class UMABonePoseEditorContext
    {
        private UMAData umaData = null;
		private Transform activeTrans = null;
		private MirrorData activeMirror = new MirrorData();

		private Dictionary<Transform, MirrorData> mirrors = new Dictionary<Transform, MirrorData>();

		public UMABonePose startingPose;
		public float startingPoseWeight = 1.0f;

		public string[] boneList = null;

		public Transform activeTransform
		{
			get { return activeTrans; }
			set
			{
				if (activeTrans != value)
				{
					activeTransChanged = true;
					activeTrans = value;
					activeMirror = new MirrorData();
					if (activeTrans != null)
					{
						mirrors.TryGetValue(activeTrans, out activeMirror);
					}
				}
			}
		}

		public MirrorPlane mirrorPlane
		{
			get { return activeMirror.plane; }
		}

		public EditorTool activeTool = EditorTool.Tool_Position;

		public Transform mirrorTransform
		{
			get 
			{
				if (activeMirror.plane == MirrorPlane.Mirror_None)
				{
					return null;
				}

				if (activeMirror.transformA == activeTrans)
				{
					return activeMirror.transformB;
				}
				else
				{
					return activeMirror.transformA;
				}
			}
		}

		public bool activeTransChanged = false;

		public UMAData activeUMA
		{
			get { return umaData; }
			set
			{
				if (umaData != value)
				{
					umaData = value;
					UpdateMirrors();
					boneList = umaData.skeleton.BoneNames;
					Array.Sort(boneList);
				}
			}
		}

		public enum MirrorPlane
		{
			Mirror_None = 0,
			Mirror_Any,
			Mirror_X,
			Mirror_Y,
			Mirror_Z,		
		}

		public enum EditorTool
		{
			Tool_None = 0,
			Tool_Position,
			Tool_Rotation,
			Tool_Scale
		}

		public struct MirrorData
		{
			public Transform transformA;
			public Transform transformB;
			public MirrorPlane plane;
		}

		private void UpdateMirrors()
		{
			mirrors.Clear();

			if ((umaData != null) && (umaData.umaRoot != null))
			{
				FindMirroredTransforms(umaData.umaRoot.transform, mirrors);
			}
		}

		private static void FindMirroredTransforms(Transform transform, Dictionary<Transform, MirrorData> mirrors)
		{
			if (transform == null) return;

			MirrorData mirror;
			// Current transform has a mirror, check children
			if (mirrors.TryGetValue(transform, out mirror))
			{
				// But only add each pair of mirrors once
				if (transform == mirror.transformA)
				{
					for (int i = 0; i < transform.childCount; i++)
					{
						Transform childA = transform.GetChild(i);
						Vector3 childApos = childA.localPosition;

						// If the bone is too close to the local origin check for a name match
						if (Vector3.Distance(childApos, Vector3.zero) < 0.05f)
						{
							int longestMatch = 0;
							int bestIndex = -1;

							for (int j = 0; j < mirror.transformB.childCount; j++)
							{
								Transform childB = mirror.transformB.GetChild(j);
								Vector3 childBpos = childB.localPosition;
								if (childBpos != Vector3.zero) continue;
								if (childB.name == childA.name)
								{
									longestMatch = childA.name.Length;
									bestIndex = j;
									break;
								}

								while ((childA.name.Length > longestMatch) &&
									(childB.name.StartsWith(childA.name.Substring(0, longestMatch + 1))))
								{
									longestMatch++;
									bestIndex = j;
								}
								while ((childA.name.Length > longestMatch) &&
									(childB.name.EndsWith(childA.name.Substring(childA.name.Length - (longestMatch + 1)))))
								{
									longestMatch++;
									bestIndex = j;
								}
							}

							// If we found a good enough match then consider it a mirror
							if (longestMatch > 5)
							{
								Transform childB = mirror.transformB.GetChild(bestIndex);
								MirrorData newMirror = new MirrorData();
								newMirror.plane = mirror.plane;
								newMirror.transformA = childA;
								newMirror.transformB = childB;
								mirrors.Add(childA, newMirror);
								mirrors.Add(childB, newMirror);

	//							Debug.Log("Found new named mirror: " + childA.name + " : " + childB.name);
							}

						}
						// Else check for a position match
						else
						{
							for (int j = 0; j < mirror.transformB.childCount; j++)
							{
								Transform childB = mirror.transformB.GetChild(j);
								Vector3 childBpos = childB.localPosition;

								switch (mirror.plane)
								{
									case MirrorPlane.Mirror_X:
										childBpos.x = -childBpos.x;
										break;
									case MirrorPlane.Mirror_Y:
										childBpos.y = -childBpos.y;
										break;
									case MirrorPlane.Mirror_Z:
										childBpos.z = -childBpos.z;
										break;
								}

								if (childApos == childBpos)
								{
									MirrorData newMirror = new MirrorData();
									newMirror.plane = mirror.plane;
									newMirror.transformA = childA;
									newMirror.transformB = childB;
									mirrors.Add(childA, newMirror);
									mirrors.Add(childB, newMirror);

	//								Debug.Log("Found new child mirror: " + childA.name + " : " + childB.name);
								}
							}
						}
					}
				}
			}
			// Current transform has no mirror, see if children are mirrored
			else
			{
				for (int i = 0; i < transform.childCount; i++)
				{
					Transform childA = transform.GetChild(i);
					Vector3 childApos = childA.localPosition;
					if (childApos == Vector3.zero) continue;

					for (int j = i + 1; j < transform.childCount; j++)
					{
						Transform childB = transform.GetChild(j);
						Vector3 childBpos = childB.localPosition;
						MirrorPlane plane = MirrorPlane.Mirror_None;

						if (!Mathf.Approximately(childApos.x, 0f) && 
							(childApos == new Vector3(-childBpos.x, childBpos.y, childBpos.z)))
						{
							plane = MirrorPlane.Mirror_X;
						}
						if (!Mathf.Approximately(childApos.y, 0f) && 
							(childApos == new Vector3(childBpos.x, -childBpos.y, childBpos.z)))
						{
							plane = MirrorPlane.Mirror_Y;
						}
						if (!Mathf.Approximately(childApos.z, 0f) && 
							(childApos == new Vector3(childBpos.x, childBpos.y, -childBpos.z)))
						{
							plane = MirrorPlane.Mirror_Z;
						}

						if (plane != MirrorPlane.Mirror_None)
						{
							MirrorData newMirror = new MirrorData();
							newMirror.plane = plane;
							newMirror.transformA = childA;
							newMirror.transformB = childB;
							mirrors.Add(childA, newMirror);
							mirrors.Add(childB, newMirror);

//							Debug.Log("Found new branching mirror: " + childA.name + " : " + childB.name);
						}
					}
				}
			}

			// Recursively look at the children
			for (int i = 0; i < transform.childCount; i++)
			{
				FindMirroredTransforms(transform.GetChild(i), mirrors);
			}
		}

	}

}
