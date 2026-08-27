using System;
using System.Collections.Generic;
using UMA.Dynamics;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>Physics components created from UMA ragdoll definitions on a detached rig.</summary>
    public sealed class DismemberedRagdollBuildResult
    {
        public Rigidbody[] rigidbodies = Array.Empty<Rigidbody>();
        public Rigidbody[] rootRigidbodies = Array.Empty<Rigidbody>();
        public Collider[] colliders = Array.Empty<Collider>();
        public CharacterJoint[] joints = Array.Empty<CharacterJoint>();

        public void ApplyImpulse(Vector3 impulse)
        {
            if (impulse.sqrMagnitude <= 0f) return;
            for (int i = 0; i < rootRigidbodies.Length; i++)
            {
                Rigidbody body = rootRigidbodies[i];
                if (body == null) continue;
                body.WakeUp();
                body.AddForce(impulse, ForceMode.Impulse);
            }
        }
    }

    /// <summary>
    /// Builds a partial ragdoll using the same UMAPhysicsElement assets and collider/joint
    /// semantics as UMAPhysicsAvatar. A definition whose configured parent is not included is
    /// treated as a root body, which allows a head or limb subset to simulate independently.
    /// </summary>
    public static class DismemberedRagdollBuilder
    {
        private sealed class RigEntry
        {
            public UMAPhysicsElement definition;
            public Transform bone;
            public Rigidbody rigidbody;
            public int originalLayer;
        }

        public static DismemberedPhysicsMode ResolvePhysicsMode(
            DismemberedPhysicsMode mode, IReadOnlyList<UMAPhysicsElement> definitions)
        {
            if (mode != DismemberedPhysicsMode.Automatic) return mode;
            int count = 0;
            var boneNames = new HashSet<string>(StringComparer.Ordinal);
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    UMAPhysicsElement definition = definitions[i];
                    if (definition != null && boneNames.Add(definition.boneName ?? string.Empty))
                        count++;
                }
            }
            return count <= 1 ? DismemberedPhysicsMode.Rigid
                : DismemberedPhysicsMode.ArticulatedRagdoll;
        }

        public static IReadOnlyList<UMAPhysicsElement> FilterDefinitionsForCutSubtree(
            Transform cutBone, IReadOnlyList<UMAPhysicsElement> definitions)
        {
            if (cutBone == null || definitions == null || definitions.Count == 0)
                return Array.Empty<UMAPhysicsElement>();
            var retainedNames = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<Transform>();
            stack.Push(cutBone);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (!retainedNames.Add(current.name)) continue;
                for (int child = current.childCount - 1; child >= 0; child--)
                    stack.Push(current.GetChild(child));
            }
            var filtered = new List<UMAPhysicsElement>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                UMAPhysicsElement definition = definitions[i];
                if (definition != null && retainedNames.Contains(definition.boneName))
                    filtered.Add(definition);
            }
            return filtered;
        }

        public static bool TryBuildRigid(Transform rigRoot,
            IReadOnlyList<UMAPhysicsElement> definitions, int physicsLayer,
            out DismemberedRagdollBuildResult result, out string error)
        {
            result = null;
            if (!TryCollectEntries(rigRoot, definitions, out List<RigEntry> entries,
                out _, out error)) return false;

            physicsLayer = Mathf.Clamp(physicsLayer, 0, 31);
            int originalRootLayer = rigRoot.gameObject.layer;
            var createdComponents = new List<Component>();
            var colliders = new List<Collider>();
            try
            {
                if (rigRoot.GetComponent<Rigidbody>() != null)
                    throw new InvalidOperationException($"Detached root '{rigRoot.name}' already " +
                        "has a Rigidbody.");
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].bone.GetComponent<Rigidbody>() != null)
                        throw new InvalidOperationException($"Detached bone " +
                            $"'{entries[i].bone.name}' already has a Rigidbody.");
                }

                rigRoot.gameObject.layer = physicsLayer;
                Rigidbody body = rigRoot.gameObject.AddComponent<Rigidbody>();
                createdComponents.Add(body);
                float totalMass = 0f;
                for (int i = 0; i < entries.Count; i++)
                    totalMass += Mathf.Max(0.001f, entries[i].definition.mass);
                body.mass = Mathf.Max(0.001f, totalMass);
                body.isKinematic = true;

                for (int i = 0; i < entries.Count; i++)
                {
                    RigEntry entry = entries[i];
                    entry.bone.gameObject.layer = physicsLayer;
                    AddColliders(entry, colliders, createdComponents);
                }

                body.detectCollisions = true;
                body.useGravity = true;
                body.isKinematic = false;
                result = new DismemberedRagdollBuildResult
                {
                    rigidbodies = new[] { body },
                    rootRigidbodies = new[] { body },
                    colliders = colliders.ToArray(),
                    joints = Array.Empty<CharacterJoint>()
                };
                return true;
            }
            catch (Exception exception)
            {
                for (int i = createdComponents.Count - 1; i >= 0; i--)
                    DestroyComponent(createdComponents[i]);
                rigRoot.gameObject.layer = originalRootLayer;
                for (int i = 0; i < entries.Count; i++)
                    entries[i].bone.gameObject.layer = entries[i].originalLayer;
                error = $"Could not build rigid detached physics: {exception.Message}";
                return false;
            }
        }

        public static bool TryBuild(Transform rigRoot,
            IReadOnlyList<UMAPhysicsElement> definitions, int ragdollLayer,
            out DismemberedRagdollBuildResult result, out string error)
        {
            result = null;
            if (!TryCollectEntries(rigRoot, definitions, out List<RigEntry> entries,
                out Dictionary<string, RigEntry> entriesByBone, out error)) return false;
            if (!ValidateJointGraph(entries, entriesByBone, out error)) return false;

            ragdollLayer = Mathf.Clamp(ragdollLayer, 0, 31);
            var createdComponents = new List<Component>();
            var colliders = new List<Collider>();
            var joints = new List<CharacterJoint>();
            var roots = new List<Rigidbody>();
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    RigEntry entry = entries[i];
                    if (entry.bone.GetComponent<Rigidbody>() != null)
                        throw new InvalidOperationException($"Detached bone '{entry.bone.name}' " +
                            "already has a Rigidbody.");
                    entry.bone.gameObject.layer = ragdollLayer;
                    entry.rigidbody = entry.bone.gameObject.AddComponent<Rigidbody>();
                    createdComponents.Add(entry.rigidbody);
                    entry.rigidbody.mass = Mathf.Max(0.001f, entry.definition.mass);
                    entry.rigidbody.isKinematic = true;
                    AddColliders(entry, colliders, createdComponents);
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    RigEntry entry = entries[i];
                    RigEntry parent = null;
                    bool hasParent = !entry.definition.isRoot &&
                        !string.IsNullOrWhiteSpace(entry.definition.parentBone) &&
                        entriesByBone.TryGetValue(entry.definition.parentBone,
                            out parent);
                    if (!hasParent)
                    {
                        roots.Add(entry.rigidbody);
                        continue;
                    }

                    CharacterJoint joint = entry.bone.gameObject.AddComponent<CharacterJoint>();
                    createdComponents.Add(joint);
                    joints.Add(joint);
                    ConfigureJoint(joint, entry.definition, parent.rigidbody);
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].rigidbody.isKinematic = false;
                    entries[i].rigidbody.detectCollisions = true;
                    entries[i].rigidbody.useGravity = true;
                }

                var bodies = new Rigidbody[entries.Count];
                for (int i = 0; i < entries.Count; i++) bodies[i] = entries[i].rigidbody;
                result = new DismemberedRagdollBuildResult
                {
                    rigidbodies = bodies,
                    rootRigidbodies = roots.ToArray(),
                    colliders = colliders.ToArray(),
                    joints = joints.ToArray()
                };
                return true;
            }
            catch (Exception exception)
            {
                for (int i = createdComponents.Count - 1; i >= 0; i--)
                    DestroyComponent(createdComponents[i]);
                for (int i = 0; i < entries.Count; i++)
                    entries[i].bone.gameObject.layer = entries[i].originalLayer;
                error = $"Could not build the detached ragdoll: {exception.Message}";
                return false;
            }
        }

        private static bool TryCollectEntries(Transform rigRoot,
            IReadOnlyList<UMAPhysicsElement> definitions, out List<RigEntry> entries,
            out Dictionary<string, RigEntry> entriesByBone, out string error)
        {
            entries = null;
            entriesByBone = null;
            error = string.Empty;
            if (rigRoot == null)
            {
                error = "The detached rig root is null.";
                return false;
            }
            if (definitions == null || definitions.Count == 0)
            {
                error = "No UMA physics definitions were assigned for this cut.";
                return false;
            }

            Dictionary<string, Transform> bones = CollectBones(rigRoot);
            entries = new List<RigEntry>(definitions.Count);
            entriesByBone = new Dictionary<string, RigEntry>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                UMAPhysicsElement definition = definitions[i];
                if (definition == null) continue;
                if (string.IsNullOrWhiteSpace(definition.boneName))
                {
                    error = $"Physics definition '{definition.name}' has no bone name.";
                    return false;
                }
                if (entriesByBone.TryGetValue(definition.boneName, out RigEntry existing))
                {
                    if (existing.definition == definition) continue;
                    error = $"Physics definitions '{existing.definition.name}' and " +
                        $"'{definition.name}' both target bone '{definition.boneName}'.";
                    return false;
                }
                if (!bones.TryGetValue(definition.boneName, out Transform bone))
                {
                    error = $"Physics definition '{definition.name}' targets missing detached " +
                        $"bone '{definition.boneName}'.";
                    return false;
                }
                if (!ValidateColliders(definition, out error)) return false;
                var entry = new RigEntry
                {
                    definition = definition,
                    bone = bone,
                    originalLayer = bone.gameObject.layer
                };
                entries.Add(entry);
                entriesByBone.Add(definition.boneName, entry);
            }
            if (entries.Count != 0) return true;
            error = "Every assigned UMA physics definition is null.";
            return false;
        }

        private static Dictionary<string, Transform> CollectBones(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (!result.ContainsKey(current.name)) result.Add(current.name, current);
                for (int child = current.childCount - 1; child >= 0; child--)
                    stack.Push(current.GetChild(child));
            }
            return result;
        }

        private static bool ValidateColliders(UMAPhysicsElement definition, out string error)
        {
            error = string.Empty;
            ColliderDefinition[] definitions = definition.colliders;
            if (definitions == null) return true;
            for (int i = 0; i < definitions.Length; i++)
            {
                ColliderDefinition collider = definitions[i];
                if (collider == null)
                {
                    error = $"Physics definition '{definition.name}' contains a null collider.";
                    return false;
                }
                switch (collider.colliderType)
                {
                    case ColliderDefinition.ColliderType.Box:
                    case ColliderDefinition.ColliderType.Sphere:
                    case ColliderDefinition.ColliderType.Capsule:
                        break;
                    default:
                        error = $"Physics definition '{definition.name}' contains an unknown " +
                            "collider type.";
                        return false;
                }
            }
            return true;
        }

        private static bool ValidateJointGraph(List<RigEntry> entries,
            Dictionary<string, RigEntry> entriesByBone, out string error)
        {
            error = string.Empty;
            for (int i = 0; i < entries.Count; i++)
            {
                RigEntry current = entries[i];
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (!current.definition.isRoot &&
                    !string.IsNullOrWhiteSpace(current.definition.parentBone) &&
                    entriesByBone.TryGetValue(current.definition.parentBone, out RigEntry parent))
                {
                    if (!visited.Add(current.definition.boneName))
                    {
                        error = $"Physics definitions contain a joint cycle at " +
                            $"'{current.definition.boneName}'.";
                        return false;
                    }
                    current = parent;
                }
            }
            return true;
        }

        private static void AddColliders(RigEntry entry, List<Collider> colliders,
            List<Component> createdComponents)
        {
            ColliderDefinition[] definitions = entry.definition.colliders;
            if (definitions == null) return;
            for (int i = 0; i < definitions.Length; i++)
            {
                ColliderDefinition definition = definitions[i];
                Collider collider;
                switch (definition.colliderType)
                {
                    case ColliderDefinition.ColliderType.Box:
                        var box = entry.bone.gameObject.AddComponent<BoxCollider>();
                        box.center = definition.colliderCentre;
                        box.size = definition.boxDimensions;
                        collider = box;
                        break;
                    case ColliderDefinition.ColliderType.Sphere:
                        var sphere = entry.bone.gameObject.AddComponent<SphereCollider>();
                        sphere.center = definition.colliderCentre;
                        sphere.radius = definition.sphereRadius;
                        collider = sphere;
                        break;
                    case ColliderDefinition.ColliderType.Capsule:
                        var capsule = entry.bone.gameObject.AddComponent<CapsuleCollider>();
                        capsule.center = definition.colliderCentre;
                        capsule.radius = definition.capsuleRadius;
                        capsule.height = definition.capsuleHeight;
                        capsule.direction = (int)definition.capsuleAlignment;
                        collider = capsule;
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported collider type.");
                }
                collider.isTrigger = false;
                colliders.Add(collider);
                createdComponents.Add(collider);
            }
        }

        private static void ConfigureJoint(CharacterJoint joint, UMAPhysicsElement definition,
            Rigidbody parent)
        {
            joint.connectedBody = parent;
            joint.axis = definition.axis;
            joint.swingAxis = definition.swingAxis;
            joint.lowTwistLimit = CreateLimit(definition.lowTwistLimit);
            joint.highTwistLimit = CreateLimit(definition.highTwistLimit);
            joint.swing1Limit = CreateLimit(definition.swing1Limit);
            joint.swing2Limit = CreateLimit(definition.swing2Limit);
            joint.enablePreprocessing = definition.enablePreprocessing;
        }

        private static SoftJointLimit CreateLimit(float value)
        {
            return new SoftJointLimit { limit = value };
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(component);
            else UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
