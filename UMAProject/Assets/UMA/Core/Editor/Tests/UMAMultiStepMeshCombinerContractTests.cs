#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAMultiStepMeshCombinerContractTests
    {
        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void ExistingSynchronousMeshCombinerContractIsUnchanged()
        {
            MethodInfo method = typeof(UMAMeshCombiner).GetMethod(
                nameof(UMAMeshCombiner.UpdateUMAMesh),
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(method);
            Assert.IsTrue(method.IsAbstract);
            Assert.AreEqual(typeof(void), method.ReturnType);

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual(typeof(bool), parameters[0].ParameterType);
            Assert.AreEqual(typeof(UMAData), parameters[1].ParameterType);
            Assert.AreEqual(typeof(int), parameters[2].ParameterType);

            Assert.IsFalse(typeof(IUMAMultiStepMeshCombiner).IsAssignableFrom(typeof(UMADefaultMeshCombiner)));
            Assert.IsFalse(typeof(IUMAMultiStepMeshCombiner).IsAssignableFrom(typeof(UMAJobifiedMeshCombiner)));
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void TimeSliceUsesInjectedMonotonicClockDeterministically()
        {
            long timestamp = 100L;
            var slice = new UMAMeshCombineTimeSlice(
                2d,
                () => timestamp,
                1000L);

            Assert.IsFalse(slice.IsUnlimited);
            Assert.IsFalse(slice.IsExpired);
            Assert.That(slice.RemainingMilliseconds, Is.EqualTo(2d).Within(1e-9d));

            timestamp = 101L;
            Assert.IsFalse(slice.IsExpired);
            Assert.That(slice.RemainingMilliseconds, Is.EqualTo(1d).Within(1e-9d));

            timestamp = 102L;
            Assert.IsTrue(slice.IsExpired);
            Assert.AreEqual(0d, slice.RemainingMilliseconds);
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void ZeroAndDefaultTimeSlicesAreUnlimited()
        {
            long timestamp = 10L;
            var zeroBudget = new UMAMeshCombineTimeSlice(0d, () => timestamp, 1000L);
            UMAMeshCombineTimeSlice defaultSlice = default;

            timestamp = long.MaxValue;
            Assert.IsTrue(zeroBudget.IsUnlimited);
            Assert.IsFalse(zeroBudget.IsExpired);
            Assert.AreEqual(double.PositiveInfinity, zeroBudget.RemainingMilliseconds);
            Assert.IsTrue(defaultSlice.IsUnlimited);
            Assert.IsFalse(defaultSlice.IsExpired);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void TimeSliceRejectsInvalidBudgets(double budget)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new UMAMeshCombineTimeSlice(budget, () => 0L, 1000L));
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void StepResultsExposeContinuationAndTerminalSemantics()
        {
            UMAMeshCombineStepResult inProgress = UMAMeshCombineStepResult.InProgress();
            UMAMeshCombineStepResult waiting = UMAMeshCombineStepResult.WaitingForAsync();
            UMAMeshCombineStepResult completed = UMAMeshCombineStepResult.Completed();
            UMAMeshCombineStepResult cancelled = UMAMeshCombineStepResult.Cancelled();
            var failure = new InvalidOperationException("fixture failure");
            UMAMeshCombineStepResult failed = UMAMeshCombineStepResult.Failed(failure);

            Assert.IsTrue(inProgress.CanContinueImmediately);
            Assert.IsFalse(inProgress.IsTerminal);
            Assert.IsFalse(waiting.CanContinueImmediately);
            Assert.IsFalse(waiting.IsTerminal);
            Assert.IsTrue(completed.IsTerminal);
            Assert.IsTrue(cancelled.IsTerminal);
            Assert.IsTrue(failed.IsTerminal);
            Assert.AreSame(failure, failed.Error);
            Assert.Throws<ArgumentNullException>(() => UMAMeshCombineStepResult.Failed(null));
        }

        [Test]
        [Category("UMA")]
        [Category("MeshCombiner")]
        [Category("MultiStepMeshCombiner")]
        public void OptionalCombinerCanResumeAndCancelWithoutAffectingSynchronousApi()
        {
            var gameObject = new GameObject("Multi-step combiner contract fixture");
            try
            {
                var umaData = gameObject.AddComponent<UMAData>();
                var combiner = gameObject.AddComponent<FakeMultiStepMeshCombiner>();
                combiner.stepsToComplete = 3;

                using (IUMAMeshCombineOperation operation =
                       combiner.BeginUpdateUMAMesh(false, umaData, 512))
                {
                    Assert.AreEqual(UMAMeshCombineStatus.InProgress, operation.Status);
                    Assert.AreEqual("Ready", operation.StageName);

                    UMAMeshCombineStepResult first = operation.Step(UMAMeshCombineTimeSlice.Unlimited);
                    Assert.AreEqual(UMAMeshCombineStatus.InProgress, first.Status);
                    Assert.AreEqual(1, combiner.lastOperation.completedSteps);

                    operation.Cancel();
                    operation.Cancel();
                    UMAMeshCombineStepResult cancelled = operation.Step(UMAMeshCombineTimeSlice.Unlimited);
                    Assert.AreEqual(UMAMeshCombineStatus.Cancelled, cancelled.Status);
                    Assert.AreEqual(UMAMeshCombineStatus.Cancelled, operation.Status);
                    Assert.AreEqual(1, combiner.lastOperation.cancelCalls);
                }

                combiner.UpdateUMAMesh(false, umaData, 512);
                Assert.AreEqual(3, combiner.lastOperation.completedSteps);
                Assert.AreEqual(UMAMeshCombineStatus.Completed, combiner.lastOperation.Status);
                Assert.IsTrue(combiner.lastOperation.disposed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }

    public sealed class FakeMultiStepMeshCombiner : UMAMeshCombiner, IUMAMultiStepMeshCombiner
    {
        public int stepsToComplete = 1;
        public Action onStep;
        public bool waitOnceAfterFirstStep;
        public bool holdCancellationUntilReleased;
        public FakeMeshCombineOperation lastOperation;
        public readonly List<FakeMeshCombineOperation> operations =
            new List<FakeMeshCombineOperation>();
        public int beginCalls;

        public IUMAMeshCombineOperation BeginUpdateUMAMesh(
            bool updatedAtlas,
            UMAData umaData,
            int atlasResolution)
        {
            if (umaData == null)
            {
                throw new ArgumentNullException(nameof(umaData));
            }
            if (atlasResolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(atlasResolution));
            }

            lastOperation = new FakeMeshCombineOperation(
                Math.Max(1, stepsToComplete),
                onStep,
                waitOnceAfterFirstStep,
                holdCancellationUntilReleased);
            operations.Add(lastOperation);
            beginCalls++;
            return lastOperation;
        }

        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
            using (IUMAMeshCombineOperation operation =
                   BeginUpdateUMAMesh(updatedAtlas, umaData, atlasResolution))
            {
                while (true)
                {
                    UMAMeshCombineStepResult result =
                        operation.Step(UMAMeshCombineTimeSlice.Unlimited);
                    if (result.Status == UMAMeshCombineStatus.Completed)
                    {
                        return;
                    }
                    if (result.Status == UMAMeshCombineStatus.Failed)
                    {
                        throw result.Error ?? operation.Error ??
                              new InvalidOperationException("The fake multi-step operation failed.");
                    }
                    if (result.Status == UMAMeshCombineStatus.Cancelled)
                    {
                        throw new OperationCanceledException();
                    }
                }
            }
        }
    }

    public sealed class FakeMeshCombineOperation : IUMAMeshCombineOperation
    {
        private readonly int stepsToComplete;
        private readonly Action onStep;
        private readonly bool waitOnceAfterFirstStep;
        private readonly bool holdCancellationUntilReleased;
        private bool cancellationRequested;
        private bool waitReturned;

        public int completedSteps;
        public int cancelCalls;
        public bool disposed;
        public bool releaseCancellation;

        public FakeMeshCombineOperation(
            int stepsToComplete,
            Action onStep = null,
            bool waitOnceAfterFirstStep = false,
            bool holdCancellationUntilReleased = false)
        {
            this.stepsToComplete = stepsToComplete;
            this.onStep = onStep;
            this.waitOnceAfterFirstStep = waitOnceAfterFirstStep;
            this.holdCancellationUntilReleased =
                holdCancellationUntilReleased;
            Status = UMAMeshCombineStatus.InProgress;
        }

        public string StageName
        {
            get
            {
                if (Status == UMAMeshCombineStatus.Completed)
                {
                    return "Completed";
                }
                if (Status == UMAMeshCombineStatus.Cancelled)
                {
                    return "Cancelled";
                }
                return completedSteps == 0 ? "Ready" : "Step";
            }
        }

        public float Progress => Mathf.Clamp01(completedSteps / (float)stepsToComplete);
        public bool HasPendingJobs =>
            cancellationRequested &&
            holdCancellationUntilReleased &&
            !releaseCancellation;
        public UMAMeshCombineStatus Status { get; private set; }
        public Exception Error { get; private set; }

        public UMAMeshCombineStepResult Step(UMAMeshCombineTimeSlice timeSlice)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FakeMeshCombineOperation));
            }
            if (Status == UMAMeshCombineStatus.Completed)
            {
                return UMAMeshCombineStepResult.Completed();
            }
            if (Status == UMAMeshCombineStatus.Cancelled)
            {
                return UMAMeshCombineStepResult.Cancelled();
            }
            if (Status == UMAMeshCombineStatus.Failed)
            {
                return UMAMeshCombineStepResult.Failed(Error);
            }
            if (Status == UMAMeshCombineStatus.WaitingForAsync)
            {
                Status = UMAMeshCombineStatus.InProgress;
            }
            if (cancellationRequested)
            {
                if (HasPendingJobs)
                {
                    Status = UMAMeshCombineStatus.WaitingForAsync;
                    return UMAMeshCombineStepResult.WaitingForAsync();
                }
                Status = UMAMeshCombineStatus.Cancelled;
                return UMAMeshCombineStepResult.Cancelled();
            }
            if (timeSlice.IsExpired)
            {
                return UMAMeshCombineStepResult.InProgress();
            }

            completedSteps++;
            onStep?.Invoke();
            if (completedSteps >= stepsToComplete)
            {
                Status = UMAMeshCombineStatus.Completed;
                return UMAMeshCombineStepResult.Completed();
            }
            if (waitOnceAfterFirstStep && !waitReturned && completedSteps == 1)
            {
                waitReturned = true;
                Status = UMAMeshCombineStatus.WaitingForAsync;
                return UMAMeshCombineStepResult.WaitingForAsync();
            }
            return UMAMeshCombineStepResult.InProgress();
        }

        public void Cancel()
        {
            if (cancellationRequested ||
                Status == UMAMeshCombineStatus.Completed ||
                Status == UMAMeshCombineStatus.Cancelled ||
                Status == UMAMeshCombineStatus.Failed)
            {
                return;
            }

            cancellationRequested = true;
            cancelCalls++;
        }

        public void Dispose()
        {
            disposed = true;
        }
    }
}

#endif
