using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Linq.Expressions;
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

        public ProcessThreadParams(MethodCallExpression call)
        {
            Parameters = call.Arguments.Select(a => (a is ParameterExpression) ? new PipeParameter() : Expression.Lambda(a).Compile().DynamicInvoke()).ToArray();
            Types = call.Method.GetParameters().Select(p => p.ParameterType).ToArray();
            Target = (call.Object != null) ? Expression.Lambda(call.Object).Compile().DynamicInvoke() : null;
            Pipe = Parameters.Any(p => p is PipeParameter) ? Guid.NewGuid().ToString() : null;
        }
    }

}