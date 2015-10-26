using Xunit;
using ProcessThreads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;

namespace ProcessThreads.Tests
{
    public class ProcessManagerTestsBase
    {
        protected ProcessManager manager = new ProcessManager();

    }
    public class ProcessManagerTests : ProcessManagerTestsBase
    {
        public static void TestSimple()
        {
        }

        [Fact]
        public void StartTestSimple()
        {
            Task task4 = manager.Start(TestSimple);
        }

        public static void TestPipe(NamedPipeClientStream pipe)
        {
            var reader = new StreamReader(pipe);
            var writer = new StreamWriter(pipe);
            var buf = reader.ReadLine();
            writer.Write("BlaBla!!!" + buf);
            writer.Flush();
        }

        [Fact]
        public void StartTestPipe()
        {
            NamedPipeServerStream pipe;
            manager.Start(TestPipe, out pipe);
            var writer = new StreamWriter(pipe);
            var reader = new StreamReader(pipe);
            writer.WriteLine("qwerty");
            writer.Flush();
            Assert.Equal("BlaBla!!!qwerty", reader.ReadToEnd());

            pipe.Disconnect();
        }

        public static string TestParam(string param)
        {
            return "!!!" + param + "!!!";
        }

        [Fact]
        public void StartTestSerializationString()
        {
            Assert.Equal("!!!123!!!", manager.Start(TestParam, "123").Result);


        }

        public static string TestParam(int param)
        {
            return "!!!" + (param * 10) + "!!!";
        }

        [Fact]
        public void StartTestSerializationInt()
        {
            Assert.Equal("!!!150!!!", manager.Start(TestParam, 15).Result);
        }

        public static void TestException()
        {
            TestException();
        }

        [Fact]
        public void StartTestStackOverflowException()
        {
            Task task4 = manager.Start(TestException);
            var ex = Assert.Throws<AggregateException>(() => task4.Wait());
            Assert.Equal("Process Thread crashed", ex.InnerException.Message);
        }
    }
}