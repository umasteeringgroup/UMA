#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAGeneratorMultiStepTests
    {
        private UMAAssetIndexer indexer;

        [SetUp]
        public void SetUp()
        {
            indexer = UMAAssetIndexer.Instance;
            Assert.NotNull(indexer, "The scheduler tests require the project UMAAssetIndexer resource.");
            indexer.dirtyList.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (indexer != null)
            {
                indexer.dirtyList.Clear();
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void GeneratorLifecycleCleanupDiscardsRetainedRequests()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject =
                    new GameObject("Generator lifecycle cleanup fixture");
                var generator = generatorObject
                    .AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject
                    .AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 10;
                ConfigureGenerator(generator, combiner);

                avatarObject =
                    new GameObject("Retained generation request fixture");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);
                generator.Work();

                Assert.IsFalse(generator.IsIdle());
                Assert.NotNull(combiner.lastOperation);

                generator.SimulateDisable();

                Assert.IsTrue(
                    generator.IsIdle(),
                    "Disabling a retained generator must clear requests from the prior play session.");
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsTrue(
                    combiner.lastOperation.disposed,
                    "Generator lifecycle cleanup must dispose its active incremental operation.");
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void MultiStepUmaRemainsQueuedUntilMeshOperationCompletes()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Multi-step scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 3;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Multi-step scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                int begunCount = 0;
                int createdCount = 0;
                data.OnCharacterBegun += _ => begunCount++;
                data.OnCharacterCreated += _ => createdCount++;

                generator.addDirtyUMA(data);

                generator.Work();
                Assert.AreEqual(1, generator.QueueSize());
                Assert.IsFalse(generator.IsIdle());
                Assert.IsTrue(generator.updateProcessing(data));
                Assert.AreEqual(1, combiner.lastOperation.completedSteps);
                Assert.AreEqual(1, begunCount);
                Assert.AreEqual(0, createdCount);
                Assert.IsTrue(data.isMeshDirty);

                generator.Work();
                Assert.AreEqual(1, generator.QueueSize());
                Assert.AreEqual(2, combiner.lastOperation.completedSteps);
                Assert.AreEqual(1, begunCount, "Character begun must not repeat while resuming mesh work.");
                Assert.AreEqual(0, createdCount);

                generator.Work();
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsTrue(generator.IsIdle());
                Assert.AreEqual(3, combiner.lastOperation.completedSteps);
                Assert.AreEqual(UMAMeshCombineStatus.Completed, combiner.lastOperation.Status);
                Assert.IsTrue(combiner.lastOperation.disposed);
                Assert.AreEqual(1, begunCount);
                Assert.AreEqual(1, createdCount);
                Assert.IsFalse(data.isMeshDirty);
                Assert.IsFalse(data.dirty);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void InterFrameDelayDoesNotBlockAnActiveMultiStepOperation()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Inter-frame scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 3;
                ConfigureGenerator(generator, combiner);
                generator.InterFrameDelay = 10;

                avatarObject = new GameObject("Inter-frame scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();
                generator.Work();
                generator.Work();

                Assert.AreEqual(3, combiner.lastOperation.completedSteps);
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsTrue(generator.IsIdle());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void RemovingActiveUmaCancelsAndDisposesItsOperation()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Cancellation scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 5;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Cancellation scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);
                generator.Work();

                FakeMeshCombineOperation operation = combiner.lastOperation;
                Assert.AreEqual(1, operation.completedSteps);
                var debugHistory = typeof(UMAGeneratorBuiltin).GetField("umaDatasGenerated")?.GetValue(generator) as System.Collections.IList;
                Assert.IsTrue(debugHistory == null || debugHistory.Contains(data));
                generator.removeUMA(data);
                Assert.IsTrue(debugHistory == null || !debugHistory.Contains(data), "Debug history must not retain removed avatar recipes.");

                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsFalse(
                    generator.IsIdle(),
                    "The generator remains internally active until cancellation reaches a safe boundary.");
                Assert.AreEqual(1, operation.cancelCalls);
                Assert.IsFalse(operation.disposed);

                generator.Work();

                Assert.IsTrue(generator.IsIdle());
                Assert.IsTrue(operation.disposed);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void ExistingCombinerStillCompletesSynchronouslyInOneWorkCall()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Synchronous scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<SynchronousSchedulerTestMeshCombiner>();
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Synchronous scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();

                Assert.AreEqual(1, combiner.updateCalls);
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsTrue(generator.IsIdle());
                Assert.IsFalse(data.isMeshDirty);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void SharedBudgetRunsOnlyTheStepsThatFitThisWorkCall()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Budgeted scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 5;
                ConfigureGenerator(generator, combiner);
                generator.MaxMultiStepWorkMilliseconds = 2f;

                avatarObject = new GameObject("Budgeted scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();
                Assert.AreEqual(2, combiner.lastOperation.completedSteps);
                Assert.AreEqual(1, generator.QueueSize());

                generator.Work();
                Assert.AreEqual(4, combiner.lastOperation.completedSteps);
                Assert.AreEqual(1, generator.QueueSize());

                generator.Work();
                Assert.AreEqual(5, combiner.lastOperation.completedSteps);
                Assert.AreEqual(0, generator.QueueSize());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void ZeroBudgetRunsImmediatelyUntilCompletion()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Unlimited scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 5;
                ConfigureGenerator(generator, combiner);
                generator.MaxMultiStepWorkMilliseconds = 0f;

                avatarObject = new GameObject("Unlimited scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();

                Assert.AreEqual(5, combiner.lastOperation.completedSteps);
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsTrue(generator.IsIdle());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void WaitingForAsyncAlwaysYieldsEvenWithTimeRemaining()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Waiting scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 2;
                combiner.waitOnceAfterFirstStep = true;
                ConfigureGenerator(generator, combiner);
                generator.MaxMultiStepWorkMilliseconds = 0f;

                avatarObject = new GameObject("Waiting scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();
                Assert.AreEqual(1, combiner.lastOperation.completedSteps);
                Assert.AreEqual(UMAMeshCombineStatus.WaitingForAsync, combiner.lastOperation.Status);
                Assert.AreEqual(1, generator.QueueSize());

                generator.Work();
                Assert.AreEqual(2, combiner.lastOperation.completedSteps);
                Assert.AreEqual(0, generator.QueueSize());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void AtomicStepOverrunIsRecorded()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Overrun scheduler generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 1;
                ConfigureGenerator(generator, combiner);
                combiner.onStep = () => System.Threading.Thread.Sleep(5);

                avatarObject = new GameObject("Overrun scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);

                generator.Work();

                Assert.GreaterOrEqual(generator.multiStepBudgetOverrunCount, 1);
                Assert.Greater(generator.lastMultiStepAtomicStepMilliseconds, 1f);
                Assert.GreaterOrEqual(
                    generator.maximumMultiStepAtomicStepMilliseconds,
                    generator.lastMultiStepAtomicStepMilliseconds);
                Assert.AreEqual(
                    "Ready",
                    generator.maximumMultiStepAtomicStepName,
                    "The completed operation must retain the name sampled before Step advanced its stage.");
                Assert.AreEqual(
                    "Ready",
                    generator.lastMultiStepBudgetOverrunStepName);
                var overruns =
                    new List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>();
                generator.GetMultiStepBudgetOverrunStatistics(overruns);
                Assert.AreEqual(1, overruns.Count);
                Assert.AreEqual("Ready", overruns[0].StepName);
                Assert.AreEqual(1, overruns[0].Count);
                Assert.Greater(overruns[0].MaximumStepMilliseconds, 1f);
                Assert.Greater(overruns[0].MaximumOverrunMilliseconds, 0f);
                var stepTimings =
                    new List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic>();
                generator.GetMultiStepAtomicStepStatistics(stepTimings);
                Assert.AreEqual(1, stepTimings.Count);
                Assert.AreEqual("Ready", stepTimings[0].StepName);
                Assert.AreEqual(1, stepTimings[0].Count);
                Assert.Greater(stepTimings[0].AverageMilliseconds, 1d);
                Assert.AreEqual(
                    stepTimings[0].TotalMilliseconds,
                    stepTimings[0].AverageMilliseconds,
                    0.0001d);

                generator.ResetStatistics();
                generator.GetMultiStepBudgetOverrunStatistics(overruns);
                Assert.IsEmpty(overruns);
                generator.GetMultiStepAtomicStepStatistics(stepTimings);
                Assert.IsEmpty(stepTimings);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void StopwatchTicksUsePlatformFrequency()
        {
            double oneSecondMilliseconds =
                UMATime.StopwatchTicksToMilliseconds(
                    System.Diagnostics.Stopwatch.Frequency);

            Assert.AreEqual(1000d, oneSecondMilliseconds, 0.0001d);
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void ResetStatisticsClearsIncrementalAndStageMetrics()
        {
            GameObject generatorObject = null;
            try
            {
                generatorObject = new GameObject("Statistics reset generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                generator.ElapsedTicks = 1;
                generator.validationTicks = 2;
                generator.meshUpdatesTicks = 3;
                generator.multiStepBudgetOverrunCount = 4;
                generator.lastMultiStepAtomicStepMilliseconds = 5f;
                generator.lastMultiStepAtomicStepName = "Last Step";
                generator.maximumMultiStepAtomicStepMilliseconds = 6f;
                generator.maximumMultiStepAtomicStepName = "Longest Step";
                generator.lastMultiStepBudgetOverrunStepName = "Overrun Step";
                generator.lastMultiStepBudgetOverrunStepMilliseconds = 7f;
                generator.lastMultiStepBudgetOverrunAmountMilliseconds = 2f;
                generator.lastMultiStepGenerationLatencyTicks = 7;
                generator.maximumMultiStepGenerationLatencyTicks = 8;
                generator.multiStepDiscardedMeshTicks = 9;

                generator.ResetStatistics();

                Assert.AreEqual(0, generator.ElapsedTicks);
                Assert.AreEqual(0, generator.validationTicks);
                Assert.AreEqual(0, generator.meshUpdatesTicks);
                Assert.AreEqual(0, generator.multiStepBudgetOverrunCount);
                Assert.AreEqual(0f, generator.lastMultiStepAtomicStepMilliseconds);
                Assert.AreEqual(string.Empty, generator.lastMultiStepAtomicStepName);
                Assert.AreEqual(0f, generator.maximumMultiStepAtomicStepMilliseconds);
                Assert.AreEqual(string.Empty, generator.maximumMultiStepAtomicStepName);
                Assert.AreEqual(
                    string.Empty,
                    generator.lastMultiStepBudgetOverrunStepName);
                Assert.AreEqual(
                    0f,
                    generator.lastMultiStepBudgetOverrunStepMilliseconds);
                Assert.AreEqual(
                    0f,
                    generator.lastMultiStepBudgetOverrunAmountMilliseconds);
                Assert.AreEqual(0, generator.lastMultiStepGenerationLatencyTicks);
                Assert.AreEqual(0, generator.maximumMultiStepGenerationLatencyTicks);
                Assert.AreEqual(0, generator.multiStepDiscardedMeshTicks);
            }
            finally
            {
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void GeneratorOverrideCapturesAppliesAndRestoresMultiStepBudget()
        {
            GameObject generatorObject = null;
            GameObject overrideObject = null;
            try
            {
                generatorObject = new GameObject("Override budget generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                generator.MaxMultiStepWorkMilliseconds = 7.5f;

                overrideObject = new GameObject("Generator override fixture");
                overrideObject.SetActive(false);
                var generatorOverride = overrideObject.AddComponent<UMAGeneratorOverride>();
                generatorOverride.MaxMultiStepWorkMilliseconds = 1.25f;

                Type stateType = typeof(UMAGeneratorOverride).GetNestedType(
                    "GeneratorState",
                    BindingFlags.NonPublic);
                Assert.NotNull(stateType);
                MethodInfo capture = stateType.GetMethod(
                    "Capture",
                    BindingFlags.Public | BindingFlags.Static);
                MethodInfo restore = stateType.GetMethod(
                    "ApplyTo",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo applyOverride = typeof(UMAGeneratorOverride).GetMethod(
                    "ApplyTo",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(capture);
                Assert.NotNull(restore);
                Assert.NotNull(applyOverride);

                object state = capture.Invoke(null, new object[] { generator });
                applyOverride.Invoke(generatorOverride, new object[] { generator });
                Assert.AreEqual(1.25f, generator.MaxMultiStepWorkMilliseconds);

                restore.Invoke(state, new object[] { generator });
                Assert.AreEqual(7.5f, generator.MaxMultiStepWorkMilliseconds);
            }
            finally
            {
                if (overrideObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(overrideObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("GeneratorScheduler")]
        [Category("CombinerSwitcher")]
        public void IncrementalCombinerIsExposedByEditorSwitcherWorkflows()
        {
            GameObject generatorObject = null;
            try
            {
                Type modeType =
                    typeof(UMAMeshCombinerSwitcherWindow).GetNestedType(
                        "CombinerMode",
                        BindingFlags.NonPublic);
                Assert.NotNull(modeType);
                CollectionAssert.Contains(
                    Enum.GetNames(modeType),
                    "Incremental");

                generatorObject =
                    new GameObject("Incremental switcher generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                generator.meshCombiner =
                    generatorObject
                        .AddComponent<UMAIncrementalMeshCombiner>();

                Type toolbarActionsType =
                    typeof(UMAMeshCombinerSwitcherWindow)
                        .Assembly.GetType(
                            "UMA.Editors.UMAToolbarActions");
                Assert.NotNull(toolbarActionsType);
                MethodInfo getName =
                    toolbarActionsType.GetMethod(
                        "GetCurrentCombinerName",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);
                Assert.NotNull(getName);
                Assert.AreEqual(
                    "Incremental",
                    getName.Invoke(
                        null,
                        new object[] { generator }));
            }
            finally
            {
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        generatorObject);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("CombinerSwitcher")]
        public void ToolbarUsesGeneratorParmsWhenSceneGeneratorIsMissing()
        {
            GameObject generatorParmsObject = null;
            try
            {
                generatorParmsObject =
                    new GameObject("GeneratorParms");
                generatorParmsObject.SetActive(false);

                UMAGeneratorOverride generatorParms =
                    generatorParmsObject.AddComponent<UMAGeneratorOverride>();
                generatorParms.meshCombiner =
                    generatorParmsObject.AddComponent<UMADefaultMeshCombiner>();

                Type toolbarActionsType =
                    typeof(UMAMeshCombinerSwitcherWindow)
                        .Assembly.GetType(
                            "UMA.Editors.UMAToolbarActions");
                Assert.NotNull(toolbarActionsType);

                MethodInfo getName =
                    toolbarActionsType.GetMethod(
                        "GetCurrentCombinerNameForTargets",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);
                Assert.NotNull(getName);
                Assert.AreEqual(
                    "Default",
                    getName.Invoke(
                        null,
                        new object[] { null, generatorParms }));

                MethodInfo useCombiner =
                    toolbarActionsType.GetMethod(
                        "UseMeshCombinerForTargets",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);
                Assert.NotNull(useCombiner);
                useCombiner
                    .MakeGenericMethod(typeof(UMAIncrementalMeshCombiner))
                    .Invoke(
                        null,
                        new object[] { null, generatorParms });

                Assert.IsInstanceOf<UMAIncrementalMeshCombiner>(
                    generatorParms.meshCombiner);
                Assert.AreSame(
                    generatorParmsObject.transform,
                    generatorParms.meshCombiner.transform.parent);
            }
            finally
            {
                if (generatorParmsObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        generatorParmsObject);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void NewDirtyRequestCancelsStaleOperationAndRebuildsLatestRequest()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Restart scheduler generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 3;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Restart scheduler UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);
                generator.Work();

                FakeMeshCombineOperation staleOperation =
                    combiner.lastOperation;
                Assert.AreEqual(1, staleOperation.completedSteps);

                data.isMeshDirty = true;
                generator.addDirtyUMA(data);
                Assert.AreEqual(
                    1,
                    generator.QueueSize(),
                    "Re-dirtying the active UMA must not duplicate its queue entry.");

                generator.Work();
                Assert.AreEqual(1, staleOperation.cancelCalls);
                Assert.IsTrue(staleOperation.disposed);
                Assert.AreEqual(1, generator.QueueSize());
                Assert.IsTrue(data.isMeshDirty);

                generator.Work();
                generator.Work();
                generator.Work();

                Assert.AreEqual(2, combiner.beginCalls);
                Assert.AreNotSame(staleOperation, combiner.lastOperation);
                Assert.AreEqual(
                    UMAMeshCombineStatus.Completed,
                    combiner.lastOperation.Status);
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsFalse(data.isMeshDirty);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void DirtyRequestFromCompletionEventRemainsQueued()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Event restart generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 1;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Event restart UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                bool requestedAgain = false;
                data.OnCharacterCreated += createdData =>
                {
                    if (requestedAgain)
                    {
                        return;
                    }
                    requestedAgain = true;
                    createdData.isMeshDirty = true;
                    generator.addDirtyUMA(createdData);
                };

                generator.addDirtyUMA(data);
                generator.Work();

                Assert.IsTrue(requestedAgain);
                Assert.AreEqual(1, generator.QueueSize());
                Assert.IsTrue(data.dirty);
                Assert.IsTrue(data.isMeshDirty);

                generator.Work();

                Assert.AreEqual(2, combiner.beginCalls);
                Assert.AreEqual(0, generator.QueueSize());
                Assert.IsFalse(data.dirty);
                Assert.IsFalse(data.isMeshDirty);
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void CancellingStaleOperationWaitsForOutstandingWorkBeforeDispose()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Pending cancellation generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 3;
                combiner.holdCancellationUntilReleased = true;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Pending cancellation UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);
                generator.Work();
                FakeMeshCombineOperation staleOperation =
                    combiner.lastOperation;

                data.isMeshDirty = true;
                generator.addDirtyUMA(data);
                generator.Work();

                Assert.AreEqual(1, staleOperation.cancelCalls);
                Assert.IsFalse(
                    staleOperation.disposed,
                    "Outstanding worker/native work must reach a terminal state before disposal.");
                Assert.AreEqual(1, generator.QueueSize());

                staleOperation.releaseCancellation = true;
                generator.Work();

                Assert.IsTrue(staleOperation.disposed);
                Assert.AreEqual(1, generator.QueueSize());
                Assert.AreEqual(1, combiner.beginCalls);

                generator.Work();
                generator.Work();
                generator.Work();
                Assert.AreEqual(2, combiner.beginCalls);
                Assert.AreEqual(0, generator.QueueSize());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        public void ChangingCombinerRestartsActiveMultiStepBuild()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            try
            {
                generatorObject = new GameObject("Combiner switch generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var first =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                var second =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                first.stepsToComplete = 3;
                second.stepsToComplete = 1;
                ConfigureGenerator(generator, first);
                second.onStep = generator.AdvanceOneMillisecond;

                avatarObject = new GameObject("Combiner switch UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                generator.addDirtyUMA(data);
                generator.Work();
                FakeMeshCombineOperation staleOperation =
                    first.lastOperation;

                generator.meshCombiner = second;
                generator.Work();
                Assert.AreEqual(1, staleOperation.cancelCalls);
                Assert.IsTrue(staleOperation.disposed);
                Assert.AreEqual(1, generator.QueueSize());

                generator.Work();
                Assert.AreEqual(1, second.beginCalls);
                Assert.AreEqual(0, generator.QueueSize());
            }
            finally
            {
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatorObject);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(race);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("GeneratorScheduler")]
        [Category("IncrementalRendererPreservation")]
        public void TexturedRebuildKeepsPreviousRendererResourcesUntilRollback()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            GameObject rendererObject = null;
            RaceData race = null;
            UMAMaterial umaMaterial = null;
            TextureMerge textureMerge = null;
            Material previousMaterial = null;
            Texture2D previousTexture = null;
            Mesh previousMesh = null;
            try
            {
                generatorObject =
                    new GameObject("Renderer preservation generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                textureMerge =
                    ScriptableObject.CreateInstance<TextureMerge>();
                generator.textureMerge = textureMerge;
                combiner.stepsToComplete = 3;
                ConfigureGenerator(generator, combiner);

                avatarObject =
                    new GameObject("Renderer preservation UMA");
                UMAData data = CreateUmaData(avatarObject, out race);

                rendererObject =
                    new GameObject("Previous live renderer");
                rendererObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                var previousRenderer =
                    rendererObject.AddComponent<SkinnedMeshRenderer>();
                previousMesh = new Mesh { name = "Previous live mesh" };
                previousRenderer.sharedMesh = previousMesh;
                previousRenderer.rootBone = avatarObject.transform;

                Shader shader =
                    Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                previousMaterial = new Material(shader);
                previousTexture = new Texture2D(2, 2);
                previousMaterial.mainTexture = previousTexture;
                previousRenderer.sharedMaterial = previousMaterial;

                umaMaterial =
                    ScriptableObject.CreateInstance<UMAMaterial>();
                umaMaterial.materialType =
                    UMAMaterial.MaterialType.Atlas;
                umaMaterial.material = previousMaterial;
                var previousGeneratedMaterial =
                    new UMAData.GeneratedMaterial
                    {
                        umaMaterial = umaMaterial,
                        material = previousMaterial,
                        resultingAtlasList =
                            new Texture[] { previousTexture },
                        rendererAsset = null,
                        skinnedMeshRenderer = previousRenderer
                    };
                var previousGeneratedMaterials =
                    new UMAData.GeneratedMaterials();
                previousGeneratedMaterials.materials.Add(
                    previousGeneratedMaterial);
                previousGeneratedMaterials.rendererAssets.Add(null);

                data.generatedMaterials =
                    previousGeneratedMaterials;
                data.SetRenderers(
                    new[] { previousRenderer });
                data.SetRendererAssets(
                    new UMARendererAsset[] { null });
                data.isTextureDirty = true;
                data.isMeshDirty = true;
                data.needsMaterialClear = true;

                generator.addDirtyUMA(data);
                generator.Work();

                Assert.AreEqual(1, generator.QueueSize());
                Assert.IsTrue(previousRenderer.enabled);
                Assert.AreSame(
                    previousMesh,
                    previousRenderer.sharedMesh);
                Assert.AreSame(
                    previousMaterial,
                    previousRenderer.sharedMaterial);
                Assert.IsNotNull(previousMaterial);
                Assert.IsNotNull(previousTexture);
                Assert.AreNotSame(
                    previousGeneratedMaterials,
                    data.generatedMaterials,
                    "The in-progress build must use a detached generated-material set.");

                generator.removeUMA(data);
                generator.Work();

                Assert.IsTrue(generator.IsIdle());
                Assert.AreSame(
                    previousGeneratedMaterials,
                    data.generatedMaterials,
                    "Cancellation must restore the generated materials used by the visible renderer.");
                Assert.AreSame(
                    previousMaterial,
                    previousRenderer.sharedMaterial);
                Assert.IsNotNull(previousTexture);
                Assert.IsTrue(data.needsMaterialClear);
            }
            finally
            {
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        generatorObject);
                }
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        avatarObject);
                }
                if (previousMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousMesh);
                }
                if (previousMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousMaterial);
                }
                if (previousTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousTexture);
                }
                if (umaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        umaMaterial);
                }
                if (textureMerge != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        textureMerge);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        race);
                }
            }
        }

#if UMA_BURSTCOMPILE
        // "initial": the shape change is requested up front.
        // "characterBegun": a build callback marks the shape dirty after validation.
        // "preprocess": the mesh combiner promotes the shape change (as bone baking does).
        // The later two used to run the pending material clear before the rebuild was known.
        [TestCase("initial")] [TestCase("characterBegun")] [TestCase("preprocess")]
        [Category("UMA")]
        [Category("MultiStepMeshCombiner")]
        [Category("IncrementalRendererPreservation")]
        public void NormalsPromotedMeshRebuildPreservesVisibleMaterials(string promotion)
        {
            // A texture + shape update (no mesh change) on an unreadable mesh whose
            // renderer recalculates normals is promoted to a multi-frame mesh
            // rebuild. The visible materials must be preserved for that rebuild.
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            RaceData race = null;
            UMAMaterial umaMaterial = null;
            TextureMerge textureMerge = null;
            UMARendererAsset asset = null;
            Material previousMaterial = null;
            Texture2D previousTexture = null;
            Mesh previousMesh = null;
            try
            {
                generatorObject = new GameObject("Normals preservation generator");
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                textureMerge = ScriptableObject.CreateInstance<TextureMerge>();
                generator.textureMerge = textureMerge;
                combiner.stepsToComplete = 3;
                ConfigureGenerator(generator, combiner);

                avatarObject = new GameObject("Normals preservation UMA");
                UMAData data = CreateUmaData(avatarObject, out race);
                asset = ScriptableObject.CreateInstance<UMARendererAsset>();
                typeof(UMARendererAsset).GetField("_RecalculateNormals", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(asset, true);
                data.defaultRendererAsset = asset;

                var rendererObject = new GameObject("Previous live renderer");
                rendererObject.transform.SetParent(avatarObject.transform, false);
                var previousRenderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                previousMesh = new Mesh { name = "Previous unreadable mesh" };
                previousMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                previousMesh.triangles = new[] { 0, 1, 2 };
                if (promotion == "preprocess")
                {
                    // Readable, so only the combiner (not normals) promotes the rebuild.
                    combiner.onPreprocess = current => current.isMeshDirty |= current.isShapeDirty;
                }
                else
                {
                    previousMesh.UploadMeshData(true);
                    Assert.IsFalse(previousMesh.isReadable);
                }
                previousRenderer.sharedMesh = previousMesh;
                previousRenderer.rootBone = avatarObject.transform;

                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                previousMaterial = new Material(shader);
                previousTexture = new Texture2D(2, 2);
                previousMaterial.mainTexture = previousTexture;
                previousRenderer.sharedMaterial = previousMaterial;

                umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
                umaMaterial.materialType = UMAMaterial.MaterialType.Atlas;
                umaMaterial.material = previousMaterial;
                var previousGeneratedMaterials = new UMAData.GeneratedMaterials();
                previousGeneratedMaterials.materials.Add(new UMAData.GeneratedMaterial
                {
                    umaMaterial = umaMaterial,
                    material = previousMaterial,
                    resultingAtlasList = new Texture[] { previousTexture },
                    skinnedMeshRenderer = previousRenderer
                });
                previousGeneratedMaterials.rendererAssets.Add(null);
                data.generatedMaterials = previousGeneratedMaterials;
                data.SetRenderers(new[] { previousRenderer });
                data.SetRendererAssets(new[] { asset });
                bool late = promotion != "initial";
                data.isTextureDirty = true;
                data.isShapeDirty = promotion != "characterBegun";
                data.isMeshDirty = false;
                // A pending clear is destructive; it must wait for the final preservation decision.
                data.needsMaterialClear = late;
                if (promotion == "characterBegun")
                {
                    data.OnCharacterBegun += begun => begun.isShapeDirty = true;
                }

                generator.addDirtyUMA(data);
                generator.Work();

                Assert.AreEqual(1, combiner.beginCalls, "The promoted mesh rebuild must start.");
                Assert.AreEqual(1, generator.QueueSize(), "The mesh rebuild spans several frames.");
                Assert.IsTrue(previousMaterial != null, "The visible material must survive while the new mesh is pending.");
                Assert.IsTrue(previousTexture != null, "The visible atlas must survive while the new mesh is pending.");
                Assert.AreSame(previousMaterial, previousRenderer.sharedMaterial);
                Assert.AreNotSame(previousGeneratedMaterials, data.generatedMaterials,
                    "The in-progress build must use a detached generated-material set.");

                generator.removeUMA(data);
                generator.Work();

                Assert.IsTrue(generator.IsIdle());
                Assert.AreSame(previousGeneratedMaterials, data.generatedMaterials,
                    "Cancellation must restore the generated materials used by the visible renderer.");
                Assert.IsTrue(previousMaterial != null);
                Assert.IsTrue(previousTexture != null);
                Assert.AreEqual(late, data.needsMaterialClear, "Rollback must restore the pending material clear.");
            }
            finally
            {
                if (generatorObject != null) UnityEngine.Object.DestroyImmediate(generatorObject);
                if (avatarObject != null) UnityEngine.Object.DestroyImmediate(avatarObject);
                if (previousMesh != null) UnityEngine.Object.DestroyImmediate(previousMesh);
                if (previousMaterial != null) UnityEngine.Object.DestroyImmediate(previousMaterial);
                if (previousTexture != null) UnityEngine.Object.DestroyImmediate(previousTexture);
                if (umaMaterial != null) UnityEngine.Object.DestroyImmediate(umaMaterial);
                if (textureMerge != null) UnityEngine.Object.DestroyImmediate(textureMerge);
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                if (race != null) UnityEngine.Object.DestroyImmediate(race);
            }
        }
#endif

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        [Category("IncrementalRendererPreservation")]
        [Category("IncrementalDestroyCleanup")]
        public void DestroyingActiveUmaCleansCurrentAndPendingGeneratedAssets()
        {
            GameObject generatorObject = null;
            GameObject avatarObject = null;
            GameObject rendererObject = null;
            RaceData race = null;
            UMAMaterial umaMaterial = null;
            TextureMerge textureMerge = null;
            Material previousMaterial = null;
            Texture2D previousTexture = null;
            Mesh previousMesh = null;
            Material pendingMaterial = null;
            Texture2D pendingTexture = null;
            UMAAssetIndexer indexer = null;
            UMAGenerator originalGenerator = null;
            try
            {
                generatorObject =
                    new GameObject("Destroy cleanup generator");
                var generator =
                    generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner =
                    generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                textureMerge =
                    ScriptableObject.CreateInstance<TextureMerge>();
                generator.textureMerge = textureMerge;
                combiner.stepsToComplete = 5;
                ConfigureGenerator(generator, combiner);

                avatarObject =
                    new GameObject("Destroy cleanup UMA");
                UMAData data = CreateUmaData(avatarObject, out race);

                rendererObject =
                    new GameObject("Current live renderer");
                rendererObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                var previousRenderer =
                    rendererObject.AddComponent<SkinnedMeshRenderer>();
                previousMesh = new Mesh { name = "Current live mesh" };
                previousRenderer.sharedMesh = previousMesh;
                previousRenderer.rootBone = avatarObject.transform;

                Shader shader =
                    Shader.Find("Hidden/InternalErrorShader");
                Assert.NotNull(shader);
                previousMaterial = new Material(shader);
                previousTexture = new Texture2D(2, 2);
                previousMaterial.mainTexture = previousTexture;
                previousRenderer.sharedMaterial = previousMaterial;

                umaMaterial =
                    ScriptableObject.CreateInstance<UMAMaterial>();
                umaMaterial.materialType =
                    UMAMaterial.MaterialType.Atlas;
                umaMaterial.material = previousMaterial;
                var previousGeneratedMaterial =
                    new UMAData.GeneratedMaterial
                    {
                        umaMaterial = umaMaterial,
                        material = previousMaterial,
                        resultingAtlasList =
                            new Texture[] { previousTexture },
                        skinnedMeshRenderer = previousRenderer
                    };
                var previousGeneratedMaterials =
                    new UMAData.GeneratedMaterials();
                previousGeneratedMaterials.materials.Add(
                    previousGeneratedMaterial);

                data.generatedMaterials =
                    previousGeneratedMaterials;
                data.SetRenderers(
                    new[] { previousRenderer });
                data.SetRendererAssets(
                    new UMARendererAsset[] { null });
                data.isTextureDirty = true;
                data.isMeshDirty = true;
                data.needsMaterialClear = true;

                generator.addDirtyUMA(data);
                generator.Work();

                Assert.AreNotSame(
                    previousGeneratedMaterials,
                    data.generatedMaterials);
                pendingMaterial = new Material(shader);
                pendingTexture = new Texture2D(2, 2);
                pendingMaterial.mainTexture = pendingTexture;
                data.generatedMaterials.materials.Add(
                    new UMAData.GeneratedMaterial
                    {
                        umaMaterial = umaMaterial,
                        material = pendingMaterial,
                        resultingAtlasList =
                            new Texture[] { pendingTexture }
                    });

                indexer = UMAAssetIndexer.Instance;
                Assert.NotNull(indexer);
                originalGenerator = indexer.generator;
                indexer.generator = generator;

                FakeMeshCombineOperation operation =
                    combiner.lastOperation;
                MethodInfo onDestroy = typeof(UMAData).GetMethod(
                    "OnDestroy",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(onDestroy);
                onDestroy.Invoke(data, null);
                UnityEngine.Object.DestroyImmediate(avatarObject);
                avatarObject = null;
                rendererObject = null;

                Assert.AreEqual(
                    1,
                    operation.cancelCalls,
                    "UMAData destruction must notify and cancel the active operation.");
                Assert.IsTrue(
                    previousMesh == null,
                    "The mesh used by the current renderer must be destroyed.");
                Assert.IsTrue(
                    previousMaterial == null,
                    "The current generated material must be destroyed.");
                Assert.IsTrue(
                    previousTexture == null,
                    "The current generated atlas must be destroyed.");
                Assert.IsTrue(
                    pendingMaterial == null,
                    "The pending generated material must be destroyed.");
                Assert.IsTrue(
                    pendingTexture == null,
                    "The pending generated atlas must be destroyed.");

                generator.Work();

                Assert.IsTrue(operation.disposed);
                Assert.IsTrue(generator.IsIdle());
            }
            finally
            {
                if (indexer != null)
                {
                    indexer.generator = originalGenerator;
                }
                if (avatarObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        avatarObject);
                }
                if (generatorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        generatorObject);
                }
                if (previousMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousMesh);
                }
                if (previousMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousMaterial);
                }
                if (previousTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        previousTexture);
                }
                if (pendingMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        pendingMaterial);
                }
                if (pendingTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        pendingTexture);
                }
                if (umaMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        umaMaterial);
                }
                if (textureMerge != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        textureMerge);
                }
                if (race != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        race);
                }
            }
        }

