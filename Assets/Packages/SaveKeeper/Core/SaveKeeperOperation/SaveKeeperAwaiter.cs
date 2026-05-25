using System;
using System.Runtime.CompilerServices;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Custom awaiter pattern implementation that allows `SaveKeeperOperationHandle` to be awaited asynchronously.
    /// Ties into the standard C# state machine via INotifyCompletion.
    /// </summary>
    public struct SaveKeeperAwaiter : INotifyCompletion
    {
        private SaveKeeperOperationHandle m_Handle;

        public SaveKeeperAwaiter(SaveKeeperOperationHandle handle)
        {
            m_Handle = handle;
        }

        /// <summary>
        /// Checked by the runtime to see if it should pause execution.
        /// </summary>
        public readonly bool IsCompleted => m_Handle.IsDone;

        /// <summary>
        /// Schedules the continuation action that resumes the async method after completion.
        /// </summary>
        public readonly void OnCompleted(Action continuation)
        {
            if (m_Handle.m_InternalOp != null)
            {
                // Register the compiler-generated continuation to our internal event
                m_Handle.m_InternalOp.ContinuationAction += continuation;
            }
            else
            {
                // Handle is null/empty; invoke continuation immediately
                continuation?.Invoke();
            }
        }

        /// <summary>
        /// Called when the await is finished. Throws the recorded exception if the operation failed.
        /// </summary>
        public readonly void GetResult()
        {
            if (m_Handle.Status == SaveKeeperOperationStatus.Failed)
            {
                throw m_Handle.Error ?? new Exception("SaveKeeper Operation Failed without explicit exception.");
            }
        }
    }

    /// <summary>
    /// Custom awaiter pattern implementation that allows `SaveKeeperOperationHandle&lt;T&gt;` to be awaited asynchronously.
    /// Returns a generic result type upon completion.
    /// </summary>
    /// <typeparam name="T">The type of the expected result.</typeparam>
    public struct SaveKeeperAwaiter<T> : INotifyCompletion
    {
        private SaveKeeperOperationHandle<T> m_Handle;

        public SaveKeeperAwaiter(SaveKeeperOperationHandle<T> handle)
        {
            m_Handle = handle;
        }

        public readonly bool IsCompleted => m_Handle.IsDone;

        public readonly void OnCompleted(Action continuation)
        {
            if (m_Handle.m_InternalOp != null)
            {
                m_Handle.m_InternalOp.ContinuationAction += continuation;
            }
            else
            {
                continuation?.Invoke();
            }
        }

        /// <summary>
        /// Retrieves the result of the operation. Throws the recorded exception if the operation failed.
        /// </summary>
        public readonly T GetResult()
        {
            if (m_Handle.Status == SaveKeeperOperationStatus.Failed)
            {
                throw m_Handle.Error ?? new Exception("SaveKeeper Operation Failed without explicit exception.");
            }

            return m_Handle.Result;
        }
    }
}
