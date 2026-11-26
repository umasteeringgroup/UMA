using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Turns a drape into ordinary UMA clothing: the sewn seams welded to one position, bone weights
    /// from the body, normals by the battle-buddy rule, UVs from the pattern, then a slot, an overlay and
    /// a wardrobe recipe. Nothing it writes refers back to the pattern or the fit.
    /// </summary>
    public static class GarmentBaker
    {
        public sealed class Result
        {
            public UMASlotPublisher.Result Published;
            /// <summary>With Holds Hair Down on a garment with head pieces: the clipping-plane slot (see <see cref="HairSmoosh"/>).</summary>
            public SlotDataAsset SmooshPlane;
            public int Vertices;
            public int Triangles;
            public int FallbackVertices;
            /// <summary>Vertices more than 10 cm from any donor triangle: their weights are guesses.</summary>
            public int FarVertices;
            public float MaxWeightSumError;
            public bool SeamWeightsMatch;
            public Texture2D CheckTexture;
            /// <summary>The edge length the slot was baked at, and the drape's (cm).</summary>
            public float SlotEdgeCm, DrapeEdgeCm;
            /// <summary>The normal map baked from the finer drape, when there was one.</summary>
            public Texture2D NormalMap;
            public DrapeNormalMap.Stats NormalMapStats;
            /// <summary>Triangles added inside the openings (the facing) and round their edges (the rim).</summary>
            public int FacingTriangles, RimTriangles;
            /// <summary>How long each stage took, for finding a slow bake.</summary>
            public List<string> Timings = new List<string>();
            /// <summary>Triangles of buttons and parts added to the slot.</summary>
            public int PartTriangles;
            /// <summary>The LOD slots baked (Name_LOD1 ...), coarsest last, and their triangle counts.</summary>
            public readonly List<SlotDataAsset> LodSlots = new List<SlotDataAsset>();
            public readonly List<int> LodTriangles = new List<int>();
            /// <summary>The topstitching overlay, when there was any.</summary>
            public OverlayDataAsset TopstitchOverlay;
            /// <summary>The looks baked, each an overlay masked to its pieces.</summary>
            public List<GarmentLooks.BakedLook> Looks = new List<GarmentLooks.BakedLook>();
            /// <summary>The body shapes fitted, when the fit has any.</summary>
            public BodyShapes.Result BodyShapes;
            /// <summary>With a quad slot: how many quads, and the OBJ written beside it (or null).</summary>
            public int Quads;
            public string QuadObjPath;
            public readonly List<MeshHideAsset> MeshHides = new List<MeshHideAsset>();
            public readonly List<string> Messages = new List<string>();
        }

        /// <summary>True when any piece is laid round an arm: the clothes have sleeves.</summary>
        public static bool HasArmPieces(ClothesPattern pattern, ClothesFit fit)
        {
            if (pattern == null) return false;
            foreach (PatternPiece piece in pattern.pieces)
            {
                if (piece == null) continue;
                PiecePlacement placement = fit != null ? fit.PlacementFor(piece) : piece.placement;
                if (placement != null && PiecePlacement.IsArm(placement.part)) return true;
            }
            return false;
        }

        /// <summary>Checks before writing anything. Returns the problems in the artist's words; empty when the bake can go ahead.</summary>
        public static List<string> Validate(FitSession session)
        {
            var problems = new List<string>();
            if (session == null || !session.HasCloth) { problems.Add("There is no cloth to bake. Simulate first."); return problems; }
            PublishSettings s = session.Fit.publish;
            if (string.IsNullOrWhiteSpace(s.slotName)) problems.Add("Enter a name for the clothes.");
            if (s.material == null && s.existingOverlay == null) problems.Add("Choose a UMA material for the clothes.");
            if (s.createWardrobeRecipe && (string.IsNullOrWhiteSpace(s.wardrobeSlot) || s.wardrobeSlot == "None")) problems.Add("Choose a wardrobe slot.");
            if (!session.AllSewn) problems.Add("The seams are still being sewn. Let the simulation run until the cloth hangs.");
            if (s.generateMeshHide && !s.addToGlobalLibrary) problems.Add("Hiding the body underneath needs the clothes in the Global Library.");
            foreach (string uv in UvLayout.FindProblems(session.Pattern, session.Mesh))
            {
                if (uv.Contains("leaves the texture") || uv.Contains("overlap")) problems.Add(uv);
            }
            return problems;
        }

        public static Result Bake(FitSession session)
        {
            // A layout made quickly while the garment generator ran: pack it tightly before anything is painted on it.
            // The UVs change nothing about the cloth, so the drape stays as it is.
            bool tightened = false;
            if (session != null && session.Pattern != null && session.Pattern.uvLayout != null && session.Pattern.uvLayout.quickPacked && session.HasCloth)
            {
                UvLayout.Repack(session.Pattern);
                UvLayout.ApplyUVs(session.Pattern, session.Mesh);
                tightened = true;
            }
            List<string> problems = Validate(session);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
            PatternMesh drapeMesh = session.Mesh;
            BodyReference body = session.Body;
            ClothesFit fit = session.Fit;
            PublishSettings settings = fit.publish;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timings = new List<string>();
            void Mark(string stage) { timings.Add(stage + " " + clock.ElapsedMilliseconds + " ms"); clock.Restart(); }

            // 1. Weld each sewn group to one position (the render copies stay separate for their UVs).
            Vector3[] drape = Weld((Vector3[])session.SyncPositions().Clone(), WeldGroups(drapeMesh.VertexCount, drapeMesh.stitches));

            Mark("weld drape");
            // 1b. The slot may have its own mesh level: a coarser mesh takes its shape from the drape
            // through the flat pattern, and the drape's finer folds can go in a normal map (step 7).
            PatternMesh mesh = drapeMesh;
            Vector3[] positions = drape;
            float slotEdge = settings.BakeEdgeLengthCm(drapeMesh.edgeLengthCm);
            if (settings.slotTopology != SlotTopology.Triangles)
            {
                // Quads made from the pattern (a grid along the grain, loops along the outlines), the drape carried onto them.
                mesh = QuadMeshFor(session, drapeMesh, drape, slotEdge, out positions);
            }
            else if (Mathf.Abs(slotEdge - drapeMesh.edgeLengthCm) > 1e-3f)
            {
                mesh = PatternMesher.Build(session.Pattern, drapeMesh.scales, slotEdge);
                positions = Weld(new PatternMeshSampler(drapeMesh).Carry(drape, mesh), WeldGroups(mesh.VertexCount, mesh.stitches));
            }
            var result = new Result { Vertices = mesh.VertexCount, Triangles = mesh.TriangleCount, SlotEdgeCm = mesh.edgeLengthCm, DrapeEdgeCm = drapeMesh.edgeLengthCm };
            if (tightened) result.Messages.Add("Packed the UVs tightly first: the garment generator had laid them out quickly while you changed the style.");
            foreach (string warning in mesh != drapeMesh ? mesh.warnings : new List<string>()) result.Messages.Add("Slot mesh: " + warning);
            result.Timings = timings;
            Mark("slot mesh");
            // 1c. Auto and Simulated Fit pose the character (Simulated Fit drapes the clothes in each pose too), once
            // for the slot and its LODs.
            GarmentWeights.Poses weightPoses = GarmentWeights.Prepare(session, drapeMesh, drape, result.Messages);
            Func<Vector3[], PatternMesh, Vector3[]> carry = (onDrape, target) => target == drapeMesh ? onDrape : Weld(new PatternMeshSampler(drapeMesh).Carry(onDrape, target), WeldGroups(target.VertexCount, target.stitches));
            Mark("weight poses");

            // 2-4. Weights, facing or thickness, parts, then the slot, overlay and recipe.
            SlotGeometry geometry = Geometry(session, mesh, positions, result.Messages, weightPoses, carry);
            result.FallbackVertices = geometry.weights.FallbackVertices;
            result.FarVertices = geometry.farVertices;
            result.MaxWeightSumError = geometry.maxWeightSumError;
            result.SeamWeightsMatch = geometry.seamWeightsMatch;
            result.FacingTriangles = geometry.facingTriangles;
            result.RimTriangles = geometry.rimTriangles;
            result.PartTriangles = geometry.partTriangles;
            result.Vertices = geometry.positions.Length;
            result.Triangles = geometry.triangles.Length / 3;
            int[] triangles = geometry.clothTriangles;
            Mark("weights, facing and parts");
            result.Published = PublishGeometry(session, geometry, settings.slotName, settings.createWardrobeRecipe, settings.existingOverlay);
            Mark("build and publish slot");
            if (mesh.QuadCount > 0)
            {
                QuadRemesher.Measure(mesh, out float quadShare, out int irregular, out int leftover);
                result.Quads = mesh.QuadCount;
                result.Messages.Add("Slot meshed as " + mesh.QuadCount.ToString("N0") + " quads" + (leftover > 0 ? " and " + leftover + " triangles (" + (quadShare * 100f).ToString("0") + "% of the cloth in quads)" : "") +
                    ", " + irregular + " inside vertices not shared by four. Each quad is two triangles in the slot, split along its shorter diagonal.");
                if (settings.writeQuadObj && result.Published.Slot != null)
                {
                    string objPath = settings.folder.TrimEnd('/') + "/" + UMASlotSkinTransfer.MakeSafeAssetName(settings.slotName) + "_Quads.obj";
                    try
                    {
                        QuadObjWriter.Write(objPath, session.Pattern, mesh, positions, session.Arranged.flipTriangles);
                        AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceSynchronousImport);
                        result.Published.CreatedPaths.Add(objPath);
                        result.QuadObjPath = objPath;
                        result.Messages.Add("Wrote the quads, as draped, to '" + objPath + "' for other tools (Blender and others): one object per piece, sewn points joined, UVs from the pattern.");
                    }
                    catch (Exception ex) { result.Messages.Add("The quad OBJ could not be written: " + ex.Message); }
                }
            }
            Mark("quads");

            // 4b. Coarser copies for distant characters, each meshed half again as coarse, drawn with the same overlay.
            for (int level = 1; level <= Mathf.Clamp(settings.lodLevels, 0, 3) && result.Published.Slot != null; level++)
            {
                float lodEdge = mesh.edgeLengthCm * Mathf.Pow(1.5f, level);
                PatternMesh lodMesh;
                Vector3[] lodPositions;
                if (settings.slotTopology != SlotTopology.Triangles) lodMesh = QuadMeshFor(session, drapeMesh, drape, lodEdge, out lodPositions);
                else
                {
                    lodMesh = PatternMesher.Build(session.Pattern, drapeMesh.scales, lodEdge);
                    lodPositions = Weld(new PatternMeshSampler(drapeMesh).Carry(drape, lodMesh), WeldGroups(lodMesh.VertexCount, lodMesh.stitches));
                }
                var lodMessages = new List<string>();
                SlotGeometry lod = Geometry(session, lodMesh, lodPositions, lodMessages, weightPoses, carry);
                UMASlotPublisher.Result published = PublishGeometry(session, lod, settings.slotName + "_LOD" + level, false, result.Published.Overlay);
                if (published.Slot != null)
                {
                    result.LodSlots.Add(published.Slot);
                    result.LodTriangles.Add(lod.triangles.Length / 3);
                    result.Published.CreatedPaths.AddRange(published.CreatedPaths);
                }
            }
            if (result.LodSlots.Count > 0)
            {
                result.Messages.Add("Baked " + result.LodSlots.Count + " LOD slots (" + string.Join(", ", result.LodTriangles) + " triangles, against " + result.Triangles + "). A character swaps them in by distance with UMA's Simple LOD component.");
            }
            Mark("LOD slots");

            // 4c. Topstitching: a texture overlay over the clothes' own, on the same slot (worn in step 7c).
            if (settings.addTopstitch && GarmentExtras.HasTopstitch(session.Pattern) && result.Published.Overlay != null && result.Published.Recipe != null)
            {
                result.TopstitchOverlay = AddTopstitchOverlay(session, result);
            }
            Mark("topstitch");
            // 5. A texture that shows the pieces, so the UVs can be checked on the character.
            if (settings.makeUvCheckTexture && settings.existingOverlay == null && result.Published.Overlay != null)
            {
                string texturePath = settings.folder.TrimEnd('/') + "/" + UMASlotSkinTransfer.MakeSafeAssetName(settings.slotName) + "_UVCheck.png";
                result.CheckTexture = UvCheckTexture.Write(session.Pattern, mesh, texturePath, session.Pattern.uvLayout.bakeResolution);
                OverlayDataAsset overlay = result.Published.Overlay;
                // Only into an empty channel or over an earlier check texture: never over a painted texture.
                Texture current = overlay.textureList != null && overlay.textureList.Length > 0 ? overlay.textureList[0] : null;
                bool replaceable = current == null || AssetDatabase.GetAssetPath(current).EndsWith("_UVCheck.png", StringComparison.Ordinal);
                if (replaceable && overlay.textureList != null && overlay.textureList.Length > 0)
                {
                    overlay.textureList[0] = result.CheckTexture;
                    if (overlay.textureNames != null && overlay.textureNames.Length > 0) overlay.textureNames[0] = result.CheckTexture.name;
                    EditorUtility.SetDirty(overlay);
                    AssetDatabase.SaveAssetIfDirty(overlay);
                }
                result.Published.CreatedPaths.Add(texturePath);
            }

            Mark("UV check texture");
            Color32[] drapeNormals = null;
            int drapeNormalSize = 0;
            // 7. The drape's finer folds, in a normal map for the coarser slot.
            if (settings.bakeNormalMap && mesh != drapeMesh && drapeMesh.edgeLengthCm < mesh.edgeLengthCm - 1e-3f && result.Published.Overlay != null)
            {
                int size = settings.normalMapSize > 0 ? settings.normalMapSize : session.Pattern.uvLayout.bakeResolution;
                // The slot's frame, worked out the way the slot's own normals and tangents are but in the
                // drape's space: the slot is stored in bind space, turned from it, and a tangent-space map
                // must compare normals in one space. A turn of the whole mesh does not change the map.
                DrapeNormalMap.SlotFrame(positions, triangles, mesh.uv, out Vector3[] frameNormals, out Vector4[] frameTangents);
                Texture2D map = DrapeNormalMap.Bake(drapeMesh, drape, FacingOut(drapeMesh, session.Arranged.flipTriangles), mesh, triangles, frameNormals, frameTangents,
                    size, session.Pattern.uvLayout.gutterPixels, out DrapeNormalMap.Stats stats);
                string mapPath = settings.folder.TrimEnd('/') + "/" + UMASlotSkinTransfer.MakeSafeAssetName(settings.slotName) + "_DrapeNormal.png";
                try
                {
                    result.NormalMap = DrapeNormalMap.Write(map, mapPath);
                    // Kept for the looks (7b), whose normal maps carry the folds where they cover the clothes.
                    drapeNormals = map.GetPixels32();
                    drapeNormalSize = map.width;
                }
                finally { UnityEngine.Object.DestroyImmediate(map); }
                result.NormalMapStats = stats;
                result.Published.CreatedPaths.Add(mapPath);
                if (!PutInNormalChannel(result.Published.Overlay, settings.material, result.NormalMap))
                {
                    result.Messages.Add("The normal map was saved beside the slot, but the overlay's normal map channel already holds another texture (or the material has none), so it was not changed.");
                }
                result.Messages.Add("Baked the slot at " + mesh.edgeLengthCm.ToString("0.##") + " cm from a " + drapeMesh.edgeLengthCm.ToString("0.##") + " cm drape; its finer folds are in the normal map (on average " + stats.meanDegrees.ToString("0.0") + " degrees off the slot's surface, at most " + stats.maxDegrees.ToString("0") + ").");
            }
            else if (settings.bakeNormalMap && settings.bakeAtOwnLevel && mesh.edgeLengthCm >= drapeMesh.edgeLengthCm - 1e-3f)
            {
                result.Messages.Add("No normal map: the slot is not coarser than the drape. Simulate at a finer Mesh Level than the slot's to keep small folds in a normal map.");
            }

            Mark("normal map");
            // 7b. A look per piece: an overlay per fabric, masked to its pieces, over the clothes' own.
            if (settings.bakeLooks && GarmentLooks.HasLooks(session.Pattern) && result.Published.Overlay != null && result.Published.Recipe != null)
            {
                OverlayDataAsset main = result.Published.Overlay;
                UMAMaterial material = main.material != null ? main.material : settings.material;
                result.Looks = GarmentLooks.Bake(session.Pattern, mesh, material, settings.folder, settings.slotName, drapeNormals, drapeNormalSize,
                    settings.addToGlobalLibrary, result.Published.CreatedPaths, result.Messages);
                foreach (GarmentLooks.BakedLook look in result.Looks)
                {
                    result.Messages.Add("Look '" + look.look.name + "' on " + string.Join(", ", look.pieces) + ": an overlay over the clothes' own" +
                        (string.IsNullOrWhiteSpace(look.look.sharedColorName) ? "." : ", coloured by the shared colour '" + look.look.sharedColorName.Trim() + "'."));
                }
            }
            Mark("looks");
            // 7b. Hats and hoods: a plane across the head openings, so UMA holds the hair down inside them.
            if (settings.holdsHairDown && HairSmoosh.HasHeadPieces(session.Pattern) && result.Published.Slot != null)
            {
                result.SmooshPlane = HairSmoosh.Publish(session, result.Published.Slot, settings, result.Messages);
                Mark("hair plane");
            }
            // 7c. The recipe wears the slot with the clothes' overlay, the looks over it, then the topstitching (and the plane).
            if (result.Published.Recipe != null && (result.Looks.Count > 0 || result.TopstitchOverlay != null || result.SmooshPlane != null)) WearOverlays(session, result);
            // 7d. Other body shapes: the slot fitted to each, kept as blendshapes its DNA drives.
            if (settings.bakeBodyShapes && fit.bodyShapes != null && fit.bodyShapes.Count > 0 && result.Published.Slot != null)
            {
                RaceData race = fit.race != null ? fit.race : body.Skin.RaceData;
                result.BodyShapes = BodyShapes.Bake(race, result.Published.Slot, result.Published.Overlay, fit.dna, fit.bodyShapes, fit.clearanceCm * 0.01f, true);
                result.Messages.AddRange(result.BodyShapes.Messages);
            }
            Mark("body shapes");
            // 6. Optionally hide the body under the clothes (Strict: only fully covered triangles).
            if (settings.generateMeshHide && result.Published.Recipe != null)
            {
                GenerateMeshHide(result.Published.Recipe, fit.race, result);
                ProtectOpenings(session, result, OpeningMargin);
            }

            Mark("mesh hide");
            fit.output.slot = result.Published.Slot;
            fit.output.overlay = result.Published.Overlay;
            fit.output.recipe = result.Published.Recipe;
            fit.output.bakeRevision = mesh.revision;
            fit.output.bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (result.FallbackVertices > 0) result.Messages.Add(result.FallbackVertices + " vertices found no body surface and took the chest's weight.");
            if (result.FarVertices > 0) result.Messages.Add(result.FarVertices + " vertices are more than 10 cm from the body; check their weights in the Slot Weight Editor.");
            if (!result.SeamWeightsMatch) result.Messages.Add("Some sewn seam copies ended with different weights.");
            if (MaterialCullsBackFaces(settings.material) && result.FacingTriangles == 0) result.Messages.Add("The chosen material hides back faces, so the inside of the clothes is invisible at the neck, armholes and hem. Turn on Inside Facing, or use a double-sided material.");
            return result;
        }

        /// <summary>A slot's geometry before it is built: positions, triangles, UVs and weights, and what was added.</summary>
        private sealed class SlotGeometry
        {
            public Vector3[] positions;
            public int[] triangles, clothTriangles;
            public List<Vector2> uv;
            public UMASlotSkinTransfer.WeightTransferResult weights;
            public int farVertices, facingTriangles, rimTriangles, partTriangles;
            /// <summary>With Cloth Motion: how far each slot vertex may swing from its skinned place (m).</summary>
            public float[] motion;
            /// <summary>Sharp seams and folds: a side id per slot vertex (see <see cref="SharpEdges"/>), or null.</summary>
            public int[] normalSides;
            public float maxWeightSumError;
            public bool seamWeightsMatch;
        }

        /// <summary>
        /// Weights from the body, the cloth facing out, the facing and rim (or a full thickness), and the
        /// buttons and parts, for a cloth mesh at welded positions.
        /// </summary>
        private static SlotGeometry Geometry(FitSession session, PatternMesh mesh, Vector3[] positions, List<string> messages, GarmentWeights.Poses weightPoses = null, Func<Vector3[], PatternMesh, Vector3[]> carry = null)
        {
            BodyReference body = session.Body;
            ClothesFit fit = session.Fit;
            PublishSettings settings = fit.publish;
            int[] group = WeldGroups(mesh.VertexCount, mesh.stitches);
            // Weights by the fit's method (UMAWeightTransfer): sewn copies and battle buddies end with one set.
            UMA.Editors.Weighting.UMAWeightResult transferred = GarmentWeights.Transfer(session, mesh, positions, group, weightPoses,
                carry != null ? (Func<Vector3[], Vector3[]>)(onDrape => carry(onDrape, mesh)) : null, messages);
            UMASlotSkinTransfer.WeightTransferResult weights = transferred.ToTransferResult();
            int fallback = weights.FallbackVertices;
            var g = new SlotGeometry();
            // How far each vertex is from the body (for Cloth Motion and the far-vertex message). Search at least
            // 10 cm: the plain search stops at about one grid cell and would call a loose hem far.
            const float searchRadius = 0.1f;
            var distance = new float[positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                if (body.Collider.Closest(positions[v], out _, out Vector3 near, out _) && (positions[v] - near).magnitude <= 0.1f) { distance[v] = (positions[v] - near).magnitude; continue; }
                distance[v] = body.Collider.Closest(positions[v], searchRadius, out _, out Vector3 onBody, out _) ? (positions[v] - onBody).magnitude : float.MaxValue;
                if (distance[v] > 0.1f) g.farVertices++;
            }
            var check = new Result();
            CheckWeights(weights, group, check);
            g.maxWeightSumError = check.MaxWeightSumError;
            g.seamWeightsMatch = check.SeamWeightsMatch;
            int[] triangles = FacingOut(mesh, session.Arranged.flipTriangles);
            g.clothTriangles = triangles;

            Vector3[] slotPositions = positions;
            int[] slotTriangles = triangles;
            IList<Vector2> slotUv = mesh.uv;
            int[] sourceOf = null;
            // A full thickness is a facing reaching everywhere, joined to the outside at every opening.
            bool full = settings.fullThickness;
            if (full || settings.addFacing || settings.addRim)
            {
                OpeningFacing.Output facing = full
                    ? OpeningFacing.Build(mesh, session.Pattern, positions, triangles, group, true, 1000f, true, settings.thicknessMm * 0.001f)
                    : OpeningFacing.Build(mesh, session.Pattern, positions, triangles, group, settings.addFacing, settings.facingDepthCm * 0.01f, settings.addRim, settings.rimThicknessMm * 0.001f);
                slotPositions = facing.positions;
                slotTriangles = facing.triangles;
                slotUv = facing.uv;
                sourceOf = facing.sourceOf;
                g.facingTriangles = facing.facingTriangles;
                g.rimTriangles = facing.rimTriangles;
                if (full) messages.Add("Gave the clothes a " + settings.thicknessMm.ToString("0.#") + " mm thickness (" + (facing.facingTriangles + facing.rimTriangles) + " triangles inside and round the openings).");
                else if (facing.facingTriangles > 0 || facing.rimTriangles > 0)
                {
                    messages.Add("Added " + (settings.addFacing ? "a " + settings.facingDepthCm.ToString("0.#") + " cm facing inside the openings (" + facing.facingTriangles + " triangles)" : "")
                        + (settings.addFacing && settings.addRim ? " and " : "")
                        + (settings.addRim ? "a " + settings.rimThicknessMm.ToString("0.#") + " mm rim round them (" + facing.rimTriangles + " triangles)" : "") + ".");
                }
            }
            // Pieces with a thickness of their own (a belt, a waistband): a copy laid outward, walled round its outline.
            List<(int vertex, int side)> walls = null;
            if (PieceThickness.Any(session.Pattern))
            {
                var pos = new List<Vector3>(slotPositions);
                var tris = new List<int>(slotTriangles);
                var uv = new List<Vector2>(slotUv);
                var source = new List<int>(sourceOf ?? Identity(positions.Length));
                PieceThickness.Output thick = PieceThickness.Build(mesh, session.Pattern, positions, triangles, pos, tris, uv, source);
                if (thick.shellTriangles > 0)
                {
                    slotPositions = pos.ToArray(); slotTriangles = tris.ToArray(); slotUv = uv; sourceOf = source.ToArray();
                    walls = thick.walls;
                    messages.Add("Gave " + string.Join(", ", thick.pieces) + " a thickness of " + (thick.pieces.Count == 1 ? "its" : "their") + " own (" + (thick.shellTriangles + thick.wallTriangles) + " triangles).");
                }
            }
            if (settings.addParts)
            {
                var pos = new List<Vector3>(slotPositions);
                var tris = new List<int>(slotTriangles);
                var uv = new List<Vector2>(slotUv);
                var source = new List<int>(sourceOf ?? Identity(positions.Length));
                g.partTriangles = GarmentExtras.AddParts(session.Pattern, mesh, positions, triangles, pos, tris, uv, source, messages);
                if (g.partTriangles > 0)
                {
                    slotPositions = pos.ToArray(); slotTriangles = tris.ToArray(); slotUv = uv; sourceOf = source.ToArray();
                    messages.Add("Added " + GarmentExtras.PartsOf(session.Pattern).Count + " buttons and parts (" + g.partTriangles + " triangles).");
                }
            }
            // Sharp seams and folds: split along the folds, and a side id for each side of every sharp edge.
            if (SharpEdges.Any(session.Pattern, mesh))
            {
                int[] sides = SharpEdges.Apply(session.Pattern, mesh, ref slotPositions, ref slotTriangles, ref slotUv, ref sourceOf, out int split);
                g.normalSides = sides;
                messages.Add("Sharp edges: " + SharpEdges.CountSharp(sides) + " vertices keep a normal for each side" + (split > 0 ? " (" + split + " split along sharp folds)" : "") + ".");
            }
            // The thick pieces' walls keep normals of their own: crisp edges round the piece.
            if (walls != null && walls.Count > 0)
            {
                if (g.normalSides == null || g.normalSides.Length != slotPositions.Length)
                {
                    var sides = new int[slotPositions.Length];
                    if (g.normalSides != null) System.Array.Copy(g.normalSides, sides, Mathf.Min(sides.Length, g.normalSides.Length));
                    g.normalSides = sides;
                }
                foreach ((int vertex, int side) in walls) if (vertex < g.normalSides.Length) g.normalSides[vertex] = side;
            }
            g.positions = slotPositions;
            g.triangles = slotTriangles;
            g.uv = new List<Vector2>(slotUv);
            g.weights = sourceOf != null ? CopyWeights(weights, sourceOf) : weights;
            g.weights.FallbackVertices = fallback;
            if (settings.clothMotion)
            {
                // Each slot vertex swings as far as the cloth vertex it was made from (facing, rim and parts follow theirs).
                float[] swing = ClothMotion.MaxDistances(positions, group, mesh.triangles, distance, settings.motionHeldCm * 0.01f, settings.motionGain, settings.motionMaxCm * 0.01f);
                g.motion = new float[slotPositions.Length];
                for (int v = 0; v < g.motion.Length; v++)
                {
                    int s = sourceOf != null ? sourceOf[v] : v;
                    g.motion[v] = s >= 0 && s < swing.Length ? swing[s] : 0f;
                }
            }
            return g;
        }

        private static int[] Identity(int count)
        {
            var list = new int[count];
            for (int i = 0; i < count; i++) list[i] = i;
            return list;
        }

        /// <summary>
        /// Keeps assets the bake writes out of the Global Library when Add to Global Library is off. UMA
        /// adds new slots, overlays and recipes to the library by itself once their import has finished
        /// (UMAGlobalLibraryAutoRegistration); its No Auto Add flag stops that. With the setting on, the
        /// flag is cleared, so the assets are added as before.
        /// </summary>
        public static void KeepOutOfLibrary(UnityEngine.Object asset, bool keepOut)
        {
            if (!(asset is IUMAIndexOptions options) || options.NoAutoAdd == keepOut) return;
            options.NoAutoAdd = keepOut;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        /// <summary>Builds the slot mesh in bind space (normals by the battle-buddy rule) and publishes it.</summary>
        private static UMASlotPublisher.Result PublishGeometry(FitSession session, SlotGeometry g, string slotName, bool createRecipe, OverlayDataAsset overlay)
        {
            BodyReference body = session.Body;
            ClothesFit fit = session.Fit;
            PublishSettings settings = fit.publish;
            var source = new UMASlotSkinTransfer.SourceGeometry { TargetLocalVertices = g.positions, Triangles = g.triangles };
            Mesh slotMesh = null;
            GameObject temporary = null;
            try
            {
                slotMesh = UMASlotSkinTransfer.BuildSlotMesh(source, body.Skin, g.weights, new UMASlotSkinTransfer.SlotMeshOptions
                {
                    SlotName = slotName,
                    RecalculateMissingNormals = true,
                    NormalSides = g.normalSides
                });
                slotMesh.SetUVs(0, g.uv);
                slotMesh.RecalculateTangents();
                temporary = UMASlotSkinTransfer.CreateTemporaryRenderer(body.Skin.Renderer);
                var renderer = temporary.GetComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = slotMesh;
                renderer.bones = body.Skin.Bones;
                renderer.rootBone = body.Skin.RootBone;

                EnsureFolder(settings.folder);
                var request = new UMASlotPublisher.Request
                {
                    SlotName = slotName,
                    FolderPath = settings.folder,
                    AddToGlobalLibrary = settings.addToGlobalLibrary,
                    // Baking again updates the same assets: their GUIDs, and anything using them, stay.
                    UpdateInPlace = true,
                    OverwriteExisting = false,
                    CreateWardrobeRecipe = createRecipe,
                    Race = fit.race != null ? fit.race : session.Body.Skin.RaceData,
                    WardrobeSlot = settings.wardrobeSlot,
                    ExistingOverlay = overlay,
                    OverlayMaterial = settings.material
                };
                UMASlotPublisher.Result published = UMASlotPublisher.Publish(request, renderer, body.Skin);
                // The slot keeps its sharp edges, so normals worked out again later (Vertex Editor, body shapes) keep them.
                if (published.Slot != null)
                {
                    int[] keep = g.normalSides != null && published.Slot.meshData != null && g.normalSides.Length == published.Slot.meshData.vertexCount ? g.normalSides : new int[0];
                    if (keep.Length > 0 || (published.Slot.normalSides != null && published.Slot.normalSides.Length > 0))
                    {
                        published.Slot.normalSides = keep;
                        EditorUtility.SetDirty(published.Slot);
                        AssetDatabase.SaveAssetIfDirty(published.Slot);
                    }
                }
                bool keepOut = !settings.addToGlobalLibrary;
                KeepOutOfLibrary(published.Slot, keepOut);
                if (published.Overlay != null && published.Overlay != overlay) KeepOutOfLibrary(published.Overlay, keepOut);
                KeepOutOfLibrary(published.Recipe, keepOut);
                ApplyClothMotion(session, g, published.Slot);
                return published;
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                if (slotMesh != null) UnityEngine.Object.DestroyImmediate(slotMesh);
            }
        }

        /// <summary>
        /// The pattern meshed as quads at an edge length (all quads or mostly, as the publish settings say), with the
        /// drape carried onto it through the flat pattern, sewn points welded, each quad split along its shorter diagonal.
        /// </summary>
        public static PatternMesh QuadMeshFor(FitSession session, PatternMesh drapeMesh, Vector3[] drape, float edgeCm, out Vector3[] positions)
        {
            bool all = session.Fit.publish.slotTopology == SlotTopology.AllQuads;
            PatternMesh quads = QuadRemesher.Build(session.Pattern, drapeMesh.scales, edgeCm, all);
            positions = Weld(new PatternMeshSampler(drapeMesh).Carry(drape, quads), WeldGroups(quads.VertexCount, quads.stitches));
            QuadRemesher.SplitAlongShorterDiagonals(quads, positions);
            return quads;
        }

        /// <summary>
        /// With Cloth Motion: the slot's cloth coefficients (how far each vertex may swing from where the bones put
        /// it) and a renderer of its own with cloth settings, which UMA turns into Unity Cloth on the character.
        /// Without it, coefficients and renderer an earlier bake gave the slot are taken off again.
        /// </summary>
        private static void ApplyClothMotion(FitSession session, SlotGeometry g, SlotDataAsset slot)
        {
            if (slot == null || slot.meshData == null) return;
            PublishSettings settings = session.Fit.publish;
            bool on = settings.clothMotion && g.motion != null && g.motion.Length == slot.meshData.vertexCount;
            if (on)
            {
                slot.meshData.clothSkinningSerialized = ClothMotion.Serialize(slot.meshData.vertices, g.motion, Mathf.Max(0.001f, session.Fit.clearanceCm * 0.01f));
                slot.meshData.clothSkinning = null;
                // One renderer for the slot and its LOD slots.
                slot.RendererAsset = ClothMotion.RendererFor(settings.folder, settings.slotName, MainFabric(session.Pattern), null);
            }
            else if (slot.meshData.clothSkinningSerialized != null && slot.meshData.clothSkinningSerialized.Length > 0)
            {
                slot.meshData.clothSkinningSerialized = null;
                slot.meshData.clothSkinning = null;
                if (slot.RendererAsset != null && AssetDatabase.GetAssetPath(slot.RendererAsset).EndsWith("_ClothRenderer.asset", StringComparison.Ordinal)) slot.RendererAsset = null;
            }
            else return;
            EditorUtility.SetDirty(slot);
            AssetDatabase.SaveAssetIfDirty(slot);
        }

        /// <summary>The fabric of the piece with the most cloth, for settings that are one per garment.</summary>
        private static FabricSettings MainFabric(ClothesPattern pattern)
        {
            FabricSettings best = null;
            float most = -1f;
            foreach (PatternPiece piece in pattern.pieces)
            {
                if (piece == null || piece.points.Count < 3) continue;
                float area = Mathf.Abs(PatternGeometry.SignedArea(PatternGeometry.SampleOutline(piece, 1f, null)));
                if (area > most) { most = area; best = piece.fabric; }
            }
            return best;
        }

        /// <summary>
        /// Draws the topstitching into a texture, makes (or updates) an overlay with it in the clothes'
        /// material, and puts the overlay on the recipe's slot over the clothes' own overlay.
        /// </summary>
        private static OverlayDataAsset AddTopstitchOverlay(FitSession session, Result result)
        {
            PublishSettings settings = session.Fit.publish;
            OverlayDataAsset main = result.Published.Overlay;
            UMAMaterial material = main.material != null ? main.material : settings.material;
            if (material == null || material.channels == null || material.channels.Length == 0) { result.Messages.Add("No topstitching: the overlay has no material."); return null; }
            string baseName = UMASlotSkinTransfer.MakeSafeAssetName(settings.slotName) + "_Topstitch";
            string texturePath = settings.folder.TrimEnd('/') + "/" + baseName + ".png";
            Texture2D texture = GarmentExtras.WriteTopstitch(session.Pattern, texturePath, session.Pattern.uvLayout.bakeResolution, settings.topstitchColor);
            result.Published.CreatedPaths.Add(texturePath);
            string overlayPath = settings.folder.TrimEnd('/') + "/" + baseName + "_Overlay.asset";
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath);
            bool created = overlay == null;
            if (created)
            {
                overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
                overlay.name = baseName + "_Overlay";
            }
            overlay._oldOverlayName = baseName;
            overlay.material = material;
            var textures = new Texture[material.channels.Length];
            textures[0] = texture;
            overlay.textureList = textures;
            overlay.noAutoAdd = !settings.addToGlobalLibrary;
            if (created) { AssetDatabase.CreateAsset(overlay, overlayPath); result.Published.CreatedPaths.Add(overlayPath); }
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);
            if (settings.addToGlobalLibrary) UMAGlobalLibraryAutoRegistration.AddAndSave(overlay);
            result.Messages.Add("Topstitching drawn into '" + baseName + "', an overlay over the clothes' own on the same slot.");
            return overlay;
        }

        /// <summary>
        /// Saves the recipe wearing the slot with the clothes' own overlay, each look's overlay over it (in its
        /// colour, shared when the look names one) and the topstitching on top.
        /// </summary>
        private static void WearOverlays(FitSession session, Result result)
        {
            UMAWardrobeRecipe recipe = result.Published.Recipe;
            OverlayDataAsset main = result.Published.Overlay;
            RaceData race = session.Fit.race != null ? session.Fit.race : session.Body.Skin.RaceData;
            var umaRecipe = new UMAData.UMARecipe();
            umaRecipe.ClearDna();
            umaRecipe.SetRace(race);
            var slotData = new SlotData(result.Published.Slot);
            if (race != null && !string.IsNullOrWhiteSpace(race.raceName)) slotData.Races = new[] { race.raceName };
            slotData.AddOverlay(new OverlayData(main));
            var shared = new List<OverlayColorData>();
            foreach (GarmentLooks.BakedLook look in result.Looks)
            {
                if (look.overlay == null) continue;
                var overlay = new OverlayData(look.overlay);
                int channels = look.overlay.material != null && look.overlay.material.channels != null ? look.overlay.material.channels.Length : 1;
                OverlayColorData color = GarmentLooks.ColorFor(look.look, channels);
                if (color.IsASharedColor)
                {
                    // Looks naming the same shared colour share one, so a character recolours them together.
                    OverlayColorData existing = shared.Find(c => c.name == color.name);
                    if (existing != null) color = existing; else shared.Add(color);
                }
                overlay.colorData = color;
                slotData.AddOverlay(overlay);
            }
            if (result.TopstitchOverlay != null) slotData.AddOverlay(new OverlayData(result.TopstitchOverlay));
            umaRecipe.SetSlot(0, slotData);
            if (result.SmooshPlane != null) umaRecipe.SetSlot(1, HairSmoosh.PlaneSlotData(result.SmooshPlane, race));
            umaRecipe.sharedColors = shared.ToArray();
            recipe.Save(umaRecipe);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
        }

        /// <summary>Moves every sewn group's copies to their mean position (in place) and returns the array.</summary>
        public static Vector3[] Weld(Vector3[] positions, int[] group)
        {
            var sum = new Dictionary<int, Vector3>();
            var count = new Dictionary<int, int>();
            for (int v = 0; v < positions.Length; v++)
            {
                sum[group[v]] = (sum.TryGetValue(group[v], out Vector3 s0) ? s0 : Vector3.zero) + positions[v];
                count[group[v]] = (count.TryGetValue(group[v], out int c0) ? c0 : 0) + 1;
            }
            for (int v = 0; v < positions.Length; v++) positions[v] = sum[group[v]] / count[group[v]];
            return positions;
        }

        private static int[] FacingOut(PatternMesh mesh, bool flip)
        {
            int[] triangles = mesh.triangles.ToArray();
            if (flip)
            {
                for (int t = 0; t < triangles.Length; t += 3) { int tmp = triangles[t + 1]; triangles[t + 1] = triangles[t + 2]; triangles[t + 2] = tmp; }
            }
            return triangles;
        }

        /// <summary>
        /// Puts a baked normal map in the overlay's normal map channel: only an empty channel, or one that
        /// holds an earlier baked map. A normal map the artist chose is never replaced.
        /// </summary>
        private static bool PutInNormalChannel(OverlayDataAsset overlay, UMAMaterial material, Texture2D map)
        {
            UMAMaterial m = material != null ? material : overlay.material;
            if (m == null || m.channels == null || map == null) return false;
            int channel = -1;
            for (int i = 0; i < m.channels.Length; i++) if (m.channels[i].channelType == UMAMaterial.ChannelType.NormalMap) { channel = i; break; }
            if (channel < 0) return false;
            if (overlay.textureList == null || overlay.textureList.Length <= channel)
            {
                var list = new Texture[m.channels.Length];
                if (overlay.textureList != null) Array.Copy(overlay.textureList, list, Mathf.Min(list.Length, overlay.textureList.Length));
                overlay.textureList = list;
            }
            if (overlay.textureNames == null || overlay.textureNames.Length < overlay.textureList.Length)
            {
                var names = new string[overlay.textureList.Length];
                if (overlay.textureNames != null) Array.Copy(overlay.textureNames, names, overlay.textureNames.Length);
                overlay.textureNames = names;
            }
            Texture current = overlay.textureList[channel];
            bool replaceable = current == null || current == map || AssetDatabase.GetAssetPath(current).EndsWith("_DrapeNormal.png", StringComparison.Ordinal);
            if (!replaceable) return false;
            overlay.textureList[channel] = map;
            if (overlay.textureNames != null && overlay.textureNames.Length > channel) overlay.textureNames[channel] = map.name;
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);
            return true;
        }

        /// <summary>Union-find over the stitches: every vertex's welded group.</summary>
        public static int[] WeldGroups(int vertexCount, List<Vector2Int> stitches)
        {
            var parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            foreach (Vector2Int s in stitches)
            {
                int a = Find(s.x), b = Find(s.y);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
            for (int i = 0; i < vertexCount; i++) parent[i] = Find(i);
            return parent;
        }

        /// <summary>Weights for a longer vertex list where each vertex copies one of the originals.</summary>
        private static UMASlotSkinTransfer.WeightTransferResult CopyWeights(UMASlotSkinTransfer.WeightTransferResult weights, int[] sourceOf)
        {
            int n = weights.BonesPerVertex.Length;
            var offsets = new int[n + 1];
            for (int v = 0; v < n; v++) offsets[v + 1] = offsets[v] + weights.BonesPerVertex[v];
            var bones = new byte[sourceOf.Length];
            var perVertex = new List<BoneWeight1>(weights.BoneWeights.Length + (sourceOf.Length - n) * 4);
            for (int v = 0; v < sourceOf.Length; v++)
            {
                int source = sourceOf[v];
                bones[v] = weights.BonesPerVertex[source];
                for (int k = 0; k < bones[v]; k++) perVertex.Add(weights.BoneWeights[offsets[source] + k]);
            }
            return new UMASlotSkinTransfer.WeightTransferResult { BonesPerVertex = bones, BoneWeights = perVertex.ToArray(), FallbackVertices = weights.FallbackVertices };
        }

        private static void CheckWeights(UMASlotSkinTransfer.WeightTransferResult weights, int[] group, Result result)
        {
            int n = group.Length;
            var offsets = new int[n + 1];
            for (int v = 0; v < n; v++) offsets[v + 1] = offsets[v] + weights.BonesPerVertex[v];
            result.SeamWeightsMatch = true;
            for (int v = 0; v < n; v++)
            {
                float s = 0f;
                for (int k = offsets[v]; k < offsets[v + 1]; k++) s += weights.BoneWeights[k].weight;
                result.MaxWeightSumError = Mathf.Max(result.MaxWeightSumError, Mathf.Abs(1f - s));
                int g = group[v];
                if (g == v) continue;
                if (weights.BonesPerVertex[v] != weights.BonesPerVertex[g]) { result.SeamWeightsMatch = false; continue; }
                for (int k = 0; k < weights.BonesPerVertex[v]; k++)
                {
                    BoneWeight1 a = weights.BoneWeights[offsets[v] + k], b = weights.BoneWeights[offsets[g] + k];
                    if (a.boneIndex != b.boneIndex || Mathf.Abs(a.weight - b.weight) > 1e-6f) result.SeamWeightsMatch = false;
                }
            }
        }

        public static bool MaterialCullsBackFaces(UMAMaterial material)
        {
            Material m = material != null ? material.material : null;
            if (m == null) return false;
            foreach (string property in new[] { "_Cull", "_CullMode" })
            {
                if (m.HasProperty(property)) return Mathf.RoundToInt(m.GetFloat(property)) == 2;
            }
            if (m.HasProperty("_RenderFace")) return Mathf.RoundToInt(m.GetFloat("_RenderFace")) == 2;
            return false;
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Body skin closer than this to an opening of the clothes is never hidden (m).</summary>
        public const float OpeningMargin = 0.04f;

        /// <summary>
        /// Shows again the body triangles near the clothes' openings (neck, armholes, hem). Seen through
        /// an opening, a hidden triangle is a hole; the cloth only covers skin well inside its edges.
        /// </summary>
        public static int ProtectOpenings(FitSession session, Result result, float margin)
        {
            if (result.MeshHides.Count == 0 || session?.Body?.Skin?.UmaData == null) return 0;
            Vector3[] cloth = session.SyncPositions();
            var openings = new List<Vector3>();
            for (int v = 0; v < cloth.Length; v++)
            {
                int e = session.Mesh.boundaryEdge[v];
                if (e >= 0 && session.Pattern.pieces[session.Mesh.piece[v]].edges[e].opening) openings.Add(cloth[v]);
                else if (v < session.Mesh.onHole.Count && session.Mesh.onHole[v]) openings.Add(cloth[v]);
            }
            var baked = new Mesh();
            int restored = 0;
            try
            {
                session.Body.Skin.Renderer.BakeMesh(baked, true);
                Vector3[] body = baked.vertices;
                float margin2 = margin * margin;
                foreach (MeshHideAsset hide in result.MeshHides)
                {
                    SlotData worn = null;
                    foreach (SlotData sd in session.Body.Skin.UmaData.umaRecipe.slotDataList)
                    {
                        // A race with prebaked shapes wears copies named after the base slot.
                        if (sd != null && sd.asset != null && sd.asset.slotName.StartsWith(hide.AssetSlotName, StringComparison.Ordinal)) { worn = sd; break; }
                    }
                    BitArray[] flags = hide.triangleFlags;
                    if (worn == null || flags == null || flags.Length == 0 || flags[0] == null) continue;
                    int[] tris = worn.asset.meshData.submeshes[0].GetBaseTriangles();
                    var copy = new BitArray[flags.Length];
                    for (int sm = 0; sm < flags.Length; sm++) copy[sm] = flags[sm] != null ? new BitArray(flags[sm]) : new BitArray(0);
                    int changed = 0;
                    for (int t = 0; t < tris.Length / 3 && t < copy[0].Length; t++)
                    {
                        if (!copy[0][t]) continue;
                        Vector3 c = (body[worn.vertexOffset + tris[3 * t]] + body[worn.vertexOffset + tris[3 * t + 1]] + body[worn.vertexOffset + tris[3 * t + 2]]) / 3f;
                        foreach (Vector3 o in openings)
                        {
                            if ((o - c).sqrMagnitude < margin2) { copy[0][t] = false; changed++; break; }
                        }
                    }
                    if (changed == 0) continue;
                    hide.SaveSelectionForLOD(copy, 0);
                    AssetDatabase.SaveAssetIfDirty(hide);
                    restored += changed;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
            if (restored > 0) result.Messages.Add(restored + " body triangles near the openings were left visible, so no holes show through the neck, armholes or hem.");
            return restored;
        }

        /// <summary>Makes the mesh hides for a baked recipe. Its slot must be in the Global Library, where the generator looks it up.</summary>
        public static void GenerateMeshHide(UMAWardrobeRecipe recipe, RaceData race, Result result)
        {
            try
            {
                UMARigRestPose rig = UMARigRestPose.FromRace(race);
                List<UMAMeshHideAutoGenerator.BodySlot> bodySlots = UMAMeshHideAutoGenerator.CollectBodySlots(race, rig, result.Messages);
                UMAMeshHideAutoGenerator.RecipeResult hide = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, bodySlots, null,
                    new UMAMeshHideOcclusionSettings { strategy = MeshHideAsset.TriangleHideStrategy.Strict });
                result.Messages.AddRange(hide.Messages);
                result.Messages.Add("Mesh hide: " + hide.Summary());
                foreach (UMAMeshHideAutoGenerator.SlotResult slot in hide.Slots)
                {
                    if (slot.Asset != null) result.MeshHides.Add(slot.Asset);
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add("The mesh hide could not be made: " + ex.Message);
            }
        }
    }
}
