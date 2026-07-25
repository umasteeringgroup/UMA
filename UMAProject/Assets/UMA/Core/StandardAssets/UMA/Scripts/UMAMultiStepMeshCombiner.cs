using System;
using System.Diagnostics;

namespace UMA
{
    /// <summary>
    /// Current state returned by one incremental mesh-combine step.
    /// </summary>
    public enum UMAMeshCombineStatus
    {
        /// <summary>
        /// The operation has more work that can be attempted immediately while
        /// the current time slice still has capacity.
        /// </summary>
        InProgress,

        /// <summary>
        /// The operation is waiting for a job, thread, or other asynchronous
        /// dependency. The generator should poll it during a later update
        /// instead of blocking the main thread.
        /// </summary>
        WaitingForAsync,

        /// <summary>
        /// All staged mesh work completed successfully.
        /// </summary>
        Completed,

        /// <summary>
        /// The operation failed. The associated error is available from the
        /// step result and the operation.
        /// </summary>
        Failed,

        /// <summary>
        /// Cancellation completed and all operation-owned resources are safe to
        /// release.
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// Result of advancing a multi-step mesh-combine operation.
    /// </summary>
    public readonly struct UMAMeshCombineStepResult
    {
        /// <summary>
        /// Current operation status.
        /// </summary>
        public UMAMeshCombineStatus Status { get; }

        /// <summary>
        /// Failure associated with this step. This is non-null only when
        /// <see cref="Status"/> is <see cref="UMAMeshCombineStatus.Failed"/>.
        /// </summary>
        public Exception Error { get; }

        /// <summary>
        /// True when no further successful work can be performed by this
        /// operation.
        /// </summary>
        public bool IsTerminal =>
            Status == UMAMeshCombineStatus.Completed ||
            Status == UMAMeshCombineStatus.Failed ||
            Status == UMAMeshCombineStatus.Cancelled;

        /// <summary>
        /// True when the operation can be advanced again immediately if the
        /// current time slice has not expired.
        /// </summary>
        public bool CanContinueImmediately => Status == UMAMeshCombineStatus.InProgress;

        private UMAMeshCombineStepResult(UMAMeshCombineStatus status, Exception error)
        {
            Status = status;
            Error = error;
        }

        public static UMAMeshCombineStepResult InProgress()
        {
            return new UMAMeshCombineStepResult(UMAMeshCombineStatus.InProgress, null);
        }

        public static UMAMeshCombineStepResult WaitingForAsync()
        {
            return new UMAMeshCombineStepResult(UMAMeshCombineStatus.WaitingForAsync, null);
        }

        public static UMAMeshCombineStepResult Completed()
        {
            return new UMAMeshCombineStepResult(UMAMeshCombineStatus.Completed, null);
        }

        public static UMAMeshCombineStepResult Cancelled()
        {
            return new UMAMeshCombineStepResult(UMAMeshCombineStatus.Cancelled, null);
        }

        public static UMAMeshCombineStepResult Failed(Exception error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }
            return new UMAMeshCombineStepResult(UMAMeshCombineStatus.Failed, error);
        }
    }

