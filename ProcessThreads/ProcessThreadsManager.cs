using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Security;
using System.Linq;
using System.Collections.Generic;
using System.Security.Permissions;

//-----------------------------------------------------------------------
// <copyright file="ProcessThreadsManager.cs" company="AZI">
// GNU GENERAL PUBLIC LICENSE
// </copyright>
//-----------------------------------------------------------------------
namespace AZI.ProcessThreads
{
    /// <summary>
    /// Manager for process threads
    /// </summary>
    public class ProcessThreadsManager : IDisposable
    {

        internal static bool isProcessThread = false;

        internal static EventWaitHandle cancellationEvent = null;

        /// <summary>
        /// List of started Process Threads
        /// </summary>
        readonly ConcurrentDictionary<Task, ProcessThread> Processes = new ConcurrentDictionary<Task, ProcessThread>();

        /// <summary>
        /// True if run in Process Thread
        /// </summary>
        public static bool IsProcessThread => isProcessThread;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public bool CreateNoWindow = true;

        /// <summary>
        /// See <see cref="Process"/>
        /// </summary>
        public ProcessPriorityClass PriorityClass = ProcessPriorityClass.Normal;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public SecureString Password;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public string UserName;

        /// <summary>
        /// Throws OperationCanceledException to inform Task about canceled operation (IsCanceled true) if cancellation signal was set by Process Threads manager.
        /// </summary>
        public static void ThrowIfCancellationRequested()
        {
            if (IsCancelled) throw new OperationCanceledException();
        }

        /// <summary>
        /// Returns true if parent send cancellation signal.
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
        public ProcessThread this[Task task]
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
        ProcessThread BuildInfo<T>(MethodInfo method, InvocationType type, TaskCompletionSource<T> exited, params string[] arguments)
        {
            var assemblyLocation = method.Module.Assembly.Location;
            var typeName = method.ReflectedType.FullName;
            var methodName = method.Name;

            string pipeName = Guid.NewGuid().ToString();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetType().Assembly.Location,
                    Arguments = $"{(int)type} {assemblyLocation} {typeName} {methodName} {pipeName}" + string.Join(" ", arguments),

                    RedirectStandardError = true,
                    CreateNoWindow = CreateNoWindow,
                    UseShellExecute = false,
                    ErrorDialog = false,

                    UserName = UserName,
                    Password = Password
                },
                EnableRaisingEvents = true
            };
            var cancelHndl = new EventWaitHandle(false, EventResetMode.ManualReset, pipeName);
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var result = new ProcessThread(proc, exited.Task, cancelHndl, pipe);

            proc.ErrorDataReceived += (sender, args) =>
            {
                result.AddErrorLine(args.Data);
            };

