# Rig lifetime and bone cleanup

## Enable and measure

On **UMAGenerator → Rig lifetime**, enable **Cleanup Unused Bones**. It defaults to off. Generator overrides and Generator Quality Profiles expose the same setting. It affects subsequent completed rig/mesh builds; enabling it does not immediately edit every avatar. Texture-only updates do not scan bones.

Enable **Measure Cleanup** to record CPU time. The inspector reports run count, removed skeleton entries, and last/total milliseconds; **Reset Cleanup Counters** resets only these diagnostic counters. Individual UMAData instances expose `LastBoneCleanupRemoved` and `LastBoneCleanupMilliseconds`. Timing covers dependency collection, registry removal and transform destruction calls, not a later Animator rebuild or Unity's deferred end-of-frame destruction. Editor destruction cost differs from player cost: benchmark on target hardware.

## Retention rules

- Every existing bone named in the effective T-pose is retained. An avatar override T-pose takes precedence over the race asset; edits are observed on subsequent builds.
- A destructive rebuild saves base-bone rest definitions and their connecting ancestors so a base bone can be restored even when no replacement slot supplies it. Current animated offsets are not saved as the new rest pose.
- Current enabled, unsuppressed slots retain their declared mesh skeleton and animated-bone dependencies, including unweighted helper bones. This is deliberately conservative: cleanup removes obsolete slot chains, not unused entries inside an active source slot's own skeleton declaration.
- Final skinned-renderer bindings, race Keep Bone Names, explicitly registered animation dependencies, and connecting ancestors are retained. Cached meshes remain shared; each avatar's transforms are managed independently.
- `UMAIgnore` (or the configured Ignore Tag) protects the tagged object and all descendants. An ignored attachment whose mount disappears is reparented to a surviving ancestor without changing its world pose.
- Caller-owned external skeletons are never pruned. No shared mesh, source slot, T-pose or prefab is modified.

Cleanup happens only after a complete successful mesh/rig transaction, including incremental builds and mesh-cache hits. Removed entries are removed from the skeleton lookup as well as the scene hierarchy. A later slot addition can recreate the chain normally.

## UMAIgnore versus UMAKeepChain

**UMAIgnore** means the subtree is outside UMA's skeleton processing. It is an attachment boundary, not a way to retain a skinned bone inside the bone lookup. Cleanup automatically enables saving/restoring these subtrees across destructive rig rebuilds.

**UMAKeepChain** keeps original Transform objects and attached components when a rebuild creates matching bones. New generated children are merged in, and renderer bones/rootBone are rebound to the retained transforms before duplicates are destroyed. It does **not** keep an obsolete, unused chain forever when cleanup is enabled. Put truly independent attachments under an ignored subtree.

When cleanup is off, **Save And Restore Ignored Items** continues to control legacy tagged-subtree preservation. The default keep tag follows UMA Project Settings; a nondefault generator `keepTag` remains an explicit legacy override. Both tags must exist in Unity's Tags and Layers settings. Bone names must remain unique within a UMA skeleton.

## Custom dependencies

UMA cannot discover arbitrary bone references hidden in user scripts. Declare required bones in the current slot or race's Keep Bone Names, or call `UMAData.RegisterAnimatedBone` / `RegisterAnimatedBoneHierarchy` for explicit animation dependencies. Bone-baking resets its registration set during mesh preparation, so register custom requirements as part of that build's callbacks, not just once at application startup. Release custom registrations when their owner no longer requires them; do not rely on lookup calls as ownership markers.

Keep cleanup off for integrations that cannot declare their dependencies. There is no recursive reflection scan of arbitrary components or vertex-weight scan during cleanup.
