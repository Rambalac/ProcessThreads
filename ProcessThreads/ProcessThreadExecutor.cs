using System;
using System.IO.Pipes;
using System.Reflection;
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
                        case InvocationType.Simple:
                            InvokeSimple(type, args[3]);
                            break;
                        case InvocationType.Pipe:
                            InvokeWithPipe(type, args[3], args[4]);
                            break;
                        case InvocationType.ParamsAndResult:
                            InvokeWithParamsAndResult(type, args[3], args[4]);
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
                Console.WriteLine(e);
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
        static void InvokeWithParamsAndResult(Type type, string methodName, string pipeName)
        {
            var pipe = new NamedPipeClientStream(pipeName);

            pipe.Connect();

            var formatter = new BinaryFormatter();

            var pars = (object[])formatter.Deserialize(pipe);
            var types = (Type[])pars[0];

            var method = type.GetMethod(methodName, types);

            object result = method.Invoke(null, (object[])pars[1]);

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
