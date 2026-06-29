using System;
using System.Runtime.CompilerServices;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>Awaiter that lets <see cref="SaveKeeperOperationHandle"/> work with the C# <c>await</c> keyword.</summary>
    public struct SaveKeeperAwaiter : INotifyCompletion
    {
        private SaveKeeperOperationHandle m_Handle;

        public SaveKeeperAwaiter(SaveKeeperOperationHandle handle)
        {
            m_Handle = handle;
        }

        /// <summary>Checked by the runtime — true means skip the suspend.</summary>
        public readonly bool IsCompleted => m_Handle.IsDone;

        /// <summary>Schedules the continuation that resumes the async method when the operation finishes.</summary>
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

        /// <summary>Called when the await resumes — rethrows the recorded exception if the operation failed.</summary>
        public readonly void GetResult()
        {
            if (m_Handle.Status == SaveKeeperOperationStatus.Failed)
            {
                throw m_Handle.Error ?? new Exception("SaveKeeper Operation Failed without explicit exception.");
            }
        }
    }

    /// <summary>Awaiter for the generic <see cref="SaveKeeperOperationHandle{T}"/>; returns the typed result.</summary>
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

        /// <summary>Retrieves the operation's result — rethrows the recorded exception if the operation failed.</summary>
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
