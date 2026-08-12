using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public enum UMAAddressableOperationStatus
    {
        None,
        Succeeded,
        Failed
    }

    /// <summary>
    /// Addressables-neutral operation exposed by UMA runtime APIs. The optional
    /// Addressables integration supplies the native implementation at runtime.
    /// </summary>
    public sealed class UMAAddressableOperation : IEquatable<UMAAddressableOperation>
    {
        private readonly object nativeHandle;
        private readonly Func<bool> isValid;
        private readonly Func<bool> isDone;
        private readonly Func<float> percentComplete;
        private readonly Func<UMAAddressableOperationStatus> status;
        private readonly Func<IList<UnityEngine.Object>> result;
        private readonly Func<Exception> operationException;
        private Action<UMAAddressableOperation> completed;
        private bool completionRaised;

        public UMAAddressableOperation(
            object nativeHandle,
            Func<bool> isValid,
            Func<bool> isDone,
            Func<float> percentComplete,
            Func<UMAAddressableOperationStatus> status,
            Func<IList<UnityEngine.Object>> result,
            Func<Exception> operationException)
        {
            this.nativeHandle = nativeHandle;
            this.isValid = isValid;
            this.isDone = isDone;
            this.percentComplete = percentComplete;
            this.status = status;
            this.result = result;
            this.operationException = operationException;
        }

        public object NativeHandle => nativeHandle;
        public bool IsDone => isDone?.Invoke() ?? true;
        public float PercentComplete => percentComplete?.Invoke() ?? (IsDone ? 1f : 0f);
        public UMAAddressableOperationStatus Status =>
            status?.Invoke() ?? UMAAddressableOperationStatus.Failed;
        public IList<UnityEngine.Object> Result => result?.Invoke();
        public Exception OperationException => operationException?.Invoke();

        public event Action<UMAAddressableOperation> Completed
        {
            add
            {
                completed += value;
                if (!completionRaised && IsDone) completionRaised = true;
                if (completionRaised) value?.Invoke(this);
            }
            remove => completed -= value;
        }

        public bool IsValid()
        {
            return isValid?.Invoke() ?? false;
        }

        public void RaiseCompleted()
        {
            if (completionRaised) return;
            completionRaised = true;
            completed?.Invoke(this);
        }

        public bool Equals(UMAAddressableOperation other)
        {
            return ReferenceEquals(this, other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UMAAddressableOperation);
        }

        public override int GetHashCode()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        internal static UMAAddressableOperation CompletedResult(IList<UnityEngine.Object> value)
        {
            var operation = new UMAAddressableOperation(
                value, () => true, () => true, () => 1f,
                () => UMAAddressableOperationStatus.Succeeded,
                () => value, () => null);
            operation.completionRaised = true;
            return operation;
        }

        internal static UMAAddressableOperation Failed(Exception exception)
        {
            var operation = new UMAAddressableOperation(
                exception, () => true, () => true, () => 1f,
                () => UMAAddressableOperationStatus.Failed,
                () => null, () => exception);
            operation.completionRaised = true;
            return operation;
        }
    }

    public static class UMAAddressablesRuntimeBridge
    {
        public sealed class Provider
        {
            public Func<IReadOnlyList<string>, UMAAddressableOperation> loadAssets;
            public Action<UMAAddressableOperation> release;
        }

        public static Provider Current { private get; set; }
        public static bool IsAvailable => Current != null;

        public static UMAAddressableOperation LoadAssets(IReadOnlyList<string> keys)
        {
            return Current?.loadAssets?.Invoke(keys) ?? UMAAddressableOperation.Failed(
                new InvalidOperationException(
                    "UMA Addressables support is unavailable. Install Addressables and enable UMA_ADDRESSABLES."));
        }

        public static UMAAddressableOperation CreateCompleted(IList<UnityEngine.Object> result)
        {
            return UMAAddressableOperation.CompletedResult(result);
        }

        public static void Release(UMAAddressableOperation operation)
        {
            if (operation == null || !operation.IsValid()) return;
            Current?.release?.Invoke(operation);
        }
    }
}
