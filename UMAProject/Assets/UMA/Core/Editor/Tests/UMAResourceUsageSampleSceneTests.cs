#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public sealed class UMAResourceUsageSampleSceneTests
    {
        private SceneSetup[] previousScenes;
        private bool previousOptionsEnabled;
        private EnterPlayModeOptions previousOptions;

        [Test]
        public void NPCRecipeCopyKeepsInternalAliasesButNotMutableSourceData()
        {
            var overlay = (OverlayData)Activator.CreateInstance(typeof(OverlayData), true);
            overlay.colorData = new OverlayColorData(1);
            var first = new SlotData(); var second = new SlotData();
            var overlays = new System.Collections.Generic.List<OverlayData> { overlay };
            first.SetOverlayList(overlays); second.SetOverlayList(overlays);
            var adjustments = new VertexDeltaAdjustmentCollection();
            adjustments.Add(new VertexDeltaAdjustment { vertexIndex = 3, delta = Vector3.up });
            first.meshModifiers.Add(new MeshModifier.Modifier { SlotName = "test", Scale = .5f, adjustments = adjustments });
            var recipe = new UMAData.UMARecipe { slotDataList = new[] { first, second }, sharedColors = new[] { overlay.colorData } };
            var method = typeof(UMAData).Assembly.GetType("UMA.UMANPCSnapshot").GetMethod("CloneRecipe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var clone = (UMAData.UMARecipe)method.Invoke(null, new object[] { recipe });
            Assert.That(clone.slotDataList[0].GetOverlayList(), Is.SameAs(clone.slotDataList[1].GetOverlayList()));
            Assert.That(clone.slotDataList[0].GetOverlayList(), Is.Not.SameAs(overlays));
            Assert.That(clone.slotDataList[0].GetOverlay(0).colorData, Is.SameAs(clone.sharedColors[0]));
            Assert.That(clone.sharedColors[0], Is.Not.SameAs(overlay.colorData));
            var modifier = clone.slotDataList[0].meshModifiers[0];
            modifier.Scale = 1;
            ((VertexDeltaAdjustment)modifier.adjustments.vertexAdjustments[0]).delta = Vector3.left;
            Assert.That(first.meshModifiers[0].Scale, Is.EqualTo(.5f));
            Assert.That(((VertexDeltaAdjustment)adjustments.vertexAdjustments[0]).delta, Is.EqualTo(Vector3.up));
        }

        [UnityTest]
        [Category("UMASampleIntegration")]
        public IEnumerator LimitedRandomCrowdReplaysItsPoolAndSharesMeshes()
            => LimitedCrowd(Environment.GetEnvironmentVariable("UMA_PERFORMANCE_NPC") == "1");

        [UnityTest]
        [Category("UMASampleIntegration")]
        public IEnumerator CompletedNPCsPreserveAppearanceStateAndLifetime() => LimitedCrowd(true);

        private IEnumerator LimitedCrowd(bool npc)
        {
            const string path = "Assets/UMA/SRP/Samples/Scenes/U3-Generating Random Characters.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) Assert.Ignore("Random crowd sample is not installed.");
            previousScenes = EditorSceneManager.GetSceneManagerSetup();
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            var installedSettings = AssetDatabase.LoadAssetAtPath<UMASettings>("Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset");
            Assert.That(installedSettings, Is.Not.Null);
            Debug.Log("Validating limited crowd: " + installedSettings.UMAVersion + "; Unity " + Application.unityVersion + "; " + Application.dataPath);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var monitor = UnityEngine.Object.FindAnyObjectByType<UMAResourceUsageMonitor>();
            var crowd = UnityEngine.Object.FindAnyObjectByType<UMARandomAvatar>();
            bool fullCrowd = Environment.GetEnvironmentVariable("UMA_REUSE_PERFORMANCE_FULL_CROWD") == "1";
            int size = fullCrowd ? 9 : 3;
            if (int.TryParse(Environment.GetEnvironmentVariable("UMA_PERFORMANCE_GRID_SIZE"), out int benchmarkSize))
                size = Mathf.Clamp(benchmarkSize, 3, 20);
            int setups = fullCrowd ? 8 : 2;
            monitor.InitialReuse = UMAResourceUsageMonitor.ReusePolicy.Off;
            crowd.GenerateGrid = true;
            crowd.GridXSize = crowd.GridZSize = size;
            crowd.MaximumUniqueCharacters = setups;
            monitor.LogCompletedRuns = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            yield return WaitFor(() => monitor.LastOff != null, monitor);
            // An optional fixed seed makes before/after executable revisions comparable.
            // Restart after Start has captured its initial state; do not affect scene assets.
            if (int.TryParse(Environment.GetEnvironmentVariable("UMA_PERFORMANCE_SEED"), out int benchmarkSeed))
            {
                var ambient = UnityEngine.Random.state;
                UnityEngine.Random.InitState(benchmarkSeed);
                typeof(UMARandomAvatar).GetField("initialRandomState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(crowd, UnityEngine.Random.state);
                UnityEngine.Random.state = ambient;
                var warmup = monitor.LastOff;
                monitor.RestartCrowd(false);
                yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOff != warmup, monitor);
            }
            var definitions = Definitions();
            Assert.That(definitions.Length, Is.EqualTo(size * size));
            Assert.That(definitions.Distinct().Count(), Is.EqualTo(setups));
            Assert.That(crowd.UniqueCharacterSetupCount, Is.EqualTo(setups));
            Debug.Log("Pooled crowd OFF:\n" + monitor.GetReport());
            var preparationBaseline = PreparationTimings();
            crowd.UseNPCBuilds = npc;
            if (float.TryParse(Environment.GetEnvironmentVariable("UMA_PERFORMANCE_SPAWN_BUDGET"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float spawnBudget))
                crowd.SpawnBudgetMilliseconds = spawnBudget;
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != null, monitor);
            CollectionAssert.AreEqual(definitions, Definitions(), "OFF/ON must rebuild the same pool and choose the same instances.");
            Assert.That(monitor.LastOn.CompletedAvatars, Is.EqualTo(size * size));
            if (!npc) Assert.That(monitor.LastOn.MeshCache.Hits + monitor.LastOn.MeshCache.PendingJoins, Is.GreaterThan(0), monitor.GetReport());
            else Assert.That(UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsSortMode.None).Sum(a => a.NPCShortcutHits),
                Is.EqualTo(size * size - setups), string.Join("\n", UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsSortMode.None).Select(a => a.NPCBuildStatus)));
            if (npc)
            {
                foreach (var character in Avatars()) AssertNPCAnimatorDrivesRig(character);
                Debug.Log("NPC Animator movement verified on " + (size * size) + " characters; races: " +
                    string.Join(", ", Avatars().Select(a => a.RacePreset).Distinct()));
            }
            Assert.That(monitor.LastOn.Memory.UniqueMeshes, Is.LessThan(monitor.LastOff.Memory.UniqueMeshes));
            Assert.That(monitor.LastOn.TextureCache.Bypasses, Is.Zero, monitor.GetReport());
            if (!npc) Assert.That(monitor.LastOn.TextureCache.Hits + monitor.LastOn.TextureCache.PendingJoins,
                Is.GreaterThan(0), "Empty AtlasUpdated events must not prevent pooled characters sharing textures.\n" + monitor.GetReport());
            Assert.That(monitor.LastOn.Memory.UniqueTextures, Is.LessThan(monitor.LastOff.Memory.UniqueTextures));
            if (!npc) Assert.That(monitor.LastOn.AtlasEarlyHits, Is.GreaterThan(0));
            Assert.That(monitor.LastOn.SpawnTimings[(int)UMAGenerationDiagnostics.Stage.Instantiate].Samples, Is.EqualTo(size * size));
            Assert.That(monitor.LastOn.SpawnTimings[(int)UMAGenerationDiagnostics.Stage.SpawnBatch].Samples,
                crowd.SpawnBudgetMilliseconds > 0 ? Is.GreaterThan(1) : Is.EqualTo(1));
            if (!npc) Assert.That(monitor.LastOn.GeneratorTimings[(int)UMAGenerationDiagnostics.Stage.BuildAllMeshHits].Samples, Is.GreaterThan(0));
            if (!npc) Assert.That(monitor.LastOn.MeshLookupTimings[(int)UMAGenerationDiagnostics.Stage.MeshLookupHit].Samples, Is.GreaterThan(0));
            var timingReport = JsonUtility.FromJson<UMAResourceUsageMonitor.Report>(monitor.GetReport());
            Assert.That(timingReport.LastOff.Completed, Is.True);
            Assert.That(timingReport.LastOn.Completed, Is.True);
            string timingReportPath = Environment.GetEnvironmentVariable("UMA_TIMING_REPORT_PATH");
            if (!string.IsNullOrEmpty(timingReportPath)) monitor.SaveReport(timingReportPath);
            Debug.Log($"Limited random crowd verified ({size * size} avatars, {setups} setups):\n" + monitor.GetReport());
            var preparationAfter = PreparationTimings();
            foreach (var timing in preparationAfter)
                Debug.Log($"Crowd ON preparation {timing.Key}: {UMATime.StopwatchTicksToMilliseconds(timing.Value - preparationBaseline[timing.Key]):F3} ms");
            if (npc && size == 3) yield return CheckNPCIndependenceAndFallback(crowd, monitor);
            var previousOff = monitor.LastOff;
            crowd.UseNPCBuilds = false;
            monitor.RestartCrowd(false);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOff != previousOff, monitor);
            CollectionAssert.AreEqual(definitions, Definitions());
            Assert.That(monitor.LastOff.CacheEntriesAtStart, Is.Zero, "The setup pool must not retain generated resources.");
            Debug.Log("Pooled crowd OFF again:\n" + monitor.GetReport());
        }

        private static IEnumerator CheckNPCIndependenceAndFallback(UMARandomAvatar crowd, UMAResourceUsageMonitor monitor)
        {
            var copy = Avatars().First(a => a.NPCShortcutHits == 1);
            var source = Avatars().First(a => a.NPCShortcutHits == 0 && a.GetRenderer(0).sharedMesh == copy.GetRenderer(0).sharedMesh);
            Assert.That(copy.umaRecipe, Is.Not.SameAs(source.umaRecipe));
            Assert.That(copy.dnaInstanceCollection, Is.Not.SameAs(source.dnaInstanceCollection));
            Assert.That(copy.animator, Is.Not.SameAs(source.animator));
            Assert.That(copy.animator.avatar, Is.Not.SameAs(source.animator.avatar));
            var sourceBones = source.GetRenderer(0).bones;
            var copyBones = copy.GetRenderer(0).bones;
            for (int i = 0; i < sourceBones.Length; i++)
            {
                Assert.That(copyBones[i], Is.Not.SameAs(sourceBones[i]));
                Assert.That(copyBones[i].IsChildOf(copy.transform), Is.True);
            }
            foreach (var pair in source.skeleton.boneHashData)
                Assert.That(copy.skeleton.boneHashData[pair.Key].umaTransform.position, Is.EqualTo(pair.Value.umaTransform.position), "Rest pose must not contain DNA twice.");
            AssertNPCGeometryMatches(source, copy);
            var dna = copy.GetDNA().First();
            float previous = dna.Value.Value;
            dna.Value.Set(previous + .01f);
            Assert.That(source.GetDNA()[dna.Key].Value, Is.EqualTo(previous));
            dna.Value.Set(previous);
            if (copy.GetRenderer(0).sharedMesh.blendShapeCount > 0)
            {
                float weight = source.GetRenderer(0).GetBlendShapeWeight(0);
                copy.GetRenderer(0).SetBlendShapeWeight(0, weight + 5);
                Assert.That(source.GetRenderer(0).GetBlendShapeWeight(0), Is.EqualTo(weight));
                copy.GetRenderer(0).SetBlendShapeWeight(0, weight);
            }
            var borrowed = (UMANPCBuildHandle)typeof(DynamicCharacterAvatar).GetField("npcOwnedTemplate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(source);
            using (var handle = borrowed.Retain())
            {
                string expected = source.GetAvatarDefinitionString(false, false);
                var probe = UnityEngine.Object.Instantiate(crowd.prefab).GetComponent<DynamicCharacterAvatar>();
                probe.BuildCharacterEnabled = false;
                probe.reuseGeneratedMeshes = probe.reuseGeneratedTextures = true;
                bool created = false, boundAtCreation = false;
                probe.CharacterCreated.AddListener(data =>
                {
                    created = true;
                    var arm = data.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    boundAtCreation = arm != null && arm.IsChildOf(data.umaRoot.transform);
                });
                probe.BuildNPC(handle);
                yield return WaitFor(() => created, monitor);
                Assert.That(probe.NPCShortcutHits, Is.EqualTo(1), probe.NPCBuildStatus);
                Assert.That(boundAtCreation, Is.True, "Creation callbacks must see the replacement rig, not the prefab rig pending destruction.");
                yield return null; // The old prefab rig has now been destroyed.
                AssertNPCAnimatorDrivesRig(probe);
                var releasedAvatar = probe.animator.avatar;
                Assert.That(UMAGeneratorBase.CreatedAvatars.Contains(releasedAvatar.GetUmaObjectId()), Is.True);
                UnityEngine.Object.Destroy(probe.gameObject);
                yield return null;
                yield return null;
                Assert.That(releasedAvatar == null, Is.True, "NPC native Avatar copies must be released when the character is destroyed.");

                probe = UnityEngine.Object.Instantiate(crowd.prefab).GetComponent<DynamicCharacterAvatar>();
                probe.BuildCharacterEnabled = false;
                probe.reuseGeneratedMeshes = probe.reuseGeneratedTextures = true;
                probe.BuildNPC(handle); probe.CancelNPCBuild();
                yield return null;
                Assert.That(probe.RendererCount, Is.Zero, "Cancelling a queued request must not instantiate it later.");
                UnityEngine.Object.Destroy(probe.gameObject);

                probe = UnityEngine.Object.Instantiate(crowd.prefab).GetComponent<DynamicCharacterAvatar>();
                probe.BuildCharacterEnabled = false;
                probe.reuseGeneratedMeshes = probe.reuseGeneratedTextures = true;
                int customBuilds = 0, customCompleted = 0;
                probe.BuildCharacterBegun.AddListener(_ => customBuilds++);
                probe.CharacterCreated.AddListener(_ => customCompleted++);
                probe.BuildNPC(handle);
                yield return WaitFor(() => customCompleted != 0, monitor);
                Assert.That(customBuilds, Is.EqualTo(1), "Custom build callbacks must not be skipped by NPC cloning.");
                Assert.That(customCompleted, Is.EqualTo(1));
                Assert.That(probe.NPCShortcutHits, Is.Zero);
                Assert.That(probe.GetAvatarDefinitionString(false, false), Is.EqualTo(expected));
                UnityEngine.Object.Destroy(probe.gameObject);

                UMANPCBuildHandle.InvalidateAll();
                Assert.That(handle.IsReady, Is.False);
                probe = UnityEngine.Object.Instantiate(crowd.prefab).GetComponent<DynamicCharacterAvatar>();
                probe.BuildCharacterEnabled = false;
                probe.reuseGeneratedMeshes = probe.reuseGeneratedTextures = true;
                int completed = 0;
                probe.CharacterCreated.AddListener(_ => completed++);
                probe.BuildNPC(handle);
                yield return WaitFor(() => completed != 0, monitor);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(probe.NPCShortcutHits, Is.Zero);
                Assert.That(probe.GetAvatarDefinitionString(false, false), Is.EqualTo(expected), "Fallback must retain the requested appearance.");
                UnityEngine.Object.Destroy(probe.gameObject);
            }
            copy.GetDNA().First().Value.Set(.43f);
            var replacedAvatar = copy.animator.avatar;
            string before = copy.GetAvatarDefinitionString(false, false);
            int updates = 0;
            copy.CharacterUpdated.AddListener(_ => updates++);
            copy.BuildCharacter();
            yield return WaitFor(() => updates != 0 && !copy.dirty, monitor);
            Assert.That(updates, Is.EqualTo(1));
            Assert.That(copy.GetAvatarDefinitionString(false, false), Is.EqualTo(before), "Normal edits after an NPC clone must retain wardrobe/DNA.");
            yield return null;
            Assert.That(replacedAvatar == null, Is.True, "Normal regeneration must release the previous NPC Avatar.");
            var survivingMesh = copy.GetRenderer(0).sharedMesh;
            crowd.ClearCharacterSetupPool();
            UnityEngine.Object.Destroy(source.gameObject);
            yield return null;
            yield return null;
            Assert.That(survivingMesh != null, Is.True, "Disposing the source and pool must not destroy a live instance's mesh.");
        }

        private static void AssertNPCGeometryMatches(DynamicCharacterAvatar source, DynamicCharacterAvatar copy)
        {
            source.skeleton.RestoreAll(); copy.skeleton.RestoreAll();
            for (int r = 0; r < source.RendererCount; r++)
            {
                var first = source.GetRenderer(r); var second = copy.GetRenderer(r);
                CollectionAssert.AreEqual(first.sharedMaterials, second.sharedMaterials);
                var weights = new float[first.sharedMesh.blendShapeCount, 2];
                for (int i = 0; i < weights.GetLength(0); i++)
                {
                    weights[i, 0] = first.GetBlendShapeWeight(i); weights[i, 1] = second.GetBlendShapeWeight(i);
                    first.SetBlendShapeWeight(i, 0); second.SetBlendShapeWeight(i, 0);
                }
                var a = new Mesh(); var b = new Mesh();
                try
                {
                    first.BakeMesh(a); second.BakeMesh(b);
                    Assert.That(b.vertexCount, Is.EqualTo(a.vertexCount));
                    var av = a.vertices; var bv = b.vertices;
                    for (int i = 0; i < av.Length; i += 97)
                        Assert.That((av[i] - bv[i]).sqrMagnitude, Is.LessThan(0.00000001f), "NPC skinning differs at vertex " + i);
                }
                finally
                {
                    UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                    for (int i = 0; i < weights.GetLength(0); i++)
                    { first.SetBlendShapeWeight(i, weights[i, 0]); second.SetBlendShapeWeight(i, weights[i, 1]); }
                }
            }
        }

        private static void AssertNPCAnimatorDrivesRig(DynamicCharacterAvatar character)
        {
            var animator = character.animator;
            Assert.That(animator, Is.Not.Null, character.name);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null, character.name);
            Assert.That(animator.avatar, Is.Not.Null, character.name);
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True, character.name);
            var arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Assert.That(arm, Is.Not.Null, character.name + " has no bound upper arm");
            Assert.That(arm.IsChildOf(character.umaRoot.transform), Is.True,
                character.name + " Animator is not driving its own generated rig");
            character.skeleton.RestoreAll();
            var rest = arm.localRotation;
            var culling = animator.cullingMode;
            try
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0);
                animator.Update(.25f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.Not.Zero, character.name);
                Assert.That(Quaternion.Angle(rest, arm.localRotation), Is.GreaterThan(5),
                    character.name + " stays in the rest pose despite an assigned Locomotion controller; " + character.NPCBuildStatus);
            }
            finally { animator.cullingMode = culling; }
        }

        [UnityTest]
        [Category("UMASampleIntegration")]
        public IEnumerator RandomCrowdSampleReplaysTheSameAvatarsWithReuseOffOnAndOff()
        {
            const string path = "Assets/UMA/SRP/Samples/Scenes/U3-Generating Random Characters.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                Assert.Ignore("The random crowd sample is not installed in this validation project.");
            previousScenes = EditorSceneManager.GetSceneManagerSetup();
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            var installedSettings = AssetDatabase.LoadAssetAtPath<UMASettings>("Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset");
            Assert.That(installedSettings, Is.Not.Null, "Validate against the current installed UMA settings, not a fallback-only test copy.");
            Assert.That(UMASettings.GetOrCreateSettings().UMAVersion, Is.EqualTo(installedSettings.UMAVersion));
            Debug.Log("Validating current UMA source: " + installedSettings.UMAVersion + "; Unity " + Application.unityVersion + "; project " + Application.dataPath);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var monitor = UnityEngine.Object.FindAnyObjectByType<UMAResourceUsageMonitor>();
            var crowd = UnityEngine.Object.FindAnyObjectByType<UMARandomAvatar>();
            Assert.That(monitor, Is.Not.Null, "The shipped scene must contain the monitor.");
            Assert.That(monitor.gameObject.name, Is.EqualTo("GeneratorParms"));
            Assert.That(monitor.InitialReuse, Is.EqualTo(UMAResourceUsageMonitor.ReusePolicy.Off));
            Assert.That(crowd, Is.Not.Null);
            // Reduce only the loaded test scene; never save these test overrides to disk.
            crowd.GenerateGrid = true;
            int gridSize = Environment.GetEnvironmentVariable("UMA_REUSE_PERFORMANCE_FULL_CROWD") == "1" ? 9 : 2;
            int avatarCount = gridSize * gridSize;
            crowd.GridXSize = crowd.GridZSize = gridSize;
            monitor.LogCompletedRuns = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            yield return WaitFor(() => monitor.LastOff != null, monitor);
            Assert.That(monitor.LastOff.SpawnedAvatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOff.CompletedAvatars, Is.EqualTo(avatarCount), monitor.Status);
            Assert.That(monitor.LastOff.Memory.UniqueMeshes, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.Memory.UniqueTextures, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.MeshMilliseconds, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.TextureMilliseconds, Is.GreaterThan(0));
            Assert.That(monitor.LastOff.MeshCache.Requests, Is.Zero);
            Assert.That(monitor.LastOff.TextureCache.Requests, Is.Zero);
            var originalAvatars = Avatars();
            var originalDefinitions = Definitions();
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != null, monitor);
            Assert.That(originalAvatars.All(a => a == null), Is.True, "Old avatars must be destroyed before the new run.");
            CollectionAssert.AreEqual(originalDefinitions, Definitions(), "OFF and ON must build the same random appearances.");
            Assert.That(Avatars().All(a => a.reuseGeneratedMeshes && a.reuseGeneratedTextures), Is.True);
            Assert.That(monitor.LastOn.MeshCache.Requests, Is.GreaterThan(0), monitor.GetReport() + "\n" + monitor.Generator.meshCombiner.GetType().Name);
            Assert.That(monitor.LastOn.TextureCache.Attempts, Is.GreaterThan(0), monitor.GetReport());
            if (monitor.LastOn.TextureCache.Bypasses > 0)
                Assert.That(monitor.LastOn.TextureCache.LastBypassReason, Is.Not.Null.And.Not.Empty);
            Assert.That(monitor.LastOn.Memory.Avatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOn.CountersReset, Is.False);
            var coldOn = monitor.LastOn;
            long lookupTicksBefore = LookupCounter("MeshLookupTicks");
            long lookupCountBefore = LookupCounter("MeshLookupCount");
            long detailedBefore = LookupCounter("MeshDetailedKeyCount");
            long rejectedBefore = LookupCounter("MeshFirstStageRejectCount");
            long admittedBefore = LookupCounter("MeshAdmissionCount");
            monitor.RestartCrowd(true);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOn != coldOn, monitor);
            CollectionAssert.AreEqual(originalDefinitions, Definitions());
            Assert.That(monitor.LastOn.CacheEntriesAtStart, Is.Zero);
            long warmLookups = LookupCounter("MeshLookupCount") - lookupCountBefore;
            double warmLookupMs = UMATime.StopwatchTicksToMilliseconds(LookupCounter("MeshLookupTicks") - lookupTicksBefore);
            long detailedKeys = LookupCounter("MeshDetailedKeyCount") - detailedBefore;
            long firstStageRejects = LookupCounter("MeshFirstStageRejectCount") - rejectedBefore;
            long admissions = LookupCounter("MeshAdmissionCount") - admittedBefore;
            Assert.That(warmLookups, Is.GreaterThan(0));
            var firstOff = monitor.LastOff;
            monitor.RestartCrowd(false);
            yield return WaitFor(() => !monitor.IsRestarting && monitor.LastOff != firstOff, monitor);
            CollectionAssert.AreEqual(originalDefinitions, Definitions());
            Assert.That(Avatars().All(a => !a.reuseGeneratedMeshes && !a.reuseGeneratedTextures), Is.True);
            Assert.That(monitor.LastOff.CacheEntriesAtStart, Is.Zero, "No cache entries should survive the previous crowd.");
            Assert.That(monitor.LastOff.Memory.Avatars, Is.EqualTo(avatarCount));
            Assert.That(monitor.LastOff.CountersReset, Is.False);
            Debug.Log("Resource monitor sample OFF/ON cold/ON warm/OFF verified. Last ON report:\n" + JsonUtility.ToJson(monitor.LastOn, true));
            Debug.Log($"Warm crowd mesh lookup: {warmLookupMs / warmLookups:F4} ms/request; {warmLookupMs / avatarCount:F4} ms/avatar including publication recheck ({warmLookups} lookups, {warmLookupMs:F2} ms total).");
            Debug.Log($"First-stage rejects: {firstStageRejects}/{admissions}; detailed keys created: {detailedKeys}. Timing includes dictionary lookup and candidate verification.");
            Debug.Log($"Reuse performance ({avatarCount} avatars): OFF cold/warm mesh {firstOff.MeshMilliseconds:F2}/{monitor.LastOff.MeshMilliseconds:F2} ms; " +
                $"ON cold/warm mesh {coldOn.MeshMilliseconds:F2}/{monitor.LastOn.MeshMilliseconds:F2} ms; OFF cold/warm ready {firstOff.WallMilliseconds:F2}/{monitor.LastOff.WallMilliseconds:F2} ms; " +
                $"ON cold/warm ready {coldOn.WallMilliseconds:F2}/{monitor.LastOn.WallMilliseconds:F2} ms.");
            Assert.That(warmLookupMs / avatarCount, Is.LessThan(.5), "All warm lookups per avatar must average below 0.5 ms on the actual sample inputs.");
            Assert.That(monitor.LastOn.MeshMilliseconds,
                Is.LessThan(Math.Max(firstOff.MeshMilliseconds, monitor.LastOff.MeshMilliseconds) * 3 + avatarCount * 10),
                "Reuse misses must not introduce per-avatar serialization stalls.");
        }

        private static long LookupCounter(string name) => (long)typeof(UMAResourceReuse)
            .GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);

        private static System.Collections.Generic.Dictionary<string, long> PreparationTimings()
        {
            var result = new System.Collections.Generic.Dictionary<string, long>();
            foreach (var type in new[] { typeof(DynamicCharacterAvatar), typeof(UMAJobifiedMeshCombiner), typeof(SkinnedMeshCombinerMeshAPI), typeof(UMAGeneratorPro) })
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    if (field.Name.StartsWith("Ticks_", StringComparison.Ordinal) && field.FieldType == typeof(long))
                        result.Add(type.Name + "." + field.Name, (long)field.GetValue(null));
            return result;
        }

        [UnityTearDown]
        public IEnumerator RestoreScene()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            if (previousScenes == null) yield break;
            EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
            EditorSettings.enterPlayModeOptions = previousOptions;
            if (previousScenes.Any(scene => scene.isLoaded && scene.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previousScenes = null;
        }

        private static DynamicCharacterAvatar[] Avatars() =>
            UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(a => a.name).ToArray();
        private static string[] Definitions() => Avatars().Select(a => a.GetAvatarDefinitionString(false, false)).ToArray();

        private static IEnumerator WaitFor(Func<bool> condition, UMAResourceUsageMonitor monitor)
        {
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, monitor.Status + "\n" + monitor.GetReport());
        }
    }
}
#endif