    /// <summary>
    /// Shared deadline for the main-thread work performed during one generator
    /// update. A value created with zero milliseconds, or the default value,
    /// represents an unlimited time slice.
    /// </summary>
    public readonly struct UMAMeshCombineTimeSlice
    {
        private readonly Func<long> timestampProvider;
        private readonly long timestampFrequency;
        private readonly long deadlineTimestamp;
        private readonly bool hasDeadline;

        /// <summary>
        /// Creates a time slice using <see cref="Stopwatch"/> timestamps.
        /// </summary>
        /// <param name="maximumMilliseconds">
        /// Soft main-thread budget. Zero creates an unlimited time slice.
        /// </param>
        public UMAMeshCombineTimeSlice(double maximumMilliseconds)
            : this(maximumMilliseconds, Stopwatch.GetTimestamp, Stopwatch.Frequency)
        {
        }

        /// <summary>
        /// Creates a time slice using a supplied monotonic timestamp source.
        /// This overload allows deterministic scheduler tests without waiting
        /// for wall-clock time.
        /// </summary>
        /// <param name="maximumMilliseconds">
        /// Soft main-thread budget. Zero creates an unlimited time slice.
        /// </param>
        /// <param name="timestampProvider">Monotonic timestamp provider.</param>
        /// <param name="timestampFrequency">Timestamp ticks per second.</param>
        public UMAMeshCombineTimeSlice(
            double maximumMilliseconds,
            Func<long> timestampProvider,
            long timestampFrequency)
        {
            if (double.IsNaN(maximumMilliseconds) ||
                double.IsInfinity(maximumMilliseconds) ||
                maximumMilliseconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMilliseconds),
                    "The mesh-combine budget must be a finite value greater than or equal to zero.");
            }
            if (timestampProvider == null)
            {
                throw new ArgumentNullException(nameof(timestampProvider));
            }
            if (timestampFrequency <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampFrequency),
                    "Timestamp frequency must be greater than zero.");
            }

            this.timestampProvider = timestampProvider;
            this.timestampFrequency = timestampFrequency;

            if (maximumMilliseconds == 0d)
            {
                deadlineTimestamp = 0L;
                hasDeadline = false;
                return;
            }

            long startTimestamp = timestampProvider();
            double requestedTicks = maximumMilliseconds * timestampFrequency / 1000d;
            long budgetTicks = requestedTicks >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)Math.Ceiling(requestedTicks));

            deadlineTimestamp =
                startTimestamp >= long.MaxValue - budgetTicks
                    ? long.MaxValue
                    : startTimestamp + budgetTicks;
            hasDeadline = true;
        }

        /// <summary>
        /// An unlimited time slice.
        /// </summary>
        public static UMAMeshCombineTimeSlice Unlimited => default;

        /// <summary>
        /// True when this slice has no deadline.
        /// </summary>
        public bool IsUnlimited => !hasDeadline;

        /// <summary>
        /// True when the current timestamp has reached or passed the deadline.
        /// </summary>
        public bool IsExpired =>
            hasDeadline &&
            timestampProvider() >= deadlineTimestamp;

        /// <summary>
        /// Remaining soft main-thread budget in milliseconds. Unlimited slices
        /// return positive infinity.
        /// </summary>
        public double RemainingMilliseconds
        {
            get
            {
                if (!hasDeadline)
                {
                    return double.PositiveInfinity;
                }

                long remainingTicks = deadlineTimestamp - timestampProvider();
                if (remainingTicks <= 0L)
                {
                    return 0d;
                }
                return remainingTicks * 1000d / timestampFrequency;
            }
        }
    }

    /// <summary>
    /// Optional capability implemented by mesh combiners that can parcel their
    /// work across generator updates.
    /// </summary>
    /// <remarks>
    /// This interface does not replace <see cref="UMAMeshCombiner.UpdateUMAMesh"/>.
    /// Existing callers and combiners remain synchronous. A multi-step combiner
    /// must also provide a synchronous implementation of UpdateUMAMesh for
    /// direct and editor-time callers.
    /// </remarks>
    public interface IUMAMultiStepMeshCombiner
    {
        /// <summary>
        /// Begins a self-contained mesh-combine operation.
        /// </summary>
        IUMAMeshCombineOperation BeginUpdateUMAMesh(
            bool updatedAtlas,
            UMAData umaData,
            int atlasResolution);
    }

    /// <summary>
    /// Optional diagnostic detail for a multi-step operation. The generator
    /// samples this immediately before calling Step so timing is attributed to
    /// the atomic unit that actually ran, even when Step advances the visible
    /// operation stage.
    /// </summary>
    public interface IUMAMeshCombineOperationDiagnostics
    {
        /// <summary>
        /// Stable, human-readable category for the next atomic unit of work.
        /// Unlike StageName, this value should avoid per-avatar or per-frame
        /// details so repeated timings can be grouped in generator statistics.
        /// </summary>
        string AtomicStepName { get; }

        /// <summary>
        /// Returns completed nested or worker timing samples that should be
        /// included in generator step statistics. Returns false when no sample
        /// is waiting.
        /// </summary>
        bool TryDequeueCompletedTiming(
            out UMAMeshCombineStepTiming timing);
    }

    public readonly struct UMAMeshCombineStepTiming
    {
        public string StepName { get; }
        public long StopwatchTicks { get; }

        public UMAMeshCombineStepTiming(
            string stepName,
            long stopwatchTicks)
        {
            StepName = stepName;
            StopwatchTicks = stopwatchTicks;
        }
    }

    /// <summary>
    /// Owns the state and temporary resources for one incremental UMA mesh
    /// combination.
    /// </summary>
    public interface IUMAMeshCombineOperation : IDisposable
    {
        /// <summary>
        /// Advances at most one bounded or atomic unit of main-thread work.
        /// The generator may call this repeatedly while it returns InProgress
        /// and the shared time slice has not expired.
        /// </summary>
        UMAMeshCombineStepResult Step(UMAMeshCombineTimeSlice timeSlice);

        /// <summary>
        /// Requests cancellation. This method must be idempotent. If worker
        /// jobs still own resources, cancellation may remain pending until a
        /// later Step can observe their completion safely.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Human-readable current stage for diagnostics and profiling.
        /// </summary>
        string StageName { get; }

        /// <summary>
        /// Approximate progress in the inclusive range zero through one.
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// True while jobs, worker threads, or other asynchronous dependencies
        /// are outstanding.
        /// </summary>
        bool HasPendingJobs { get; }

        /// <summary>
        /// Current operation status.
        /// </summary>
        UMAMeshCombineStatus Status { get; }

        /// <summary>
        /// Failure that caused the operation to enter Failed status.
        /// </summary>
        Exception Error { get; }
    }
}
