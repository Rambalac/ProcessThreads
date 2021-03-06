﻿using Xunit;
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

    public static class TestExtensions
    {
        public static string TestExtension(this string obj, int param)
        {
            return obj + param;
        }
    }
    public class ProcessManagerTests : ProcessManagerTestsBase
    {
        [Serializable]
        public class TestClass
        {
            public string teststring = "GJHKG";

            public string TestMethod(int param)
            {
                return teststring + param;
            }
            public string TestPipe(int param, NamedPipeClientStream pipe)
            {
                return teststring + param;
            }
        }

        [Fact]
        public void StartTestClassMethod()
        {
            var testobject = new TestClass { teststring = "123" };
            var task = manager.Start(() => testobject.TestMethod(234));
            Assert.Equal("123234", task.Result);
        }

        [Fact]
        public void StartTestClassMethodWithPipe()
        {
            var testobject = new TestClass { teststring = "123" };
            NamedPipeServerStream pipe;
            var task = manager.Start((p) => testobject.TestPipe(234, p), out pipe);
            Assert.Equal("123234", task.Result);
        }

        [Fact]
        public void StartTestLibraryClassMethod()
        {
            var testobject = new ProjectForTests.Class1 { teststring = "123" };
            var task = manager.Start(() => testobject.TestMethod(234));
            Assert.Equal("123234", task.Result);
        }

        public static void TestAction()
        {
        }

        private static void TestPrivateAction()
        {
        }

        [Fact]
        public void StartTestPrivateAction()
        {
            var task = manager.Start(() => TestPrivateAction());
            task.Wait();
        }

        public static void TestAction1(string param)
        {
        }

        [Fact]
        public void StartTestAction1()
        {
            var task = manager.Start(() => TestAction1("SDFG"));
            task.Wait();
            Assert.InRange(DateTime.Now.Subtract(manager[task].Process.ExitTime).Minutes, 0, 5);
        }

        public static string TestPipe(string myparam, NamedPipeClientStream pipe)
        {
            var reader = new StreamReader(pipe);
            using (var writer = new StreamWriter(pipe))
            {
                var buf = reader.ReadLine();
                writer.Write(myparam + "BlaBla!!!" + buf);
                writer.Flush();
            }
            return "Done";
        }

        [Fact]
        public void StartTestPipe()
        {
            NamedPipeServerStream pipe;
            var task = manager.Start((p) => TestPipe("HJG", p), out pipe);
            pipe.WaitForConnection();
            var writer = new StreamWriter(pipe);
            using (var reader = new StreamReader(pipe))
            {
                writer.WriteLine("qwerty");
                writer.Flush();
                Assert.Equal("HJGBlaBla!!!qwerty", reader.ReadToEnd());
            }
            Assert.Equal("Done", task.Result);
        }

        public static string TestParam(string param)
        {
            return "!!!" + param + "!!!";
        }

        [Fact]
        public void StartTestSerializationString()
        {
            Assert.Equal("!!!123!!!", manager.Start(() => TestParam("123")).Result);
        }

        [Fact]
        public void StartTestLambda()
        {
            var str = "234";
            Assert.Equal("!!!123234!!!", manager.Start(() => TestParam("123" + str)).Result);
        }

        [Fact]
        public void StartTestExtension()
        {
            Assert.Equal("321567", manager.Start(() => "321".TestExtension(567)).Result);
        }

        public static string TestParam(object param)
        {
            return "Trick!";
        }

        [Fact]
        public void StartTestParamTypes()
        {
            Assert.Equal("Trick!", manager.Start(() => TestParam((object)"123")).Result);
        }

        public static string TestParam2(string p1, int p2)
        {
            return p1 + p2;
        }

        [Fact]
        public void StartTestParam2()
        {
            Assert.Equal("123321", manager.Start(() => TestParam2("123", 321)).Result);
        }

        public static string TestParam3(string p1, int p2, bool p3)
        {
            return p1 + p2 + p3;
        }

        [Fact]
        public void StartTestParam3()
        {
            Assert.Equal("123321False", manager.Start(() => TestParam3("123", 321, false)).Result);
        }

        public static string TestParam(int param)
        {
            return "!!!" + (param * 10) + "!!!";
        }

        public static void TestSleep(int param)
        {
            Thread.Sleep(param);
        }


        [Fact]
        public void StartTestSerializationInt()
        {
            Assert.Equal("!!!150!!!", manager.Start(() => TestParam(15)).Result);
        }

        [Fact]
        public void StartTestDispose()
        {
            using (var newmanager = new ProcessThreadsManager())
            {
                Assert.Equal("!!!150!!!", newmanager.Start(() => TestParam(15)).Result);
            }
        }

        [Fact]
        public void StartTestDisposeKill()
        {
            int procid;
            using (var newmanager = new ProcessThreadsManager())
            {
                var task = newmanager.Start(() => TestSleep(60000));
                procid = newmanager[task].Process.Id;
                Thread.Sleep(200);
            }
            Thread.Sleep(100);
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(procid));

        }

        [Fact]
        public void StartTestDisposeNotKill()
        {
            int procid;
            using (var newmanager = new ProcessThreadsManager())
            {
                newmanager.KillOnDispose = false;
                var task = newmanager.Start(() => TestSleep(60000));
                procid = newmanager[task].Process.Id;
                Thread.Sleep(200);
            }
            Thread.Sleep(100);
            var proc = Process.GetProcessById(procid);
            Assert.Equal(procid, proc.Id);
            proc.Kill();

        }


        public static void TestException()
        {
            throw new NotSupportedException();
        }

        [Fact]
        public void StartTestExceptionAction()
        {
            var task = manager.Start(() => TestException());
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.IsType<NotSupportedException>(ex.InnerException);
        }

        public static void TestStackOverflowExceptionException()
        {
            TestStackOverflowExceptionException();
        }

        [Fact]
        public void StartTestStackOverflowExceptionAction()
        {
            var task = manager.Start(() => TestStackOverflowExceptionException());
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.IsType<StackOverflowException>(ex.InnerException);
        }

        public static string TestStackOverflowExceptionException(int a)
        {
            return TestStackOverflowExceptionException(a);
        }

        [Fact]
        public void StartTestStackOverflowExceptionFunc()
        {
            var task = manager.Start(() => TestStackOverflowExceptionException(123));
            var ex = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.IsType<StackOverflowException>(ex.InnerException);
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
            var task = manager.Start(() => TestCancel());
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
            var task = manager.Start(() => TestCancelException());
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