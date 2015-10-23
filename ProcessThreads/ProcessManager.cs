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
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Linq;

    /// <summary>
    /// Manager for process threads
    /// </summary>
    public class ProcessManager
    {
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
            internal ProcessThread(Process process, NamedPipeServerStream pipe)
            {
                if (process == null) throw new ArgumentNullException(nameof(process));

                Process = process;
                Pipe = pipe;
            }

        }


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
                    //CreateNoWindow = true,
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
        /// <param name="method">Static method to start in Process Thread</param>
        public Task Start(Action method)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            var exited = new TaskCompletionSource<bool>();
            var proc = BuildInfo(method.Method, InvokeType.Simple, exited);


            proc.Exited += (sender, args) =>
            {
                exited.SetResult(true);
                proc.Dispose();
            };

            proc.Start();
            Processes.Add(new ProcessThread(proc, null));
            return exited.Task;
        }

        /// <summary>
        /// Starts Process Thread without parameters
        /// </summary>
        /// <param name="method">Static method to start in Process Thread</param>
        public Task<int> Start(Func<int> method)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            var exited = new TaskCompletionSource<int>();
            var proc = BuildInfo(method.Method, InvokeType.Simple, exited);


            proc.Exited += (sender, args) =>
            {
                exited.SetResult(proc.ExitCode);
                proc.Dispose();
            };

            proc.Start();
            Processes.Add(new ProcessThread(proc, null));
            return exited.Task;
        }
        /// <summary>
        /// Starts Process Thread with bi-directional pipe for communication
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public ProcessThread Start(Action<NamedPipeClientStream> method, out NamedPipeServerStream pipe)
        {
            if (!method.Method.IsStatic) throw new ArgumentException("Method has to be static", nameof(method));

            string pipeName = Guid.NewGuid().ToString();
            pipe = new NamedPipeServerStream(pipeName);
            var exited = new TaskCompletionSource<bool>();
            var proc = BuildInfo(method.Method, InvokeType.Pipe, exited, pipeName);

            proc.Exited += (sender, args) =>
            {
                exited.SetResult(true);
                proc.Dispose();
            };

            proc.Start();
            var res = new ProcessThread(proc, pipe);
            Processes.Add(res);
            pipe.WaitForConnection();
            return res;
        }

        enum InvokeType
        {
            Simple,
            Pipe,
            OneParamOneResult
        }

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

        static int Main(string[] args)
        {
            if (args.Length < 4) return -1;
            Console.Title = args[2] + "." + args[3];
            //Console.WriteLine(string.Join(",", args));
            try
            {
                var assembly = Assembly.LoadFile(args[1]);
                var type = assembly.GetType(args[2]);
                switch ((InvokeType)int.Parse(args[0]))
                {
                    case InvokeType.Simple:
                        return InvokeSimple(type, args[3]);
                    case InvokeType.Pipe:
                        return InvokeWithPipe(type, args[3], args[4]);
                    case InvokeType.OneParamOneResult:
                        return InvokeWithOneParamOneResult(type, args[3], args[4]);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.Error.WriteLine(e);
            }
            return -1;
        }

        static int InvokeWithOneParamOneResult(Type type, string methodName, string pipeName)
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

            return 0;
        }

        static int InvokeWithPipe(Type type, string methodName, string pipeName)
        {
            var method = type.GetMethod(methodName);
            var pipe = new NamedPipeClientStream(pipeName);
            pipe.Connect();
            return (int)(method.Invoke(null, new object[] { pipe }) ?? 0);
        }

        static int InvokeSimple(Type type, string methodName)
        {
            var method = type.GetMethod(methodName);
            return (int)(method.Invoke(null, new object[] { }) ?? 0);
        }
    }
}
