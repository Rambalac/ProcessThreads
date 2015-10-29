using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace AZI.ProcessThreads
{
    /// <summary>
    /// Internal class to run in separate process and invoke methods
    /// </summary>
    static class ProcessThreadExecutor
    {
        /// <summary>
        /// Main is used to start new process and call method with parameters passed in command line argument
        /// </summary>
        /// <param name="args"></param>
        /// <returns>Success - 0, Error anything other</returns>
        static int Main(string[] args)
        {
            if (args.Length < 5) return -1;
            NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX);
            //Console.WriteLine(string.Join(", ", args));

            Console.Title = args[2] + "." + args[3];

            ProcessThreadsManager.isProcessThread = true;
            ProcessThreadsManager.cancellationEvent = EventWaitHandle.OpenExisting(args[4]);

            try
            {
                var assembly = Assembly.LoadFile(args[1]);
                var type = assembly.GetType(args[2]);
                try
                {
                    switch ((InvocationType)int.Parse(args[0]))
                    {
                        case InvocationType.Pipe:
                            InvokeWithPipe(type, args[3], args[4]);
                            break;
                        case InvocationType.Func:
                            InvokeFunc(type, args[3], args[4]);
                            break;
                    }
                }
                catch (TargetInvocationException e)
                {
                    throw e.InnerException;
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                ProcessThreadsManager.cancellationEvent.Set();
                return 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return -1;
            }
        }

        /// <summary>
        /// Calls method with one parameter and result. Deserializes parameter and serialize result.
        /// </summary>
        /// <param name="type">Type containing method</param>
        /// <param name="methodName">Name of method to call</param>
        /// <param name="pipeName">Name of pipe to pass parameter and result</param>
        static void InvokeFunc(Type type, string methodName, string pipeName)
        {
            using (var pipe = new NamedPipeClientStream(pipeName))
            {

                pipe.Connect();

                var formatter = new BinaryFormatter();
                ProcessThreadParams pars;
                try
                {
                    var lengthBytes = new byte[4];
                    pipe.Read(lengthBytes, 0, 4);
                    var length = BitConverter.ToInt32(lengthBytes, 0);

                    var memory = new MemoryStream(length);
                    var buf = new byte[1024];
                    while (length != 0)
                    {
                        var red = pipe.Read(buf, 0, buf.Length);
                        memory.Write(buf, 0, red);
                        length -= red;
                    }
                    memory.Position = 0;

                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                      {
                          return type.Assembly;
                      };

                    pars = (ProcessThreadParams)formatter.Deserialize(memory);


                }
                catch (SerializationException e)
                {

                    formatter.Serialize(pipe, ProcessThreadResult.Exception(e));
                    throw;
                }

                var method = type.GetMethod(methodName, pars.Types);

                try
                {
                    object result = method.Invoke(pars.Target, pars.Parameters);

                    var memory = new MemoryStream();
                    formatter.Serialize(memory, ProcessThreadResult.Successeded(result));
                    memory.WriteTo(pipe);
                }
                catch (TargetInvocationException e)
                {
                    formatter.Serialize(pipe, ProcessThreadResult.Exception(e.InnerException));
                    throw e.InnerException;
                }
                catch (Exception e)
                {
                    formatter.Serialize(pipe, ProcessThreadResult.Exception(e));
                    throw;
                }
            }
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
    }
}
