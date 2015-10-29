using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AZI.ProcessThreads
{

    [Serializable]
    class ProcessThreadResult
    {
        public bool IsSuccesseded;
        public object Result;

        static public ProcessThreadResult Successeded(object r)
        {
            return new ProcessThreadResult { IsSuccesseded = true,        Result = r };
        }
        static public ProcessThreadResult Exception(Exception e)
        {
            return new ProcessThreadResult { IsSuccesseded = false, Result = e };
        }
    }
}