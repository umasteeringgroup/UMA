#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    // These must run in Play Mode: Edit Mode destroys outgoing renderers immediately
    // and cannot reproduce Animator binding to an object awaiting deferred destruction.
    public sealed class UMAAnimatorRebuildTests
    {
        private readonly List<Object> assets = new List<Object>();
        private readonly List<UMAGenerator> pausedGenerators = new List<UMAGenerator>();
        private GameObject avatarObject, generatorObject;
        private UMAData data;
        private AnimatorRebuildTestGenerator generator;
        private UMAIncrementalMeshCombiner combiner;
        private SlotDataAsset slot;
        private DynamicDNAConverterController heels;
        private Animator animator;
        private Transform position;
        private int refreshes;
        private float savedFaceTime;
        private static readonly string[] RendererPaths = { "UMARenderer", "Custom Face" };

        private T Keep<T>(T value) where T : Object { assets.Add(value); return value; }

        [SetUp]
        public void SetUp()
        {
            // UMA's dirty queue is shared. Keep the project's generator from consuming
            // this fixture's sliced requests between the Work calls below.
            _ = UMAAssetIndexer.Instance.Generator;
            // Resources also includes UMA's hidden internal generator.
            foreach (var existing in Resources.FindObjectsOfTypeAll<UMAGenerator>())
            {
                if (!existing.gameObject.scene.IsValid() || !existing.gameObject.scene.isLoaded || !existing.enabled) continue;
                pausedGenerators.Add(existing);
                existing.enabled = false;
            }
            generatorObject = new GameObject("Animator rebuild generator");
            generator = generatorObject.AddComponent<AnimatorRebuildTestGenerator>();
            generator.enabled = false;
            generator.atlasResolution = 32;
            generator.autoSetRaceBlendshapes = false;
            generator.collectGarbage = false;
            generator.garbageCollectionRate = 0;
            generator.InterFrameDelay = 0;
            generator.IterationCount = 1;
            generator.MaxMultiStepWorkMilliseconds = 0.1f;

            avatarObject = new GameObject("Animator rebuild avatar");
            position = new GameObject("Position").transform;
            position.SetParent(avatarObject.transform, false);
            var global = new GameObject("Global").transform;
            global.SetParent(position, false);
            data = avatarObject.AddComponent<UMAData>();
            data.inheritGeneratorQuality = false;
            data.defaultRendererAsset = Keep(ScriptableObject.CreateInstance<UMARendererAsset>());
            data.umaRoot = position.gameObject;
            data.skeleton = new UMASkeleton(position);
            data.markNotReadable = false;
            data.reuseGeneratedMeshes = false;
            // Also exercise the case where staging's parent is the Animator root.
            combiner = avatarObject.AddComponent<UMAIncrementalMeshCombiner>();
            generator.meshCombiner = combiner;

            var race = Keep(ScriptableObject.CreateInstance<RaceData>());
            race.name = "AnimatorRebuildRace";
            race.useNewDNA = false;
            race.umaTarget = RaceData.UMATarget.Generic;
            race.genericRootMotionTransformName = "Global";
            var material = Keep(ScriptableObject.CreateInstance<UMAMaterial>());
            material.materialType = UMAMaterial.MaterialType.Atlas;
            material.material = Keep(new Material(Shader.Find("Hidden/InternalErrorShader")));
            slot = Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
            slot.name = "AnimatorRebuildSlot";
            int globalHash = UMAUtils.StringToHash("Global"), positionHash = UMAUtils.StringToHash("Position");
            slot.meshData = new UMAMeshData
            {
                SlotName = slot.slotName, vertexCount = 3, subMeshCount = 1,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = Enumerable.Repeat(Vector3.forward, 3).ToArray(),
                tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 3).ToArray(),
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                bindPoses = new[] { Matrix4x4.identity }, boneNameHashes = new[] { globalHash },
                umaBones = new[]
                {
                    new UMATransform { name = "Position", hash = positionHash, scale = Vector3.one, rotation = Quaternion.identity },
                    new UMATransform { name = "Global", hash = globalHash, parent = positionHash, scale = Vector3.one, rotation = Quaternion.identity }
                },
                rootBoneHash = globalHash, RootBoneName = "Global", umaBoneCount = 2,
                ManagedBonesPerVertex = new byte[] { 1, 1, 1 },
                ManagedBoneWeights = Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, 3).ToArray(),
                blendShapes = new[] { new UMABlendShape { shapeName = "eyeBlinkLeft", frames = new[]
                {
                    new UMABlendFrame { frameWeight = 100, deltaVertices = Enumerable.Repeat(Vector3.up * .1f, 3).ToArray() }
                } } }
            };
            var custom = Keep(ScriptableObject.CreateInstance<UMARendererAsset>());
            var serialized = new SerializedObject(custom);
            serialized.FindProperty("_RendererName").stringValue = RendererPaths[1];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var slots = new[] { new SlotData(slot), new SlotData(slot) };
            var overlay = Keep(ScriptableObject.CreateInstance<OverlayDataAsset>());
            overlay.material = material;
            foreach (var instance in slots) instance.AddOverlay(new OverlayData(overlay));
            data.umaRecipe = new UMAData.UMARecipe { slotDataList = slots };
            data.umaRecipe.SetRace(race);
            for (int i = 0; i < slots.Length; i++)
            {
                var rendererAsset = i == 0 ? null : custom;
                data.generatedMaterials.rendererAssets.Add(rendererAsset);
                data.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial
                {
                    umaMaterial = material, material = new Material(material.material), rendererAsset = rendererAsset,
                    resultingAtlasList = Array.Empty<Texture>(), cropResolution = new Vector2(32, 32), resolutionScale = Vector2.one,
                    materialFragments = new List<UMAData.MaterialFragment> { new UMAData.MaterialFragment
                    { slotData = slots[i], atlasRegion = new Rect(0, 0, 32, 32), overlayList = slots[i].GetOverlayList() } }
                });
            }
            heels = Keep(ScriptableObject.CreateInstance<DynamicDNAConverterController>());
            heels.DNAAsset = Keep(ScriptableObject.CreateInstance<DynamicUMADnaAsset>());
            heels.DNAAsset.dnaTypeHash = 17023651;
            heels.DNAAsset.Names = new[] { "HeelHeight" };
            serialized = new SerializedObject(heels);
            var overall = serialized.FindProperty("_overallModifiers");
            overall.FindPropertyRelative("_adjustScale").boolValue = true;
            overall.FindPropertyRelative("_scale").floatValue = .965f;
            overall.FindPropertyRelative("_bone").stringValue = "Position";
            overall.FindPropertyRelative("_scaleBoneHash").intValue = positionHash;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            data.animationController = CreateController();
            data.OnAnimatorStateSaved += value =>
            {
                refreshes++;
                savedFaceTime = value.animator.GetCurrentAnimatorStateInfo(1).normalizedTime;
            };
        }

        private AnimatorController CreateController()
        {
            var controller = Keep(new AnimatorController());
            controller.AddLayer("Base");
            controller.AddLayer("Face");
            controller.AddParameter("Amount", AnimatorControllerParameterType.Float);
            controller.AddParameter("Mode", AnimatorControllerParameterType.Int);
            controller.AddParameter("Enabled", AnimatorControllerParameterType.Bool);
            var rootClip = Keep(new AnimationClip());
            AnimationUtility.SetEditorCurve(rootClip, EditorCurveBinding.FloatCurve("Position", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, .125f));
            var idle = controller.layers[0].stateMachine.AddState("Idle");
            idle.motion = rootClip; idle.writeDefaultValues = true;
            controller.layers[0].stateMachine.defaultState = idle;
            // Scale is animated elsewhere in the controller, so Idle's write defaults
            // exercises the root-scale default captured when the Animator is rebound.
            var scaleClip = Keep(new AnimationClip());
            AnimationUtility.SetEditorCurve(scaleClip, EditorCurveBinding.FloatCurve("Position", typeof(Transform), "m_LocalScale.x"), AnimationCurve.Constant(0, 1, .8f));
            controller.layers[0].stateMachine.AddState("Scaled").motion = scaleClip;
            var faceClip = Keep(new AnimationClip());
            foreach (string path in RendererPaths)
                AnimationUtility.SetEditorCurve(faceClip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.eyeBlinkLeft"), AnimationCurve.Constant(0, 1, 75));
            var face = controller.layers[1].stateMachine.AddState("Blink");
            face.motion = faceClip; face.writeDefaultValues = true;
            controller.layers[1].stateMachine.defaultState = face;
            return controller;
        }

        [UnityTest] public IEnumerator SynchronousRebuildsRefreshCurves() => Rebuilds(false, false, false);
        [UnityTest] public IEnumerator SynchronousRebuildsWithKeepAvatarRefreshCurves() => Rebuilds(false, true, false);
        [UnityTest] public IEnumerator SlicedRebuildsRefreshCurves() => Rebuilds(true, false, false);
        [UnityTest] public IEnumerator SlicedRebuildsWithKeepAvatarRefreshCurves() => Rebuilds(true, true, false);
        [UnityTest] public IEnumerator ForceRebindRestoresPlaybackAfterBinding() => Rebuilds(false, true, true);

        private IEnumerator Rebuilds(bool sliced, bool keepAvatar, bool forceRebind)
        {
            data.KeepAvatar = keepAvatar;
            data.ForceRebindAnimator = forceRebind;
            yield return Build(sliced, true, false);
            animator = data.animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetLayerWeight(1, 1);
            animator.SetFloat("Amount", .42f); animator.SetInteger("Mode", 7); animator.SetBool("Enabled", true);
            animator.Play("Base.Idle", 0, .25f);
            animator.Play("Face.Blink", 1, .37f);
            animator.Update(0);
            var skeleton = data.skeleton;
            var avatar = animator.avatar;

            // Repeat equip -> remove -> equip for both wardrobe/body and mesh-only builds.
            foreach (bool shapeDirty in new[] { true, false })
            foreach (bool equipped in new[] { true, false, true })
            {
                var outgoing = data.GetRenderers().ToArray();
                int before = refreshes;
                // Add after mesh publication so no renderer's bone array references it.
                // Cleanup and renderer changes must share one Animator refresh.
                GameObject unused = null;
                Action<UMAData> addUnusedBone = null;
                addUnusedBone = value =>
                {
                    unused = new GameObject("Unused"); unused.transform.SetParent(position, false);
                    value.skeleton.AddBone(UMAUtils.StringToHash("Position"), UMAUtils.StringToHash("Unused"), unused.transform);
                    value.OnPreUpdateUMABody -= addUnusedBone;
                };
                data.OnPreUpdateUMABody += addUnusedBone;
                generator.CleanupUnusedBones = true;
                yield return Build(sliced, shapeDirty, equipped, () =>
                {
                    Assert.That(refreshes - before, Is.EqualTo(1), "One state save/restore per committed build.");
                    Assert.That(data.skeleton, Is.SameAs(skeleton));
                    if (keepAvatar) Assert.That(animator.avatar, Is.SameAs(avatar));
                    Assert.That(position.localScale.x, Is.EqualTo(equipped ? .965f : 1f).Within(.0001f), "Heel scale must reset before rebinding.");
                    Assert.That(animator.GetFloat("Amount"), Is.EqualTo(.42f));
                    Assert.That(animator.GetInteger("Mode"), Is.EqualTo(7));
                    Assert.That(animator.GetBool("Enabled"), Is.True);
                    Assert.That(animator.GetLayerWeight(1), Is.EqualTo(1));
                    Assert.That(animator.GetCurrentAnimatorStateInfo(1).fullPathHash, Is.EqualTo(Animator.StringToHash("Face.Blink")));
                    Assert.That(animator.GetCurrentAnimatorStateInfo(1).normalizedTime, Is.EqualTo(savedFaceTime).Within(.001f), "Rebinding must precede playback restoration.");
                    Assert.That(unused, Is.Not.Null);
                    Assert.That(unused.transform.parent, Is.Null, "Cleanup must finish before bindings are refreshed.");
                    AssertCurvesAndPaths();
                    foreach (var old in outgoing)
                    {
                        Assert.That(old != null, Is.True, "The test must observe play-mode deferred destruction.");
                        Assert.That(old.transform.parent, Is.Null);
                    }
                });
                yield return null;
                foreach (var old in outgoing) Assert.That(old == null, Is.True);
                AssertCurvesAndPaths();
                Assert.That(refreshes - before, Is.EqualTo(1), "No next-frame rebind may capture animated defaults.");
            }
        }

        private IEnumerator Build(bool sliced, bool shapeDirty, bool equipped, Action committed = null)
        {
            slot.slotDNA = equipped ? heels : null;
            data.ClearExternalSkeletonRoot(); // Standard DCA's non-FBX cleanup path.
            data.isMeshDirty = true; data.isShapeDirty = shapeDirty;
            data.isTextureDirty = false; data.isAtlasDirty = true; data.dirty = true;
            if (!sliced)
            {
                Assert.That(generator.GenerateSingleUMA(data, true), Is.True);
                committed?.Invoke();
                yield break;
            }
            generator.addDirtyUMA(data);
            for (int frame = 0; generator.QueueSize() != 0; frame++)
            {
                Assert.That(frame, Is.LessThan(600), "Sliced build did not complete.");
                generator.Work();
                // Inspect the commit in its own frame, before Object.Destroy takes effect.
                if (generator.QueueSize() != 0) yield return null;
            }
            committed?.Invoke();
        }

        private void AssertCurvesAndPaths()
        {
            for (int i = 0; i < RendererPaths.Length; i++)
            {
                var renderer = data.GetRenderer(i);
                Assert.That(data.transform.Find(RendererPaths[i]), Is.SameAs(renderer.transform));
                Assert.That(data.transform.Cast<Transform>().Count(t => t.name == RendererPaths[i]), Is.EqualTo(1));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(75).Within(.01f));
                renderer.SetBlendShapeWeight(0, 0);
            }
            animator.Update(0);
            foreach (var renderer in data.GetRenderers())
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(75).Within(.01f), "Clip must still drive the replacement renderer.");
        }

        [UnityTest]
        public IEnumerator CancellationBeforeCommitKeepsLivePathsAndBindings()
        {
            yield return Build(false, true, false);
            animator = data.animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetLayerWeight(1, 1); animator.Play("Face.Blink", 1, .37f); animator.Update(0);
            var original = data.GetRenderers().ToArray();
            int before = refreshes;
            using (var operation = combiner.BeginUpdateUMAMesh(true, data, 32))
            {
                var diagnostics = (IUMAMeshCombineOperationDiagnostics)operation;
                for (int step = 0; diagnostics.AtomicStepName != "Commit Mesh"; step++)
                {
                    Assert.That(step, Is.LessThan(600));
                    var result = operation.Step(UMAMeshCombineTimeSlice.Unlimited);
                    Assert.That(result.Status, Is.Not.EqualTo(UMAMeshCombineStatus.Failed), result.Error?.ToString());
                    CollectionAssert.AreEqual(original, data.GetRenderers());
                    AssertCurvesAndPaths();
                    yield return null;
                }
                operation.Cancel();
                Assert.That(operation.Step(UMAMeshCombineTimeSlice.Unlimited).Status, Is.EqualTo(UMAMeshCombineStatus.Cancelled));
            }
            yield return null;
            CollectionAssert.AreEqual(original, data.GetRenderers());
            AssertCurvesAndPaths();
            Assert.That(refreshes, Is.EqualTo(before));
            Assert.That(data.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(2));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (avatarObject != null) Object.Destroy(avatarObject);
            if (generatorObject != null) Object.Destroy(generatorObject);
            yield return null; yield return null;
            if (slot != null)
                foreach (var triangles in slot.meshData.submeshes)
                    if (triangles.nativeTriangles.IsCreated) triangles.nativeTriangles.Dispose();
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            assets.Clear();
            foreach (var existing in pausedGenerators) if (existing != null) existing.enabled = true;
            pausedGenerators.Clear();
        }
    }

    public sealed class AnimatorRebuildTestGenerator : UMAGenerator
    {
        public override void Awake() { }
    }
}
#endif
