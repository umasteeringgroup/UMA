# Copying Bone Weights to Clothes

UMA gives clothes their bone weights from the body they are worn on. One shared set of methods does this for the **Clothes Designer** (when it bakes), **Touchup Weights** (**Copy Weights from the Body**), the **Scene Mesh Slot Builder** (**Weighting**), and any other tool that turns geometry into a slot. This page explains the methods, when to use each, and how a tool calls them.

## The methods

| Method | What it does | Use it for |
|---|---|---|
| **Robust Inpaint** (default) | Copies the body only where the cloth clearly lies on it: within the **Match Distance** (4 cm), facing the same way within the **Match Angle** (35°), and not deep inside the body. The rest is filled in smoothly over the cloth from there. | Most clothes: shirts, jackets, hoods, wide sleeves, short skirts and dresses. |
| **Tight Surface** | Every vertex copies the closest point of the body. | Gloves, socks, leggings, body suits: clothes that lie on the skin. |
| **Volumetric** | Spreads the body's weights through the space round it, then each vertex samples that field. Cloth near the body keeps the body's own weights. | Long skirts, gowns, capes, flared coats, and parts that hang free of the body. |
| **Exact Match** | A vertex at a body vertex's place, or at the same UV on a body slot, copies that body vertex exactly. The rest is weighted as Tight Surface. | Clothes cut from the body mesh (a copy of the body's polygons). |
| **Simulated Fit** | Starts from Robust Inpaint, drapes the clothes in seven test poses with the cloth simulation, and fits the weights that best reproduce how the cloth hangs. | Clothes that move away from the body and back, such as a T-shirt's sleeves and hem. Clothes Designer only (it needs the simulation). |
| **Auto** | Weights the clothes with Tight Surface, Robust Inpaint and Volumetric, bends the character into the test poses, and keeps the one whose cloth goes into the body least, then stretches least. | When you are not sure. It needs a humanoid character (it falls back to Robust Inpaint otherwise). |

### Rules every method follows

- **Vertices at one place get one set of weights.** The copies of a vertex along a UV island edge, along a sewn seam, or where two slots meet move as one point. A seam never opens when the character moves.
- **Poke-through is handled.** Cloth slightly inside the body (less than 1.5 cm) still copies the body under it, which keeps it moving with the skin. Cloth deep inside the body is not copied: the closest point there is often another part of the body. It is filled in from the cloth around it instead, and the report counts it.
- **The body's DNA bones are kept.** Cloth on the body copies the body's adjust bones (belly, breasts, glutes and so on), so it follows the body when its DNA changes shape.
- **Weights are clean.** Each vertex keeps at most **Max Bones** (4), drops weights under 1% of its total, and sums to one.
- **Mirror Left/Right** (optional) makes the two sides match. Each vertex is averaged with its mirror image, with the Left and Right bones swapped by name (`Left`/`Right`, `_L`/`_R`, `.L`/`.R`, `L_`/`R_`).

## How well they do

Measured on Human Female 3.0 in seven test poses: walking (each leg forward), sitting, arms raised, arms forward, a twist with a forward bend, and a wide stance. *Inside* is the share of cloth vertices that end up inside the posed body (the deepest in brackets). *Stretch* is how much the cloth's edges change length, on average and at the worst edge. *Old* is the closest-point copy UMA used before.

| Clothes | Old | Tight Surface | Robust Inpaint | Volumetric | Simulated Fit |
|---|---|---|---|---|---|
| T-shirt (2,414 vertices) | 0.2% (40 mm), 2.8% / 396% | 0.2% (40 mm), 2.7% / 475% | 0.1% (9 mm), 2.6% / 184% | 0.1% (14 mm), 2.8% / 318% | **0.0% (5 mm), 2.6% / 166%** |
| A-line skirt (1,898) | 0.1% (15 mm), 7.4% / 1505% | 0.1% (13 mm), 7.4% / 1505% | 0.1% (11 mm), **5.4% / 178%** | 0.1% (12 mm), 5.8% / 176% | 0.2% (14 mm), 5.6% / 153% |
| Circle skirt (3,972) | 0.1% (12 mm), 9.6% / 2376% | 0.0% (11 mm), 9.6% / 2382% | 0.2% (34 mm), **4.3% / 174%** | **0.0% (11 mm)**, 7.7% / 255% | 0.2% (41 mm), 9.4% / 1678% |
| UMA 3 tennis dress (4,922) | | 0.1% (13 mm), 6.3% / 4062% | **0.1% (12 mm), 4.2% / 308%** | 0.1% (12 mm), 4.6% / 879% | |

What this shows:

