using Xunit;
using System;
using System.IO.Pipes;
using System.IO;
using System.Threading;

namespace AZI.ProcessThreads.Tests
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
            var task = manager.Start(TestSimple);
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
            var task = manager.Start(TestException);
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.Equal("Process Thread crashed", ex.InnerException.Message);
        }

        public static void TestCancel()
        {
            while (!ProcessManager.IsCancelled)
            {
                Thread.Sleep(50);
            }
        }

        [Fact]
        public void StartTestCancel()
        {
            var task = manager.Start(TestCancel);
            Thread.Sleep(200);
            Assert.False(task.IsCompleted);

            manager[task].Cancel();

            Thread.Sleep(200);
            Assert.True(task.IsCompleted);
            Assert.False(task.IsCanceled);
            Assert.False(task.IsFaulted);
        }

        public static void TestCancelException()
        {
            while (true)
            {
                Thread.Sleep(50);
                ProcessManager.ThrowIfCancellationRequested();
            }
        }

        [Fact]
        public void StartTestCancelException()
        {
            var task = manager.Start(TestCancelException);
            Thread.Sleep(200);
            Assert.False(task.IsCompleted);

            manager[task].Cancel();

            Thread.Sleep(200);
            Assert.True(task.IsCompleted);
            Assert.True(task.IsCanceled);
            Assert.False(task.IsFaulted);
        }
    }
}