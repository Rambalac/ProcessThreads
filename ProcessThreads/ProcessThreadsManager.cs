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
using System.Linq.Expressions;

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

        internal static EventWaitHandle cancellationEvent = null;

        /// <summary>
        /// List of started Process Threads
        /// </summary>
        readonly ConcurrentDictionary<Task, ProcessThread> processes = new ConcurrentDictionary<Task, ProcessThread>();

        /// <summary>
        /// True if run in Process Thread
        /// </summary>
        public static bool IsProcessThread { get; internal set; } = false;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public bool CreateNoWindow { get; set; } = true;

        /// <summary>
        /// See <see cref="Process"/>
        /// </summary>
        public ProcessPriorityClass PriorityClass { get; set; } = ProcessPriorityClass.Normal;

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public SecureString Password { get; set; }

        /// <summary>
        /// See <see cref="ProcessStartInfo" />
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Kill all started processes on Dispose
        /// </summary>
        public bool KillOnDispose { get; set; } = true;

        /// <summary>
        /// If cancellation signal was set by manager throws OperationCanceledException to inform Task about canceled operation (IsCanceled true).
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
                if (!IsProcessThread) throw new InvalidOperationException("Not a Process Thread");
                return cancellationEvent.WaitOne(0);
            }
        }

        /// <summary>
        /// Returns Process Thread control object by Task.
        /// </summary>
        /// <param name="task">Task associated to Process Thread.</param>
        /// <returns>Process Thread control object.</returns>
        public ProcessThread this[Task task]
        {
            get { return processes[task]; }
        }

        /// <summary>
        /// Creates Process object, but does not start.
        /// </summary>
        /// <typeparam name="T">Type for TaskCompletionSource type parameter.</typeparam>
        /// <param name="method">Method to run in new process.</param>
        /// <param name="exited">Response object to create Task.</param>
        /// <param name="arguments">Additional command line argument.</param>
        /// <returns>New Process Thread object.</returns>
        ProcessThread BuildInfo<T>(MethodInfo method, TaskCompletionSource<T> exited, params string[] arguments)
        {
            var assemblyLocation = method?.Module.Assembly.Location;
            var typeName = method?.ReflectedType.FullName;
            var methodName = method?.Name;

            string pipeName = Guid.NewGuid().ToString();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetType().Assembly.Location,
                    Arguments = $"{assemblyLocation ?? "null"} {typeName ?? "null"} {methodName ?? "null"} {pipeName}" + string.Join(" ", arguments),

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
        /// Starts Process Thread with bi-directional pipe for communication.
        /// </summary>
        /// <param name="lambda">Lambda to start in separate process. Lambda parameter is pipe you get in that process. 
        /// Don't forget to close it to be sure all data get transfered to caller process.</param>
        /// <param name="pipe">Bi-directional pipe for interprocess communication. Don't forget to WaitForConnection and in the end Close pipe. </param>
        /// <returns>Task to wait for lambda execution</returns>
        public Task Start(Expression<Action<NamedPipeClientStream>> lambda, out NamedPipeServerStream pipe)
        {
            var call = lambda.Body as MethodCallExpression;
            if (call == null) throw new ArgumentException("Lambda must be method call", nameof(lambda));

            return StartVariableParamsAndResult<Void>(call.Method, new ProcessThreadParams(call), out pipe);
        }

        /// <summary>
        /// Starts Process Thread with bi-directional pipe for communication.
        /// </summary>
        /// <param name="lambda">Lambda to start in separate process. Lambda parameter is pipe you get in that process. 
        /// Don't forget to close it to be sure all data get transfered to caller process.</param>
        /// <param name="pipe">Bi-directional pipe for interprocess communication. Don't forget to WaitForConnection and in the end Close pipe. </param>
        /// <returns>Task to wait for lambda execution and for its result</returns>
        public Task<R> Start<R>(Expression<Func<NamedPipeClientStream, R>> lambda, out NamedPipeServerStream pipe)
        {
            var call = lambda.Body as MethodCallExpression;
            if (call == null) throw new ArgumentException("Lambda must be method call", nameof(lambda));

            return StartVariableParamsAndResult<R>(call.Method, new ProcessThreadParams(call), out pipe);
        }

        /// <summary>
        /// Starts Process Thread without parameters.
        /// </summary>
        /// <param name="lambda">Lambda to start in separate process. 
        /// Must be method call expression. 
        /// If method object is expression it will be calculated in the caller process. 
        /// If method parameters are expression they will be calculated in the caller process</param>
        /// <returns>Task to wait for lambda execution</returns>
        public Task Start(Expression<Action> lambda)
        {
            var call = lambda.Body as MethodCallExpression;
            if (call == null) throw new ArgumentException("Lambda must be method call", nameof(lambda));

            NamedPipeServerStream pipe;
            return StartVariableParamsAndResult<Void>(call.Method, new ProcessThreadParams(call), out pipe);
        }

        /// <summary>
        /// Start Process Thread with result and no parameters.
        /// </summary>
        /// <typeparam name="R">Result type</typeparam>
        /// <param name="lambda">Lambda to start in separate process. 
        /// Must be method call expression. 
        /// If method object is expression it will be calculated in the caller process. 
        /// If method parameters are expression they will be calculated in the caller process</param>
        /// <returns>Task to wait for lambda execution and for its result</returns>
        public Task<R> Start<R>(Expression<Func<R>> lambda)
        {
            var call = lambda.Body as MethodCallExpression;
            if (call == null) throw new ArgumentException("Lambda must be method call", nameof(lambda));

            NamedPipeServerStream pipe;
            return StartVariableParamsAndResult<R>(call.Method, new ProcessThreadParams(call), out pipe);
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

        Task<R> StartVariableParamsAndResult<R>(MethodInfo method, ProcessThreadParams parameters, out NamedPipeServerStream pipe)
        {
            if (!(parameters.Target?.GetType().IsSerializable ?? true)) throw new ArgumentException("Method target must be serializable", nameof(method));
            if (!typeof(R).IsSerializable) throw new ArgumentException("Result must be serializable", typeof(R).FullName);
            foreach (var p in parameters.Parameters)
                if (p != null && !p.GetType().IsSerializable) throw new ArgumentException($"Parameter must be serializable", p.GetType().FullName);

            var exited = new TaskCompletionSource<R>();

            var proc = BuildInfo(method, exited);

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

            processes.TryAdd(exited.Task, proc);

            NamedPipeServerStream auxPipe = null;
            if (parameters.Pipe != null) auxPipe = new NamedPipeServerStream(parameters.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            pipe = auxPipe;

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
            foreach (var pair in processes.Where(p => p.Value.Process.HasExited))
            {
                pair.Value.Process.Dispose();
                remove.Add(pair.Key);
            }
            ProcessThread value;
            foreach (var key in remove)
                processes.TryRemove(key, out value);
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
            if (Password != null) Password.Dispose();
            if (KillOnDispose)
                foreach (var pair in processes)
                {
                    var proc = pair.Value.Process;
                    if (!proc.HasExited)
                        try {
                            proc.Kill();
                        } catch(InvalidOperationException)
                        {
                            //Should be ignored, probably process has exited in between
                        }
                    pair.Value.Process.Dispose();
                }
        }
    }
}
