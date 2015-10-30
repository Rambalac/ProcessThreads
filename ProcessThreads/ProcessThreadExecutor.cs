using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
            if (args.Length < 4) return -1;
            NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX);
            //Console.WriteLine(string.Join(", ", args));

            Console.Title = args[1] + "." + args[2];

            ProcessThreadsManager.isProcessThread = true;
            ProcessThreadsManager.cancellationEvent = EventWaitHandle.OpenExisting(args[3]);

            try
            {
                var assembly = Assembly.LoadFile(args[0]);
                var type = assembly.GetType(args[1]);
                try
                {
                    InvokeFunc(type, args[2], args[3]);
                    return 0;
                }
                catch (TargetInvocationException e)
                {
                    throw e.InnerException;
                }

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
                var lengthBytes = new byte[4];
                pipe.Read(lengthBytes, 0, 4);
                var length = BitConverter.ToInt32(lengthBytes, 0);

                var inmemory = new MemoryStream(length);
                var buf = new byte[1024];
                while (length != 0)
                {
                    var red = pipe.Read(buf, 0, buf.Length);
                    inmemory.Write(buf, 0, red);
                    length -= red;
                }
                inmemory.Position = 0;
                try
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                      {
                          return type.Assembly;
                      };

                    pars = (ProcessThreadParams)formatter.Deserialize(inmemory);


                    var method = type.GetMethod(methodName, pars.Types);

                    if (pars.Pipe != null)
                    {
                        var auxPipe = new NamedPipeClientStream(pars.Pipe);
                        auxPipe.Connect();

                        for (int i = 0; i < pars.Parameters.Length; i++)
                            if (pars.Parameters[i] is PipeParameter) pars.Parameters[i] = auxPipe;
                    }
                    object result = method.Invoke(pars.Target, pars.Parameters);

                    var outmemory = new MemoryStream();
                    formatter.Serialize(outmemory, ProcessThreadResult.Successeded(result));
                    outmemory.WriteTo(pipe);
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
    }
}
