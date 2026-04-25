using System;
using System.Runtime.CompilerServices;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Custom awaiter pattern implementation that allows `GameSaverOperationHandle` to be awaited asynchronously.
    /// Ties into the standard C# state machine via INotifyCompletion.
    /// </summary>
    public struct GameSaverAwaiter : INotifyCompletion
    {
        private GameSaverOperationHandle m_Handle;

        public GameSaverAwaiter(GameSaverOperationHandle handle)
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
            if (m_Handle.Status == GameSaverOperationStatus.Failed)
            {
                throw m_Handle.Error ?? new Exception("GameSaver Operation Failed without explicit exception.");
            }
        }
    }

    /// <summary>
    /// Custom awaiter pattern implementation that allows `GameSaverOperationHandle&lt;T&gt;` to be awaited asynchronously.
    /// Returns a generic result type upon completion.
    /// </summary>
    /// <typeparam name="T">The type of the expected result.</typeparam>
    public struct GameSaverAwaiter<T> : INotifyCompletion
    {
        private GameSaverOperationHandle<T> m_Handle;

        public GameSaverAwaiter(GameSaverOperationHandle<T> handle)
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
            if (m_Handle.Status == GameSaverOperationStatus.Failed)
            {
                throw m_Handle.Error ?? new Exception("GameSaver Operation Failed without explicit exception.");
            }

            return m_Handle.Result;
        }
    }
}
