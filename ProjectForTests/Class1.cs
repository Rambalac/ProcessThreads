using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectForTests
{
    [Serializable]
    public class Class1
    {
        public string teststring = "7634678";

        public string TestMethod(int param)
        {
            return teststring + param;
        }
    }
}
