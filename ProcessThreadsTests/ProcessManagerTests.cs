using Xunit;
using System;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace AZI.ProcessThreads.Tests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public class ProcessManagerTestsBase
    {
        protected ProcessThreadsManager manager = new ProcessThreadsManager();


    }

    [Serializable]
    public class TestClass
    {
        public string teststring = "sdfgsd";

        public string TestMethod(int param)
        {
            return teststring + param;
        }
    }

    public class ProcessManagerTests : ProcessManagerTestsBase
    {
        static void Main(string[] args)
        {
            var test = new ProcessManagerTests();
            test.manager.CreateNoWindow = false;
            test.StartTestClassMethod();
        }

        [Fact]
        public void StartTestClassMethod()
        {
            var testobject = new TestClass { teststring = "123" };
            var task = manager.Start(testobject.TestMethod, 234);
            Console.WriteLine(task.Result);
            Assert.Equal("123234", task.Result);
        }

        public static void TestAction()
        {
        }

        [Fact]
        public void StartTestAction()
        {
            var task = manager.Start(TestAction);
            task.Wait();
            Assert.InRange(DateTime.Now.Subtract(manager[task].Process.ExitTime).Minutes, 0, 5);
        }

        public static void TestAction1(string param)
        {
        }

        [Fact]
        public void StartTestAction1()
        {
            var task = manager.Start(TestAction1, "sdfg");
            task.Wait();
            Assert.InRange(DateTime.Now.Subtract(manager[task].Process.ExitTime).Minutes, 0, 5);
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

        public static string TestParam(object param)
        {
            return "Trick!";
        }

        [Fact]
        public void StartTestParamTypes()
        {
            Assert.Equal("Trick!", manager.Start(TestParam, (object)"123").Result);
        }

        public static string TestParam2(string p1, int p2)
        {
            return p1 + p2;
        }

        [Fact]
        public void StartTestParam2()
        {
            Assert.Equal("123321", manager.Start(TestParam2, "123", 321).Result);
        }

        public static string TestParam3(string p1, int p2, bool p3)
        {
            return p1 + p2 + p3;
        }

        [Fact]
        public void StartTestParam3()
        {
            Assert.Equal("123321False", manager.Start(TestParam3, "123", 321, false).Result);
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
            throw new NotSupportedException();
        }

        [Fact]
        public void StartTestExceptionAction()
        {
            var task = manager.Start(TestException);
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.True(ex.InnerException is NotSupportedException);
        }

        public static void TestStackOverflowExceptionException()
        {
            TestStackOverflowExceptionException();
        }

        [Fact]
        public void StartTestStackOverflowExceptionAction()
        {
            var task = manager.Start(TestStackOverflowExceptionException);
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.Equal("Process Thread crashed, possible StackOverflowException", ex.InnerException.Message);
        }

        public static string TestStackOverflowExceptionException(int a)
        {
            return TestStackOverflowExceptionException(a);
        }

        [Fact]
        public void StartTestStackOverflowExceptionFunc()
        {
            var task = manager.Start(TestStackOverflowExceptionException, 123);
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.Equal("Process Thread crashed, possible StackOverflowException", ex.InnerException.Message);
        }


        public static void TestCancel()
        {
            while (!ProcessThreadsManager.IsCancelled)
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
                ProcessThreadsManager.ThrowIfCancellationRequested();
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