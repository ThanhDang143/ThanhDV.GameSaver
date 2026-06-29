using System;
using System.Threading;

namespace ThanhDV.SaveKeeper.Core
{
    public enum SaveKeeperOperationStatus { Pending, Succeeded, Failed }

    /// <summary>
    /// Mutable backing store for a typeless async operation: status, progress, error, and callbacks.
    /// Captures the SynchronizationContext at construction so completion callbacks fire on the origin thread
    /// (typically Unity main thread), even when the pipeline finishes on a threadpool worker.
    /// </summary>
    internal class SaveKeeperOperationInternal
    {
        /// <summary>Current status of the operation.</summary>
        public SaveKeeperOperationStatus Status { get; set; } = SaveKeeperOperationStatus.Pending;

        /// <summary>Progress from 0.0 to 1.0.</summary>
        public float PercentComplete { get; set; } = 0f;

        /// <summary>The exception that caused failure, if any.</summary>
        public Exception Error { get; set; }

        /// <summary>True once the operation has completed (succeeded or failed).</summary>
        public bool IsDone => Status != SaveKeeperOperationStatus.Pending;

        /// <summary>Raised on completion; passes a public read-only handle to listeners.</summary>
        public event Action<SaveKeeperOperationHandle> Completed;

        /// <summary>Continuation invoked by the C# async state machine to resume after <c>await</c>.</summary>
        public event Action ContinuationAction;

        private readonly SynchronizationContext _capturedContext;

        public SaveKeeperOperationInternal()
        {
            _capturedContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Finalizes the operation and fires callbacks. Idempotent — later calls no-op, preserving the original outcome.
        /// </summary>
        /// <param name="error">Exception that caused failure, or null for success.</param>
        public void Complete(Exception error = null)
        {
            // Idempotent guard: the first call wins. Prevents state from being overwritten
            // and callbacks from firing twice if a pipeline bug or reentrancy triggers a second Complete.
            if (IsDone) return;

            Error = error;
            Status = error == null ? SaveKeeperOperationStatus.Succeeded : SaveKeeperOperationStatus.Failed;
            PercentComplete = 1f;

            if (_capturedContext != null && _capturedContext != SynchronizationContext.Current)
            {
                _capturedContext.Post(_ => InvokeCallbacks(), null);
            }
            else
            {
                InvokeCallbacks();
            }
        }

        private void InvokeCallbacks()
        {
            Completed?.Invoke(new SaveKeeperOperationHandle(this));
            ContinuationAction?.Invoke();

            // Clear events to prevent potential memory leaks after completion
            ContinuationAction = null;
            Completed = null;
        }
    }

    /// <summary>
    /// Typed counterpart of <see cref="SaveKeeperOperationInternal"/> — adds a <see cref="Result"/> for the
    /// operation's payload. Same SynchronizationContext-marshalled callback semantics.
    /// </summary>
    /// <typeparam name="T">The result type of the operation.</typeparam>
    internal class SaveKeeperOperationInternal<T>
    {
        /// <summary>Current status of the operation.</summary>
        public SaveKeeperOperationStatus Status { get; set; } = SaveKeeperOperationStatus.Pending;

        /// <summary>Progress from 0.0 to 1.0.</summary>
        public float PercentComplete { get; set; } = 0f;

        /// <summary>The exception that caused failure, if any.</summary>
        public Exception Error { get; set; }

        /// <summary>The result on successful completion.</summary>
        public T Result { get; set; }

        /// <summary>True once the operation has completed.</summary>
        public bool IsDone => Status != SaveKeeperOperationStatus.Pending;

        /// <summary>Raised on completion; passes a public read-only handle to listeners.</summary>
        public event Action<SaveKeeperOperationHandle<T>> Completed;

        /// <summary>Continuation invoked by the C# async state machine to resume after <c>await</c>.</summary>
        public event Action ContinuationAction;

        private readonly SynchronizationContext _capturedContext;

        public SaveKeeperOperationInternal()
        {
            _capturedContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Finalizes the operation with a result and fires callbacks. Idempotent — later calls no-op.
        /// </summary>
        /// <param name="result">The operation's result value.</param>
        /// <param name="error">Exception that caused failure, or null for success.</param>
        public void Complete(T result, Exception error = null)
        {
            // Idempotent guard: the first call wins. Prevents state from being overwritten
            // and callbacks from firing twice if a pipeline bug or reentrancy triggers a second Complete.
            if (IsDone) return;

            Result = result;
            Error = error;
            Status = error == null ? SaveKeeperOperationStatus.Succeeded : SaveKeeperOperationStatus.Failed;
            PercentComplete = 1f;

            if (_capturedContext != null && _capturedContext != SynchronizationContext.Current)
            {
                _capturedContext.Post(_ => InvokeCallbacks(), null);
            }
            else
            {
                InvokeCallbacks();
            }
        }

        private void InvokeCallbacks()
        {
            Completed?.Invoke(new SaveKeeperOperationHandle<T>(this));
            ContinuationAction?.Invoke();

            // Clear events to prevent potential memory leaks after completion
            ContinuationAction = null;
            Completed = null;
        }
    }
}
