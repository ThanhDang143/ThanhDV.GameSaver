using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>Read-only wrapper exposing operation state without letting callers mutate it.</summary>
    public struct SaveKeeperOperationHandle
    {
        internal SaveKeeperOperationInternal m_InternalOp;

        internal SaveKeeperOperationHandle(SaveKeeperOperationInternal internalOp)
        {
            m_InternalOp = internalOp;
        }

        /// <summary>True if the operation is null or has finished.</summary>
        public readonly bool IsDone => m_InternalOp == null || m_InternalOp.IsDone;

        /// <summary>Current status. Defaults to <see cref="SaveKeeperOperationStatus.Succeeded"/> when null.</summary>
        public readonly SaveKeeperOperationStatus Status => m_InternalOp?.Status ?? SaveKeeperOperationStatus.Succeeded;

        /// <summary>Progress from 0.0 to 1.0.</summary>
        public readonly float PercentComplete => m_InternalOp?.PercentComplete ?? 1f;

        /// <summary>The exception that caused failure, or null.</summary>
        public readonly Exception Error => m_InternalOp?.Error;

        /// <summary>
        /// Subscribes a callback for completion. Fires immediately if the operation is already done.
        /// </summary>
        public event Action<SaveKeeperOperationHandle> Completed
        {
            add
            {
                if (m_InternalOp != null && !m_InternalOp.IsDone) 
                {
                    m_InternalOp.Completed += value;
                }
                else 
                {
                    // If the operation is already finished (or null), guarantee the callback executes right away.
                    value?.Invoke(this);
                }
            }
            remove
            {
                if (m_InternalOp != null) m_InternalOp.Completed -= value;
            }
        }

        /// <summary>Enables <c>await handle;</c>.</summary>
        public readonly SaveKeeperAwaiter GetAwaiter()
        {
            return new SaveKeeperAwaiter(this);
        }
    }

    /// <summary>Read-only wrapper exposing operation state + typed result.</summary>
    /// <typeparam name="T">The type of the expected result.</typeparam>
    public struct SaveKeeperOperationHandle<T>
    {
        internal SaveKeeperOperationInternal<T> m_InternalOp;

        internal SaveKeeperOperationHandle(SaveKeeperOperationInternal<T> internalOp)
        {
            m_InternalOp = internalOp;
        }

        public readonly bool IsDone => m_InternalOp == null || m_InternalOp.IsDone;
        public readonly SaveKeeperOperationStatus Status => m_InternalOp?.Status ?? SaveKeeperOperationStatus.Succeeded;
        public readonly float PercentComplete => m_InternalOp?.PercentComplete ?? 1f;
        public readonly Exception Error => m_InternalOp?.Error;
        
        /// <summary>The final result. Returns <c>default</c> if the operation is incomplete or failed.</summary>
        public readonly T Result => m_InternalOp != null ? m_InternalOp.Result : default;

        /// <summary>
        /// Subscribes a callback for completion. Fires immediately if the operation is already done.
        /// </summary>
        public event Action<SaveKeeperOperationHandle<T>> Completed
        {
            add
            {
                if (m_InternalOp != null && !m_InternalOp.IsDone)
                {
                    m_InternalOp.Completed += value;
                }
                else
                {
                    // Guarantee execution if the subscription happens post-completion.
                    value?.Invoke(this);
                }
            }
            remove
            {
                if (m_InternalOp != null) m_InternalOp.Completed -= value;
            }
        }

        /// <summary>Enables <c>var result = await handle;</c>.</summary>
        public readonly SaveKeeperAwaiter<T> GetAwaiter()
        {
            return new SaveKeeperAwaiter<T>(this);
        }
    }
}