- Copying the closest point stretches loose cloth badly at a few places. That is where the closest point jumps from one leg to the other, or from the torso to an arm. Robust Inpaint takes the worst edge from 15 to 24 times its length down to under 2 times on both skirts. On the T-shirt it takes the deepest poke-through from 40 mm to 9 mm.
- Volumetric keeps a very full skirt out of the legs best, at the cost of more stretch.
- Simulated Fit was the best on the T-shirt and close to Robust Inpaint on the A-line skirt. On the circle skirt it did worse: skinning cannot follow that much swing in every pose, and the fit's compromise stretches the cloth. For very full clothes, use Robust Inpaint or Volumetric, or add Cloth Motion.

Time on that machine: Robust Inpaint and Tight Surface take 0.1 to 0.3 s per garment. Volumetric takes 0.25 to 0.9 s. Simulated Fit takes 0.5 to 1.5 s, plus 2 to 3 s to drape the clothes in the poses with the GPU solver.

## GPU and CPU

- **Volumetric** solves its field on the GPU (`Core/Editor/Shaders/UMAWeightField.compute`) when the machine has compute shaders, and on the CPU otherwise. Both run the same arithmetic in the same order; the tests check they agree to within 0.0001.
- **Simulated Fit** drapes with the Clothes Designer's GPU solver, or its CPU solver when the designer is set to CPU or the GPU solver cannot start.
- Everything else runs on the CPU, on all cores. The fill is solved exactly, with a sparse Cholesky factorization.

## Where the methods come from

- **Robust Inpaint**: Abdrashitov, Raichstat, Monsen and Hill, *Robust Skin Weights Transfer via Weight Inpainting* (SIGGRAPH Asia 2023, Epic Games). The fill minimises the paper's energy over the cloth: the cotangent Laplacian plus its square, weighted by area. This is also the Inpaint mode of Unreal's Transfer Skin Weights.
- **Volumetric**: the diffused skinning field of Lin et al., *Learning Implicit Templates for Point-Based Clothed Human Modeling* (FITE, ECCV 2022). Here it is solved on a grid round the clothes, coarse grids first.
- **Simulated Fit**: the weight step of Le and Deng, *Smooth Skinning Decomposition with Rigid Bones* (SIGGRAPH Asia 2012; EA's Dem Bones), with the bones given. Two additions keep cloth out of the body. Cloth touching the body in a simulated pose counts ten times as much in that pose. A vertex keeps its fitted weights only if they put it inside the posed body no more often than its starting weights did.

## For tool authors

The methods live in `UMA.Editors.Weighting` (editor only):

```csharp
using UMA.Editors;
using UMA.Editors.Weighting;

// The body: a captured character surface (UMASlotSkinTransfer), optionally leaving some slots out.
UMASlotSkinTransfer.TargetData skin = UMASlotSkinTransfer.CaptureTargetData(avatar, 0, false);
UMASlotSkinTransfer.TargetSurface surface = UMASlotSkinTransfer.BuildTargetSurface(skin);
UMAWeightDonor body = UMAWeightDonor.FromSurface(surface, skin, fallbackBone);

// The clothes, in the same space as the body (the renderer's).
var clothes = new UMAWeightTarget { Positions = vertices, Triangles = triangles };

var settings = new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint };
// Optional: poses for Auto, and for reporting how the weights do.
settings.Poses = UMAWeightPoses.Capture(skin, body, null, out string why);

UMAWeightResult result = UMAWeightTransfer.Transfer(body, clothes, settings);
Debug.Log(result.Report.Summary());
Mesh slotMesh = UMASlotSkinTransfer.BuildSlotMesh(source, skin, result.ToTransferResult());
```

A slot builder that already has a `SourceGeometry` can do all of that in one call, as the Scene Mesh Slot Builder does:

```csharp
var weights = UMASlotSkinTransfer.TransferWeights(source, skin, surface,
    new UMAWeightSettings { Method = UMAWeightMethod.Auto }, fallbackBone, out UMAWeightReport report);
```

It sets up the body, the mesh's normals and UVs, Exact Match's slot UVs, the mirror plane, and Auto's test poses.

- **`UMAWeightTarget`** also takes `Groups` (sewn copies), `Locked` and `CurrentWeights` (vertices that keep their weights and act as fixed points for the fill), and `Region` (which part of the body each vertex may copy from).
- **`UMAWeightSettings.RegionTriangles`** gives each region its donor triangles. The Clothes Designer uses this so that a vest's side does not copy the arm next to it, while its sleeves do.
- **`UMAWeightReport`** gives each vertex's state (copied, filled in, from the field, fitted, kept), the vertices deep inside the body, the scores in the poses, and timings.
- **`UMAWeightTransfer.Score`** scores any weights in poses. **`UMAWeightPoses.Capture`** puts a humanoid character in the standard test poses and puts it back.

See also [Weight Touchup](WeightTouchup.md) and the Clothes Designer's *Fitting and Publishing* guide.
