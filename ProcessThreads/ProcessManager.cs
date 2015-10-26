//-----------------------------------------------------------------------
// <copyright file="ProcessManager.cs" company="Rambalac">
// GNU GENERAL PUBLIC LICENSE
// </copyright>
//-----------------------------------------------------------------------
namespace AZI.ProcessThreads
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipes;
    using System.Reflection;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Collections.Concurrent;

    /// <summary>
    /// Manager for process threads
    /// </summary>
    public class ProcessManager
    {

        internal static bool isProcessThread = false;

        internal static EventWaitHandle cancellationEvent = null;

        /// <summary>
        /// List of started Process Threads
        /// </summary>
        readonly ConcurrentDictionary<Task, ProcessThreadsManager> Processes = new ConcurrentDictionary<Task, ProcessThreadsManager>();

        /// <summary>
        /// True if run as Process Thread
        /// </summary>
        public static bool IsProcessThread => isProcessThread;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public bool CreateNoWindow = true;

        public static void ThrowIfCancellationRequested()
        {
            if (IsCancelled) throw new OperationCanceledException();
        }

        /// <summary>
        /// Cancellation Token If run in Process Thread
        /// </summary>
        public static bool IsCancelled
        {
            get
            {
                if (!isProcessThread) throw new InvalidOperationException("Not Process Thread");
                return cancellationEvent.WaitOne(0);
            }
        }

        /// <summary>
        /// Returns Process Thread control object by Task
        /// </summary>
        /// <param name="task">Task associated to Process Thread</param>
        /// <returns>Process Thread control object</returns>
        public ProcessThreadsManager this[Task task]
        {
            get { return Processes[task]; }
        }

        /// <summary>
        /// Creates Process object, but does not start
        /// </summary>
        /// <typeparam name="T">Type for TaskCompletionSource type parameter</typeparam>
        /// <param name="method">Method to run in new process</param>
        /// <param name="type">Type of method parameters</param>
        /// <param name="exited">Response object to create Task</param>
        /// <param name="arguments">Additional command line argument</param>
        /// <returns>New Process object</returns>
        Process BuildInfo<T>(MethodInfo method, InvocationType type, TaskCompletionSource<T> exited, params string[] arguments)
        {
            var assemblyLocation = method.Module.Assembly.Location;
            var typeName = method.ReflectedType.FullName;
            var methodName = method.Name;

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetType().Assembly.Location,
                    Arguments = $"{(int)type} {assemblyLocation} {typeName} {methodName} " + string.Join(" ", arguments),
                    RedirectStandardError = true,
                    CreateNoWindow = CreateNoWindow,
                    UseShellExecute = false,
                    ErrorDialog = false
                },
                EnableRaisingEvents = true
            };
            proc.ErrorDataReceived += (sender, e) =>
            {
                exited.SetException(new Exception(e.Data));
                proc.Dispose();
            };
            return proc;
        }

        /// <summary>
        /// Starts Process Thread without parameters
        /// </summary>
        /// <param name="method">Static method to start</param>
        public Task Start(Action method)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            string cancelHndlName = Guid.NewGuid().ToString();
            var cancelHndl = new EventWaitHandle(false, EventResetMode.ManualReset, cancelHndlName);

            var exited = new TaskCompletionSource<bool>();
            var proc = BuildInfo(method.Method, InvocationType.Simple, exited, cancelHndlName);


            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode == 0)
                    exited.SetResult(true);
                else
                    if (proc.ExitCode == 1) exited.SetCanceled();
                else
                    exited.SetException(new Exception("Process Thread crashed"));

                proc.Dispose();
            };

            proc.Start();

            Processes.TryAdd(exited.Task, new ProcessThreadsManager(proc, exited.Task, cancelHndl));
            return exited.Task;
        }

        /// <summary>
        /// Starts Process Thread with bi-directional pipe for communication
        /// </summary>
        /// <param name="method">Static method to start</param>
        /// <param name="pipe">Bi-directional pipe for interprocess communication</param>
        /// <returns></returns>
        public Task Start(Action<NamedPipeClientStream> method, out NamedPipeServerStream pipe)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            string pipeName = Guid.NewGuid().ToString();
            pipe = new NamedPipeServerStream(pipeName);
            var exited = new TaskCompletionSource<bool>();
            var cancelHndl = new EventWaitHandle(false, EventResetMode.ManualReset, pipeName);

            var proc = BuildInfo(method.Method, InvocationType.Pipe, exited, pipeName);

            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode == 0)
                    exited.SetResult(true);
                else
                    if (proc.ExitCode == 1) exited.SetCanceled();
                else
                    exited.SetException(new Exception("Process Thread crashed"));

                proc.Dispose();
            };

            proc.Start();
            Processes.TryAdd(exited.Task, new ProcessThreadsManager(proc, exited.Task, cancelHndl, pipe));

            pipe.WaitForConnection();
            return exited.Task;
        }

        /// <summary>
        /// Start Process Thread with parameter and result
        /// </summary>
        /// <typeparam name="P">Parameter type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param">Parameter to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task<R> Start<P, R>(Func<P, R> method, P param)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));
            if (!typeof(P).IsSerializable) throw new ArgumentException("Parameter has to be serializable", nameof(param));
            if (!typeof(R).IsSerializable) throw new ArgumentException("Result has to be serializable", nameof(param));

            string pipeName = Guid.NewGuid().ToString();
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var exited = new TaskCompletionSource<R>();
            var cancelHndl = new EventWaitHandle(false, EventResetMode.ManualReset, pipeName);

            var proc = BuildInfo(method.Method, InvocationType.OneParamOneResult, exited, pipeName);
            var formatter = new BinaryFormatter();
            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode == 1)
                    exited.SetCanceled();
                else
                    if (proc.ExitCode != 0) exited.SetException(new Exception("Process Thread crashed"));
                proc.Dispose();
            };

            proc.Start();

            Processes.TryAdd(exited.Task, new ProcessThreadsManager(proc, exited.Task, cancelHndl, pipe));

            pipe.BeginWaitForConnection((ar) =>
            {
                pipe.EndWaitForConnection(ar);
                formatter.Serialize(pipe, param);

                var buf = new byte[1024];
                var memory = new MemoryStream();
                AsyncCallback callback = null;
                callback = readar =>
                {
                    int bytesRead = pipe.EndRead(readar);
                    if (bytesRead == 0)
                    {
                        pipe.Close();
                        memory.Position = 0;
                        var result = (R)formatter.Deserialize(memory);
                        exited.SetResult(result);
                    }
                    else
                    {
                        memory.Write(buf, 0, bytesRead);
                        pipe.BeginRead(buf, 0, buf.Length, callback, null);
                    }

                };

                pipe.BeginRead(buf, 0, buf.Length, callback, null);
            }, null);

            return exited.Task;
        }

    }
}
