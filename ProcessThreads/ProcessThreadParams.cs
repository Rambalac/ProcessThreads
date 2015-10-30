using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AZI.ProcessThreads
{
    [Serializable]
    class ProcessThreadParams
    {
        public object Target;
        public Type[] Types;
        public object[] Parameters;
        public string Pipe;

        public ProcessThreadParams(object target, Type[] types, object[] pars, bool piped = false)
        {
            Target = target;
            Types = types;
            Parameters = pars;
            Pipe = Guid.NewGuid().ToString();
        }
    }

}