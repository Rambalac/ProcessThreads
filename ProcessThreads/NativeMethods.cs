using System;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace AZI.ProcessThreads
{
    static class NativeMethods
    {
        /// <summary>
        /// Windows application error modes
        /// </summary>
        [Flags]
        internal enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }

        /// <summary>
        /// Sets error mode for current application
        /// </summary>
        /// <param name="uMode">Error mode</param>
        /// <returns></returns>
        [DllImport("kernel32.dll")]
        internal static extern ErrorModes SetErrorMode(ErrorModes uMode);
    }
}