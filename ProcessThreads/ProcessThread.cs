using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AZI.ProcessThreads
{
    /// <summary>
    /// Process Thread information
    /// </summary>
    public class ProcessThreadsManager
    {
        /// <summary>
        /// Process object in which thread is started
        /// </summary>
        public readonly Process Process;

        /// <summary>
        /// Communication pipe. Can be null.
        /// </summary>
        public readonly NamedPipeServerStream Pipe;

        public readonly Task Task;

        public readonly EventWaitHandle CancellationHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessThreadsManager" /> class.
        /// </summary>
        /// <param name="process">Process object for Process Thread</param>
        /// <param name="pipe">Optional server-sided named pipe</param>
        internal ProcessThreadsManager(Process process, Task task, EventWaitHandle cancellationHandle, NamedPipeServerStream pipe = null)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            Process = process;
            Pipe = pipe;
            Task = task;
            CancellationHandle = cancellationHandle;
        }

        public void Cancel()
        {
            CancellationHandle.Set();
        }
    }
}