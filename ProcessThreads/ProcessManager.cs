//-----------------------------------------------------------------------
// <copyright file="ProcessManager.cs" company="Rambalac">
// GNU GENERAL PUBLIC LICENSE
// </copyright>
//-----------------------------------------------------------------------
namespace ProcessThreads
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipes;
    using System.Reflection;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Manager for process threads
    /// </summary>
    public class ProcessManager
    {
        /// <summary>
        /// Windows application error modes
        /// </summary>
        [Flags]
        public enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }

        enum InvokeType
        {
            Simple,
            Pipe,
            OneParamOneResult
        }

        /// <summary>
        /// Sets error mode for current application
        /// </summary>
        /// <param name="uMode">Error mode</param>
        /// <returns></returns>
        [DllImport("kernel32.dll")]
        static extern ErrorModes SetErrorMode(ErrorModes uMode);

        /// <summary>
        /// List of started Process Threads
        /// </summary>
        public readonly List<ProcessThread> Processes = new List<ProcessThread>();

        /// <summary>
        /// Process Thread information
        /// </summary>
        public class ProcessThread
        {
            /// <summary>
            /// Process object in which thread is started
            /// </summary>
            public readonly Process Process;

            /// <summary>
            /// Communication pipe. Can be null.
            /// </summary>
            public readonly NamedPipeServerStream Pipe;

            /// <summary>
            /// Initializes a new instance of the <see cref="ProcessThread" /> class.
            /// </summary>
            /// <param name="process">Process object for Process Thread</param>
            /// <param name="pipe">Optional server-sided named pipe</param>
            internal ProcessThread(Process process, NamedPipeServerStream pipe = null)
            {
                if (process == null) throw new ArgumentNullException(nameof(process));

                Process = process;
                Pipe = pipe;
            }

        }

        /// <summary>
        /// <see cref="ProcessStartInfo" />
        /// </summary>
        public bool CreateNoWindow = true;

        /// <summary>
        /// Creates Process object, but does not start
        /// </summary>
        /// <typeparam name="T">Type for TaskCompletionSource type parameter</typeparam>
        /// <param name="method">Method to run in new preocess</param>
        /// <param name="type">Type of method parameters</param>
        /// <param name="exited">Responce object to create Task</param>
        /// <param name="arguments">Additional command line argument</param>
        /// <returns>New Process object</returns>
        Process BuildInfo<T>(MethodInfo method, InvokeType type, TaskCompletionSource<T> exited, string arguments = "")
        {
            var assemblyLocation = method.Module.Assembly.Location;
            var typeName = method.ReflectedType.FullName;
            var methodName = method.Name;

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetType().Assembly.Location,
                    Arguments = $"{(int)type} {assemblyLocation} {typeName} {methodName} " + arguments,
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

            var exited = new TaskCompletionSource<bool>();
            var proc = BuildInfo(method.Method, InvokeType.Simple, exited);


            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode == 0) exited.SetResult(true);
                else
                    exited.SetException(new Exception("Process Thread crashed"));
                proc.Dispose();
            };

            proc.Start();
            Processes.Add(new ProcessThread(proc));
            return exited.Task;
        }

        /// <summary>
        /// Starts Process Thread with bi-directional pipe for communication
        /// </summary>
        /// <param name="method">Static method to start</param>
        /// <returns></returns>
        public Task Start(Action<NamedPipeClientStream> method, out NamedPipeServerStream pipe)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            string pipeName = Guid.NewGuid().ToString();
            pipe = new NamedPipeServerStream(pipeName);
            var exited = new TaskCompletionSource<bool>();
            var proc = BuildInfo(method.Method, InvokeType.Pipe, exited, pipeName);

            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode == 0) exited.SetResult(true);
                else
                    exited.SetException(new Exception("Process Thread crashed"));

                proc.Dispose();
            };

            proc.Start();
            var res = new ProcessThread(proc, pipe);
            Processes.Add(res);
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

            var proc = BuildInfo(method.Method, InvokeType.OneParamOneResult, exited, pipeName);
            var formatter = new BinaryFormatter();
            proc.Exited += (sender, args) =>
            {
                if (proc.ExitCode != 0) exited.SetException(new Exception("Process Thread crashed"));
                proc.Dispose();
            };

            proc.Start();
            Processes.Add(new ProcessThread(proc, pipe));

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

        /// <summary>
        /// Main is used to start new process and call method with parameters passed in args
        /// </summary>
        /// <param name="args"></param>
        /// <returns>Success - 0, Error anything other</returns>
        static int Main(string[] args)
        {
            if (args.Length < 4) return -1;
            SetErrorMode(ErrorModes.SEM_NOGPFAULTERRORBOX);

            Console.Title = args[2] + "." + args[3];
            //Console.WriteLine(string.Join(",", args));

            try
            {
                var assembly = Assembly.LoadFile(args[1]);
                var type = assembly.GetType(args[2]);
                switch ((InvokeType)int.Parse(args[0]))
                {
                    case InvokeType.Simple:
                        InvokeSimple(type, args[3]);
                        break;
                    case InvokeType.Pipe:
                        InvokeWithPipe(type, args[3], args[4]);
                        break;
                    case InvokeType.OneParamOneResult:
                        InvokeWithOneParamOneResult(type, args[3], args[4]);
                        break;
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.Error.WriteLine(e);
            }
            return -1;
        }

        /// <summary>
        /// Calls method with one parameter and result. Deserializes parameter and serialize resut.
        /// </summary>
        /// <param name="type">Type containing method</param>
        /// <param name="methodName">Name of method to call</param>
        /// <param name="pipeName">Name of pipe to pass parameter and result</param>
        static void InvokeWithOneParamOneResult(Type type, string methodName, string pipeName)
        {
            var pipe = new NamedPipeClientStream(pipeName);

            pipe.Connect();

            var formatter = new BinaryFormatter();
            var param = formatter.Deserialize(pipe);

            var method = type.GetMethod(methodName, new Type[] { param.GetType() });

            object result = method.Invoke(null, new object[] { param });

            formatter.Serialize(pipe, result);

            pipe.WaitForPipeDrain();
            pipe.Close();
        }

        /// <summary>
        /// Calls method with bi-directional pipe.
        /// </summary>
        /// <param name="type">Type containing method</param>
        /// <param name="methodName">Name of method to call</param>
        /// <param name="pipeName">Name of pipe to pass</param>
        static void InvokeWithPipe(Type type, string methodName, string pipeName)
        {
            var pipe = new NamedPipeClientStream(pipeName);
            pipe.Connect();

            var method = type.GetMethod(methodName);
            method.Invoke(null, new object[] { pipe });

            pipe.WaitForPipeDrain();
            pipe.Close();
        }

        /// <summary>
        /// Calls method.
        /// </summary>
        /// <param name="type">Type containing method</param>
        /// <param name="methodName">Name of method to call</param>
        static void InvokeSimple(Type type, string methodName)
        {
            var method = type.GetMethod(methodName);
            method.Invoke(null, new object[] { });
        }
    }
}
