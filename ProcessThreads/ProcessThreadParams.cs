using System;
using System.Collections.Generic;
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

        public ProcessThreadParams()
        {
            Types = new Type[0];
            Parameters = new object[0];
        }
        public ProcessThreadParams(Type[] t, object[] p)
        {
            Types = t;
            Parameters = p;
        }
        public ProcessThreadParams(object target)
        {
            Target = target;
            Types = new Type[0];
            Parameters = new object[0];
        }
        public ProcessThreadParams(object target, Type[] types, object[] pars)
        {
            Target = target;
            Types = types;
            Parameters = pars;
        }
    }

}