#if UMA_BURSTCOMPILE
        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void NormalsUseFinalBlendshapeWeightsBeforeDiscardingCpuData(bool multiStep, bool nonReadable)
        {
            var generatorObject = new GameObject("Normals timing generator");
            var avatarObject = new GameObject("Normals timing avatar");
            var meshes = new List<Mesh>();
            RaceData race = null;
            UMARendererAsset asset = null;
            try
            {
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                ConfigureGenerator(generator, combiner);
                var data = CreateUmaData(avatarObject, out race);
                race.useNewDNA = false;
                asset = ScriptableObject.CreateInstance<UMARendererAsset>();
                typeof(UMARendererAsset).GetField("_RecalculateNormals", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(asset, true);
                data.defaultRendererAsset = asset;
                data.markNotReadable = nonReadable;
                data.isShapeDirty = true;
                generator.autoSetRaceBlendshapes = true;
                typeof(UMAGeneratorBuiltin).GetField("raceNames", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(generator, new HashSet<string> { race.raceName });

                var rendererObject = new GameObject("Normals timing renderer");
                rendererObject.transform.SetParent(avatarObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                data.SetRenderers(new[] { renderer });
                data.SetRendererAssets(new[] { asset });
                var upload = typeof(UMARendererAsset).GetMethod("UploadGeneratedMesh", BindingFlags.Static | BindingFlags.NonPublic);
                combiner.onStep = () =>
                {
                    var mesh = new Mesh();
                    meshes.Add(mesh);
                    mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                    mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                    mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
                    mesh.triangles = new[] { 0, 1, 2 };
                    mesh.AddBlendShapeFrame("DNA", 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, null, null);
                    // Race blendshapes currently use weight 1, so this contributes one unit at that weight.
                    mesh.AddBlendShapeFrame(race.raceName, 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward * 100 }, null, null);
                    renderer.sharedMesh = mesh;
                    upload.Invoke(null, new object[] { data, renderer, null });
                    generator.AdvanceOneMillisecond();
                };
                Vector3[] observedNormals = null;
                SetNormalsObserver((smr, normals) => { if (smr == renderer) observedNormals = normals; });
                int bodyCalls = 0;
                float dnaWeight = 100;
                generator.updateBody = current =>
                {
                    bodyCalls++;
                    Assert.IsTrue(renderer.sharedMesh.isReadable, "CPU data must survive until DNA has applied its weights.");
                    renderer.SetBlendShapeWeight(0, dnaWeight);
                };

                void Build()
                {
                    observedNormals = null;
                    if (multiStep)
                    {
                        generator.addDirtyUMA(data);
                        for (int i = 0; i < 20 && !generator.IsIdle(); i++) generator.Work();
                        Assert.IsTrue(generator.IsIdle());
                    }
                    else Assert.IsTrue(generator.GenerateSingleUMA(data, false));
                }
                void CheckResult(float height)
                {
                    Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(dnaWeight));
                    Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(1));
                    Assert.That(renderer.sharedMesh.isReadable, Is.EqualTo(!nonReadable));
                    // The CPU copy may be gone, so check what was calculated before the final upload.
                    Assert.NotNull(observedNormals, "Normals must be recalculated for every build.");
                    var expectedNormal = new Vector3(0, -height, 1).normalized;
                    foreach (var normal in observedNormals)
                        Assert.That(Vector3.Distance(normal, expectedNormal), Is.LessThan(.0001f));
                    if (!nonReadable)
                    {
                        var expected = new Vector3(0, -height, 1).normalized;
                        foreach (var normal in renderer.sharedMesh.normals)
                            Assert.That(Vector3.Distance(normal, expected), Is.LessThan(.0001f));
                        Assert.That(renderer.sharedMesh.vertices[2], Is.EqualTo(Vector3.up), "Only normals should be baked.");
                    }
                }

                Build();
                Assert.That(bodyCalls, Is.EqualTo(1));
                CheckResult(2);

                // A later DNA-only update must rebuild unreadable geometry, but can reuse readable geometry.
                dnaWeight = 0;
                data.isShapeDirty = true;
                data.isMeshDirty = false;
                data.dirty = true;
                Build();
                Assert.That(bodyCalls, Is.EqualTo(2));
                Assert.That(combiner.beginCalls, Is.EqualTo(nonReadable ? 2 : 1));
                CheckResult(1);
            }
            finally
            {
                SetNormalsObserver(null);
                UnityEngine.Object.DestroyImmediate(avatarObject);
                UnityEngine.Object.DestroyImmediate(generatorObject);
                foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                if (race != null) UnityEngine.Object.DestroyImmediate(race);
            }
        }

        private static void SetNormalsObserver(Action<SkinnedMeshRenderer, Vector3[]> observer)
        {
            typeof(UMARendererAsset).GetField("NormalsRecalculatedForTests", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, observer);
        }

        private static bool IsMeshUploadDeferred(UMAData data) =>
            (bool)typeof(UMAData).GetField("deferRendererMeshUpload", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(data);

        // Deferred normals must survive a restart (using the newest weights), leave no
        // recalculation behind after cancellation, and always restore the deferral
        // scope, including when the mesh combiner throws.
        [TestCase("restart", true)] [TestCase("cancel", true)]
        [TestCase("failure", true)] [TestCase("failure", false)]
        public void DeferredNormalsHandleRestartCancellationAndFailure(string scenario, bool multiStep)
        {
            var generatorObject = new GameObject("Normals lifecycle generator");
            var avatarObject = new GameObject("Normals lifecycle avatar");
            var meshes = new List<Mesh>();
            RaceData race = null;
            UMARendererAsset asset = null;
            try
            {
                var generator = generatorObject.AddComponent<SchedulerTestGenerator>();
                var combiner = generatorObject.AddComponent<FakeMultiStepMeshCombiner>();
                ConfigureGenerator(generator, combiner);
                combiner.stepsToComplete = 2;
                int injectedFailures = 0;
                var data = CreateUmaData(avatarObject, out race);
                race.useNewDNA = false;
                asset = ScriptableObject.CreateInstance<UMARendererAsset>();
                typeof(UMARendererAsset).GetField("_RecalculateNormals", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(asset, true);
                data.defaultRendererAsset = asset;
                data.markNotReadable = true;
                data.isShapeDirty = true;

                var rendererObject = new GameObject("Normals lifecycle renderer");
                rendererObject.transform.SetParent(avatarObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                data.SetRenderers(new[] { renderer });
                data.SetRendererAssets(new[] { asset });
                var upload = typeof(UMARendererAsset).GetMethod("UploadGeneratedMesh", BindingFlags.Static | BindingFlags.NonPublic);
                combiner.onStep = () =>
                {
                    if (scenario == "failure")
                    {
                        // Only after the deferral scope is active, so restoring it is what is tested.
                        Assert.IsTrue(IsMeshUploadDeferred(data), "The failure must happen inside the deferral scope.");
                        injectedFailures++;
                        throw new InvalidOperationException("Simulated combiner failure");
                    }
                    var mesh = new Mesh();
                    meshes.Add(mesh);
                    mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                    mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                    mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
                    mesh.triangles = new[] { 0, 1, 2 };
                    mesh.AddBlendShapeFrame("DNA", 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, null, null);
                    renderer.sharedMesh = mesh;
                    upload.Invoke(null, new object[] { data, renderer, null });
                    generator.AdvanceOneMillisecond();
                };
                float dnaWeight = 100;
                generator.updateBody = current => renderer.SetBlendShapeWeight(0, dnaWeight);
                Vector3[] observedNormals = null;
                int recalculations = 0;
                SetNormalsObserver((smr, normals) => { if (smr == renderer) { observedNormals = normals; recalculations++; } });

                if (scenario == "failure")
                {
                    if (multiStep)
                    {
                        // The scheduler reports the failure and keeps running.
                        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                            new System.Text.RegularExpressions.Regex("Exception while generating UMA .*Simulated combiner failure"));
                        UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,
                            new System.Text.RegularExpressions.Regex("Simulated combiner failure"));
                        generator.addDirtyUMA(data);
                        for (int i = 0; i < 20 && !generator.IsIdle(); i++) generator.Work();
                        Assert.IsTrue(generator.IsIdle(), "A failed build must leave the queue.");
                    }
                    else
                    {
                        // A direct caller receives the combiner's own exception.
                        var thrown = Assert.Throws<InvalidOperationException>(() => generator.GenerateSingleUMA(data, false));
                        Assert.That(thrown.Message, Is.EqualTo("Simulated combiner failure"));
                    }
                    Assert.AreEqual(1, injectedFailures, "The injected combiner failure must be reached exactly once.");
                    Assert.IsFalse(IsMeshUploadDeferred(data), "A failed build must restore the upload scope.");
                    Assert.AreEqual(0, recalculations);
                    return;
                }

                generator.addDirtyUMA(data);
                generator.Work();
                Assert.IsFalse(generator.IsIdle(), "The fake mesh operation spans two frames.");
                Assert.IsFalse(IsMeshUploadDeferred(data), "Deferral is scoped to each combiner step.");

                if (scenario == "cancel")
                {
                    generator.removeUMA(data);
                    for (int i = 0; i < 20 && !generator.IsIdle(); i++) generator.Work();
                    Assert.IsTrue(generator.IsIdle());
                    Assert.IsFalse(IsMeshUploadDeferred(data));
                    Assert.AreEqual(0, recalculations, "A cancelled build must not finalize normals.");
                    return;
                }

                // restart: a newer DNA request arrives while the mesh is still building.
                Assert.AreEqual(1, combiner.operations.Count);
                var firstOperation = combiner.operations[0];
                Assert.AreEqual(1, firstOperation.completedSteps, "The first operation must still be in progress.");
                dnaWeight = 50;
                data.isShapeDirty = true;
                generator.addDirtyUMA(data);
                for (int i = 0; i < 20 && !generator.IsIdle(); i++) generator.Work();
                Assert.IsTrue(generator.IsIdle());
                Assert.AreEqual(2, combiner.beginCalls, "The newer request must start a replacement operation.");
                Assert.That(firstOperation.cancelCalls, Is.GreaterThan(0), "The stale operation must be cancelled.");
                Assert.IsTrue(firstOperation.disposed, "The stale operation must be disposed.");
                Assert.Less(firstOperation.completedSteps, 2, "The stale operation must not complete.");
                Assert.AreEqual(1, recalculations, "Only the replacement build may finalize normals.");
                Assert.IsFalse(IsMeshUploadDeferred(data));
                Assert.NotNull(observedNormals);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(dnaWeight));
                var expected = new Vector3(0, -(dnaWeight / 100f), 1).normalized;
                foreach (var normal in observedNormals)
                    Assert.That(Vector3.Distance(normal, expected), Is.LessThan(.0001f),
                        "The restarted build must use the newest blendshape weights.");
                Assert.IsFalse(renderer.sharedMesh.isReadable, "The final upload must still honor markNotReadable.");
            }
            finally
            {
                SetNormalsObserver(null);
                UnityEngine.Object.DestroyImmediate(avatarObject);
                UnityEngine.Object.DestroyImmediate(generatorObject);
                foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                if (race != null) UnityEngine.Object.DestroyImmediate(race);
            }
        }