            return result;
        }

        /// <summary>
        /// Starts Process Thread with bi-directional pipe for communication
        /// </summary>
        /// <param name="method">Static method to start</param>
        /// <param name="pipe">Bi-directional pipe for interprocess communication</param>
        /// <returns></returns>
        public Task Start(Action<NamedPipeClientStream> method, out NamedPipeServerStream pipe)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method must be static", nameof(method));

            var exited = new TaskCompletionSource<bool>();

            var proc = BuildInfo(method.Method, InvocationType.Pipe, exited);
            pipe = proc.Pipe;

            proc.Process.Exited += (sender, args) =>
            {
                if (proc.Process.ExitCode == 0)
                    exited.SetResult(true);
                else
                    if (proc.Process.ExitCode == 1) exited.SetCanceled();
                else
                    exited.SetException(proc.GetError());
            };

            proc.Start(PriorityClass);

            Processes.TryAdd(exited.Task, proc);

            proc.Pipe.WaitForConnection();
            return exited.Task;
        }

        /// <summary>
        /// Starts Process Thread without parameters
        /// </summary>
        /// <param name="method">Static method to start</param>
        public Task Start(Action method)
        {
            return StartVariableParamsAndResult<Void>(method.GetMethodInfo(), new ProcessThreadParams(method.Target));
        }

        /// <summary>
        /// Starts Process Thread with parameter
        /// </summary>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter1 to pass to the method</param>
        public Task Start<P1>(Action<P1> method, P1 param1)
        {
            return StartVariableParamsAndResult<Void>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1) },
                new object[] { param1 }));
        }

        /// <summary>
        /// Start Process Thread with 2 parameters
        /// </summary>
        /// <typeparam name="P1">Parameter1 type</typeparam>
        /// <typeparam name="P2">Parameter2 type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter1 to pass to the method</param>
        /// <param name="param2">Parameter2 to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task Start<P1, P2>(Action<P1, P2> method, P1 param1, P2 param2)
        {
            return StartVariableParamsAndResult<Void>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1), typeof(P2) },
                new object[] { param1, param2 }));
        }

        /// <summary>
        /// Start Process Thread with 3 parameters
        /// </summary>
        /// <typeparam name="P1">Parameter1 type</typeparam>
        /// <typeparam name="P2">Parameter2 type</typeparam>
        /// <typeparam name="P3">Parameter3 type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter1 to pass to the method</param>
        /// <param name="param2">Parameter2 to pass to the method</param>
        /// <param name="param3">Parameter3 to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task Start<P1, P2, P3>(Action<P1, P2, P3> method, P1 param1, P2 param2, P3 param3)
        {
            return StartVariableParamsAndResult<Void>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1), typeof(P2), typeof(P3) },
                new object[] { param1, param2, param3 }));
        }

        /// <summary>
        /// Start Process Thread with result and no parameters
        /// </summary>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <returns>Task for method result</returns>
        public Task<R> Start<R>(Func<R> method)
        {
            return StartVariableParamsAndResult<R>(method.GetMethodInfo(), new ProcessThreadParams(method.Target));
        }

        /// <summary>
        /// Start Process Thread with parameter and result
        /// </summary>
        /// <typeparam name="P1">Parameter type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task<R> Start<P1, R>(Func<P1, R> method, P1 param1)
        {
            return StartVariableParamsAndResult<R>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1) },
                new object[] { param1 }));
        }

        /// <summary>
        /// Start Process Thread with 2 parameters and result
        /// </summary>
        /// <typeparam name="P1">Parameter1 type</typeparam>
        /// <typeparam name="P2">Parameter2 type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter1 to pass to the method</param>
        /// <param name="param2">Parameter2 to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task<R> Start<P1, P2, R>(Func<P1, P2, R> method, P1 param1, P2 param2)
        {
            return StartVariableParamsAndResult<R>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1), typeof(P2) },
                new object[] { param1, param2 }));
        }

        /// <summary>
        /// Start Process Thread with 3 parameters and result
        /// </summary>
        /// <typeparam name="P1">Parameter1 type</typeparam>
        /// <typeparam name="P2">Parameter2 type</typeparam>
        /// <typeparam name="P3">Parameter3 type</typeparam>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="method">Static method to start</param>
        /// <param name="param1">Parameter1 to pass to the method</param>
        /// <param name="param2">Parameter2 to pass to the method</param>
        /// <param name="param3">Parameter3 to pass to the method</param>
        /// <returns>Result of method</returns>
        public Task<R> Start<P1, P2, P3, R>(Func<P1, P2, P3, R> method, P1 param1, P2 param2, P3 param3)
        {
            return StartVariableParamsAndResult<R>(method.GetMethodInfo(), new ProcessThreadParams(
                method.Target,
                new Type[] { typeof(P1), typeof(P2), typeof(P3) },
                new object[] { param1, param2, param3 }));
        }

        void SerializeDeserializeAsync(NamedPipeServerStream pipe, ProcessThreadParams parameters, Action<ProcessThreadResult> callback)
        {
            var formatter = new BinaryFormatter();
            pipe.BeginWaitForConnection((ar) =>
            {
                pipe.EndWaitForConnection(ar);
                var memory = new MemoryStream();
                formatter.Serialize(memory, parameters);

                var lengthBytes = BitConverter.GetBytes((int)memory.Length);
                pipe.Write(lengthBytes, 0, 4);
                memory.WriteTo(pipe);

                var buf = new byte[1024];
                memory = new MemoryStream();
                AsyncCallback endread = null;
                endread = reader =>
                {
                    int bytesRead = pipe.EndRead(reader);
                    if (bytesRead == 0)
                    {
                        pipe.Close();
                        memory.Position = 0;
                        if (memory.Length != 0)
                        {
                            var result = (ProcessThreadResult)formatter.Deserialize(memory);
                            callback(result);
                        }
                    }
                    else
                    {
                        memory.Write(buf, 0, bytesRead);
                        pipe.BeginRead(buf, 0, buf.Length, endread, null);
                    }

                };

                pipe.BeginRead(buf, 0, buf.Length, endread, null);
            }, null);
        }

        Task<R> StartVariableParamsAndResult<R>(MethodInfo method, ProcessThreadParams parameters)
        {
            //if (!method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));
            if (!(parameters.Target?.GetType().IsSerializable ?? true)) throw new ArgumentException("Method target must be serializable", nameof(method));
            if (!typeof(R).IsSerializable) throw new ArgumentException("Result must be serializable", typeof(R).FullName);
            foreach (var p in parameters.Parameters)
                if (p != null && !p.GetType().IsSerializable) throw new ArgumentException($"Parameter must be serializable", p.GetType().FullName);

            var exited = new TaskCompletionSource<R>();

            var proc = BuildInfo(method, InvocationType.Func, exited);
            var formatter = new BinaryFormatter();
            bool resultSet = false;
            R taskResult = default(R);
            proc.Process.Exited += (sender, args) =>
            {
                var exitcode = proc.Process.ExitCode;
                if (exitcode == 0)
                {
                    if (resultSet)
                        exited.SetResult(taskResult);
                    else
                        exited.TrySetException(new Exception("ProcessThread failed to pass any result back"));
                }
                if (exitcode == 1)
                    exited.TrySetCanceled();
                else
                    exited.TrySetException(proc.GetError());
            };

            proc.Start(PriorityClass);

            Processes.TryAdd(exited.Task, proc);

            SerializeDeserializeAsync(proc.Pipe, parameters, (result) =>
            {
                if (result.IsSuccesseded)
                {
                    taskResult = (typeof(R) != typeof(Void)) ? (R)result.Result : default(R);
                    resultSet = true;
                }
                else
                {
                    if (result.Result is OperationCanceledException)
                        exited.TrySetCanceled();
                    else
                        exited.SetException((Exception)result.Result);

                }
            });

            return exited.Task;
        }

        /// <summary>
        /// Disposes exited Process Threads and removes from Manager
        /// </summary>
        public void CleanUp()
        {
            var remove = new List<Task>();
            foreach (var pair in Processes.Where(p => p.Value.Process.HasExited))
            {
                pair.Value.Process.Dispose();
                remove.Add(pair.Key);
            }
            ProcessThread value;
            foreach (var key in remove)
                Processes.TryRemove(key, out value);
        }

        /// <summary>
        /// Disposes Password if set.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes Password if set.
        /// </summary>
        protected virtual void Dispose(bool v)
        {
            Password.Dispose();
        }
    }
}
