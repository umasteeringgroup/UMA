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
            if (!session.Sewn) problems.Add("The seams are still being sewn. Let the simulation run until the cloth hangs.");
            if (s.generateMeshHide && !s.addToGlobalLibrary) problems.Add("Hiding the body underneath needs the clothes in the Global Library.");
            foreach (string uv in UvLayout.FindProblems(session.Pattern, session.Mesh))
            {
                if (uv.Contains("leaves the texture") || uv.Contains("overlap")) problems.Add(uv);
            }
            return problems;
        }

        public static Result Bake(FitSession session)
        {
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
            if (Mathf.Abs(slotEdge - drapeMesh.edgeLengthCm) > 1e-3f)
            {
                mesh = PatternMesher.Build(session.Pattern, drapeMesh.scales, slotEdge);
                positions = Weld(new PatternMeshSampler(drapeMesh).Carry(drape, mesh), WeldGroups(mesh.VertexCount, mesh.stitches));
            }
            var result = new Result { Vertices = mesh.VertexCount, Triangles = mesh.TriangleCount, SlotEdgeCm = mesh.edgeLengthCm, DrapeEdgeCm = drapeMesh.edgeLengthCm };
            foreach (string warning in mesh != drapeMesh ? mesh.warnings : new List<string>()) result.Messages.Add("Slot mesh: " + warning);
            result.Timings = timings;
            Mark("slot mesh");

            // 2-4. Weights, facing or thickness, parts, then the slot, overlay and recipe.
            SlotGeometry geometry = Geometry(session, mesh, positions, result.Messages);
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

            // 4b. Coarser copies for distant characters, each meshed half again as coarse, drawn with the same overlay.
            for (int level = 1; level <= Mathf.Clamp(settings.lodLevels, 0, 3) && result.Published.Slot != null; level++)
            {
                float lodEdge = mesh.edgeLengthCm * Mathf.Pow(1.5f, level);
                PatternMesh lodMesh = PatternMesher.Build(session.Pattern, drapeMesh.scales, lodEdge);
                Vector3[] lodPositions = Weld(new PatternMeshSampler(drapeMesh).Carry(drape, lodMesh), WeldGroups(lodMesh.VertexCount, lodMesh.stitches));
                var lodMessages = new List<string>();
                SlotGeometry lod = Geometry(session, lodMesh, lodPositions, lodMessages);
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

            // 4c. Topstitching: a texture overlay over the clothes' own, on the same slot.
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
                try { result.NormalMap = DrapeNormalMap.Write(map, mapPath); }
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
            public float maxWeightSumError;
            public bool seamWeightsMatch;
        }

        /// <summary>
        /// Weights from the body, the cloth facing out, the facing and rim (or a full thickness), and the
        /// buttons and parts, for a cloth mesh at welded positions.
        /// </summary>
        private static SlotGeometry Geometry(FitSession session, PatternMesh mesh, Vector3[] positions, List<string> messages)
        {
            BodyReference body = session.Body;
            ClothesFit fit = session.Fit;
            PublishSettings settings = fit.publish;
            int[] group = WeldGroups(mesh.VertexCount, mesh.stitches);
            // Sleeves (any piece laid round an arm) take weights from the arms whatever the setting.
            UMASlotSkinTransfer.TargetSurface donors = fit.donorsExcludeArms && !HasArmPieces(session.Pattern, fit) ? body.SurfaceWithoutArms() : body.Surface;
            UMASlotSkinTransfer.WeightTransferResult weights = UMASlotSkinTransfer.TransferWeights(positions, donors, body.FallbackBone);
            int fallback = weights.FallbackVertices;
            weights = ShareWeights(weights, group);
            var g = new SlotGeometry();
            for (int v = 0; v < positions.Length; v++)
            {
                // Search the full 10 cm: the plain search stops at about one grid cell and would call a loose hem far.
                if (body.Collider.Closest(positions[v], out _, out Vector3 near, out _) && (positions[v] - near).magnitude <= 0.1f) continue;
                if (!body.Collider.Closest(positions[v], 0.1f, out _, out Vector3 onBody, out _) || (positions[v] - onBody).magnitude > 0.1f) g.farVertices++;
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
            g.positions = slotPositions;
            g.triangles = slotTriangles;
            g.uv = new List<Vector2>(slotUv);
            g.weights = sourceOf != null ? CopyWeights(weights, sourceOf) : weights;
            g.weights.FallbackVertices = fallback;
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
                    RecalculateMissingNormals = true
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
                bool keepOut = !settings.addToGlobalLibrary;
                KeepOutOfLibrary(published.Slot, keepOut);
                if (published.Overlay != null && published.Overlay != overlay) KeepOutOfLibrary(published.Overlay, keepOut);
                KeepOutOfLibrary(published.Recipe, keepOut);
                return published;
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                if (slotMesh != null) UnityEngine.Object.DestroyImmediate(slotMesh);
            }
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

            // The recipe wears the slot with the clothes' overlay, then the topstitching over it.
            UMAWardrobeRecipe recipe = result.Published.Recipe;
            RaceData race = session.Fit.race != null ? session.Fit.race : session.Body.Skin.RaceData;
            var umaRecipe = new UMAData.UMARecipe();
            umaRecipe.ClearDna();
            umaRecipe.SetRace(race);
            var slotData = new SlotData(result.Published.Slot);
            if (race != null && !string.IsNullOrWhiteSpace(race.raceName)) slotData.Races = new[] { race.raceName };
            slotData.AddOverlay(new OverlayData(main));
            slotData.AddOverlay(new OverlayData(overlay));
            umaRecipe.SetSlot(0, slotData);
            recipe.Save(umaRecipe);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            result.Messages.Add("Topstitching drawn into '" + baseName + "', an overlay over the clothes' own on the same slot.");
            return overlay;
        }

        private static Vector3[] Weld(Vector3[] positions, int[] group)
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

        private static UMASlotSkinTransfer.WeightTransferResult ShareWeights(UMASlotSkinTransfer.WeightTransferResult weights, int[] group)
        {
            int n = group.Length;
            var offsets = new int[n + 1];
            for (int v = 0; v < n; v++) offsets[v + 1] = offsets[v] + weights.BonesPerVertex[v];
            var perVertex = new List<BoneWeight1>();
            var bones = new byte[n];
            for (int v = 0; v < n; v++)
            {
                int source = group[v];
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
