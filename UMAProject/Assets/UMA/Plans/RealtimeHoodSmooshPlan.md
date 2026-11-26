# Run-time smooshing for hoods and hats: upgrade plan

Keep hair inside a hood or hat while the character moves, the hood swings as cloth and the hair moves with its own physics. This is an upgrade to UMA's build-time smooshing. Asked for by the maintainer on 2026-11-24, with the Clothes Designer V2 work (`ClothesDesignerV2Plan.md`, item 5).

Status: proposal. Nothing built.

## What UMA does today

`DynamicCharacterAvatar.SmooshSlotPhysics` runs once per character build:

1. The hat slot (tagged `Smooshtarget`) is baked into a temporary physics mesh.
2. A clipping-plane slot (four vertices) splits space: hair points on the hat's side of the plane are moved towards the plane's centre by raycasts until they are inside the hat's mesh.
3. The result replaces the hair slot's bind-pose vertices (`VertexOverrides`), so it is skinned like the rest of the hair.

That is right for a rigid hat on still hair. It fails when:

- **the hood moves as cloth** (Clothes Designer Cloth Motion, Unity Cloth): the hair was fitted to the hood's rest shape, and the hood leaves it behind;
- **the hair moves** (spring bones, hair physics, the head turning under a cloth hood): the moved hair comes out through the hood;
- **many characters are built:** it costs a physics bake of the hat mesh and a raycast per hair vertex at every build.

## Proposal: a convex smoosh volume, applied in the hair's vertex shader

Every frame, push each hair vertex that is outside a simplified convex volume (the inside of the hood) back inside it, on the GPU, in the hair shader's vertex stage.

- **The volume.** A convex polytope of 16 to 32 planes, in the head bone's space, fitted inside the hood:
  - The Clothes Designer makes it at bake from the hood's inside surface, offset inward by the cloth's thickness plus a margin.
  - Directions are sampled evenly over a sphere; each plane is the supporting plane of the inside surface in that direction.
  - The plane at the face opening is the existing clipping plane: points in front of it are left alone, so fringes and beards are not pulled in.
- **The push.** Two ways, chosen per volume; the first is the default:
  1. **Towards the centre** (the maintainer's first idea). A point outside moves along the line to the volume's centre (about the middle of the head) until it is inside, less a margin:
     - The distance along the line is a minimum over the planes of one division each: no loops that depend on the data, cheap in a shader.
     - It keeps hair in the order it was in (outer strands stay outer) but squeezes it sideways. Over a band 1 to 2 cm wide inside the surface it eases in (smoothstep), so there is no crease at the edge.
  2. **Along the surface normal.** A point outside moves to the nearest point of the volume (the plane it is farthest outside of, then the next, a few rounds).
     - It keeps hair spread along the hood, but can fold strands over each other at the volume's corners.
- **In the shader.** An HLSL include (`UMASmooshVolume.hlsl`) with one function. It is called from the vertex stage of every UMA hair shader:
  - The URP and HDRP hair Shader Graphs, through a Custom Function node.
  - The URP and HDRP hair-card shaders.
  - It applies to the shadow, depth, motion-vector and forward passes alike.
  - The vertex has already been skinned, so the push follows bones, spring bones and blendshapes. Normals are bent by the same push (about the gradient of the falloff), so lighting stays plausible.
  - A shader keyword (`_UMA_SMOOSH_VOLUME`) keeps it free on characters with no hood.
- **On the CPU.**
  - A small component that UMA adds during the build, when a slot with a volume is worn. In `LateUpdate` it sets one matrix (head bone to object space) and the plane array on the character's hair material instances.
  - UMA already makes per-character materials, so this needs no MaterialPropertyBlock and keeps the SRP Batcher.
  - For a hood that moves as cloth, the volume follows the hood rather than the head. The planes are refitted every few frames from the cloth's current vertices (a small job over the hood's points: one max per direction), or the volume is skinned to the hood's own bones.

## Why not on the CPU or in physics

- **A Burst job on baked meshes:** reading skinned hair back every frame costs a `BakeMesh` per character, too slow for crowds.
- **Hair colliders in a physics system:** only works for simulated hair, and not for hair cards that move by bones.
- **The vertex shader:** costs about 20 to 40 instructions per hair vertex, has no readback, and covers every hair type UMA ships.

## Steps

| # | Step | Accepted when |
| --- | --- | --- |
| 1 | The projection maths in C#, with tests: a point inside stays put; a point outside ends inside, less the margin; the falloff is continuous | Unit tests on a cube, a sphere-like 26-plane volume and a degenerate thin volume |
| 2 | Fitting a volume to a slot's inside surface: `UMASmooshVolume` data (planes, centre, opening plane, bone) on the slot asset, made by the Clothes Designer at bake and by a "Make Smoosh Volume" button in the slot inspector for hats made elsewhere | The beanie's volume holds every point of its own inside surface, less the margin |
| 3 | The HLSL include and the keyword in the four hair shaders (URP and HDRP Shader Graphs, URP and HDRP hair cards) | A render test: hair through a beanie with the keyword off, none with it on (depth comparison of the hair against the hat) |
| 4 | The run-time component and the build step that adds it | A Play-mode test: a character wearing a hood, head turned 60 degrees, with no hair pixel in front of the hood |
| 5 | Cloth hoods: refitting the planes from the moving cloth | A hood simulated with Unity Cloth keeps the hair inside while running |
| 6 | Performance: 50 characters with hoods | Under 0.1 ms CPU per frame for the component; GPU cost measured on a mid-range card |

## Risks

- **Shader variant count:** one more keyword on hair shaders, times their existing keywords. Measure the build's variant count before and after.
- **Bounds and culling:** pushing hair inward never makes it larger, so the renderer's bounds still hold.
- **Hair outside the hood on purpose** (a ponytail through the hood's back opening, a fringe at the face): the opening planes keep the face side free. A painted per-vertex "do not smoosh" mask (the hair's vertex colour alpha) would cover a ponytail.
- **HDRP hair's own vertex work** (hair strands, tessellation) may need its own hook.
- **Build-time smooshing stays:** it is still right for rigid hats and costs nothing per frame. A slot can have both; the run-time volume then only does what the cloth or hair motion needs.
