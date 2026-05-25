using System;
using System.Threading;

namespace ThanhDV.SaveKeeper.Core
{
    public enum SaveKeeperOperationStatus { Pending, Succeeded, Failed }

    /// <summary>
    /// Tracks state, progress, and callbacks for typeless async operations.
    /// Captures the SynchronizationContext to marshal callbacks back to their origin (typically Unity main thread),
    /// ensuring callback code safely accesses Unity APIs even when running on threadpool.
    /// </summary>
    internal class SaveKeeperOperationInternal
    {
        /// <summary>
        /// Current status of the operation.
        /// </summary>
        public SaveKeeperOperationStatus Status { get; set; } = SaveKeeperOperationStatus.Pending;

        /// <summary>
        /// Progress of the operation, ranging from 0.0 to 1.0.
        /// </summary>
        public float PercentComplete { get; set; } = 0f;

        /// <summary>
        /// The exception that caused the operation to fail, if any.
        /// </summary>
        public Exception Error { get; set; }

        /// <summary>
        /// Indicates whether the operation has finished executing (either successfully or with an error).
        /// </summary>
        public bool IsDone => Status != SaveKeeperOperationStatus.Pending;

        /// <summary>
        /// Event triggered when the operation completes. Passes a public handle to the listener.
        /// </summary>
        public event Action<SaveKeeperOperationHandle> Completed;

        /// <summary>
        /// Action invoked by the C# async state machine to continue execution after an await.
        /// Used internally by SaveKeeperAwaiter.
        /// </summary>
        public event Action ContinuationAction;

        private readonly SynchronizationContext _capturedContext;

        public SaveKeeperOperationInternal()
        {
            _capturedContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Finalizes the operation, updating its state and triggering all pending callbacks.
        /// Idempotent — second and later calls are no-ops, preserving the original outcome.
        /// </summary>
        /// <param name="error">The exception to set if the operation failed. Null indicates success.</param>
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
    /// Tracks state, progress, and callbacks for typed async operations.
    /// Captures the SynchronizationContext to marshal callbacks back to their origin (typically Unity main thread),
    /// ensuring callback code safely accesses Unity APIs even when running on threadpool.
    /// </summary>
    /// <typeparam name="T">The result type of the operation.</typeparam>
    internal class SaveKeeperOperationInternal<T>
    {
        /// <summary>
        /// Current status of the operation.
        /// </summary>
        public SaveKeeperOperationStatus Status { get; set; } = SaveKeeperOperationStatus.Pending;

        /// <summary>
        /// Progress of the operation, ranging from 0.0 to 1.0.
        /// </summary>
        public float PercentComplete { get; set; } = 0f;

        /// <summary>
        /// The exception that caused the operation to fail, if any.
        /// </summary>
        public Exception Error { get; set; }

        /// <summary>
        /// The result of the operation upon successful completion.
        /// </summary>
        public T Result { get; set; }

        /// <summary>
        /// Indicates whether the operation has finished executing.
        /// </summary>
        public bool IsDone => Status != SaveKeeperOperationStatus.Pending;

        /// <summary>
        /// Event triggered when the operation completes. Passes a public handle to the listener.
        /// </summary>
        public event Action<SaveKeeperOperationHandle<T>> Completed;

        /// <summary>
        /// Action invoked by the C# async state machine to continue execution after an await.
        /// </summary>
        public event Action ContinuationAction;

        private readonly SynchronizationContext _capturedContext;

        public SaveKeeperOperationInternal()
        {
            _capturedContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Finalizes the operation, sets the result, and triggers all pending callbacks.
        /// Idempotent — second and later calls are no-ops, preserving the original outcome.
        /// </summary>
        /// <param name="result">The result value of the operation.</param>
        /// <param name="error">The exception to set if the operation failed. Null indicates success.</param>
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