#endif

        private static void ConfigureGenerator(
            SchedulerTestGenerator generator,
            UMAMeshCombiner combiner)
        {
            generator.meshCombiner = combiner;
            generator.atlasResolution = 512;
            generator.IterationCount = 1;
            generator.InterFrameDelay = 0;
            generator.MaxMultiStepWorkMilliseconds = 1f;
            generator.processAllPending = false;
            generator.collectGarbage = false;
            generator.garbageCollectionRate = 0;
            generator.autoSetRaceBlendshapes = false;
            if (combiner is FakeMultiStepMeshCombiner multiStepCombiner)
            {
                multiStepCombiner.onStep = generator.AdvanceOneMillisecond;
            }
        }

        private static UMAData CreateUmaData(GameObject gameObject, out RaceData race)
        {
            RaceData sourceRace = UMAAssetIndexer.Instance.GetAsset<RaceData>("Human Female 3.0");
            Assert.NotNull(
                sourceRace,
                "The multi-step generator tests require the indexed HumanFemale30 race.");

            // Tests destroy their fixture race during cleanup, so use a clone rather than the
            // indexed project asset. This retains the real race's TPose and validation settings
            // without allowing a test to mutate or destroy HumanFemale30.
            race = UnityEngine.Object.Instantiate(sourceRace);
            race.name = sourceRace.name;

            var recipe = new UMAData.UMARecipe
            {
                // Scheduler tests do not need real geometry, but UMA recipes must contain at
                // least one valid slot. A placeholder keeps validation honest without making
                // these timing tests load and process the full base race recipe.
                slotDataList = new[]
                {
                    SlotData.CreatePlaceholder(
                        "MultiStepSchedulerPlaceholder",
                        Array.Empty<string>())
                }
            };
            recipe.SetRace(race);

            var data = gameObject.AddComponent<UMAData>();
            data.umaRecipe = recipe;
            data.umaRoot = gameObject;
            data.skeleton = new UMASkeleton(gameObject.transform);
            data.SetRenderers(Array.Empty<SkinnedMeshRenderer>());
            data.rawAvatar = true;
            data.isTextureDirty = false;
            data.isAtlasDirty = false;
            data.isMeshDirty = true;
            data.isShapeDirty = false;
            data.dirty = true;
            return data;
        }
    }

    public sealed class SchedulerTestGenerator : UMAGenerator
    {
        private long timestamp;
        public Action<UMAData> updateBody;

        public override void UpdateUMABody(UMAData data)
        {
            if (updateBody != null) updateBody(data);
            else base.UpdateUMABody(data);
        }

        public override void Awake()
        {
            // Scheduler fixtures do not require TextureMerge, renderer assets,
            // or the generator's runtime memory warm-up.
        }

        public void AdvanceOneMillisecond()
        {
            timestamp++;
        }

        public void SimulateDisable()
        {
            base.OnDisable();
        }

        protected override long GetMultiStepTimestamp()
        {
            return timestamp;
        }

        protected override long GetMultiStepTimestampFrequency()
        {
            return 1000L;
        }
    }

    public sealed class SynchronousSchedulerTestMeshCombiner : UMAMeshCombiner
    {
        public int updateCalls;

        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
            updateCalls++;
        }
    }
}

#endif
