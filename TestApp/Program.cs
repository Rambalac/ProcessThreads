using System;
using System.IO.Pipes;
using System.IO;

namespace TestApp
{
    class Program
    {
        public static int TestMethod()
        {
            return 5;
        }

        public static string TestParam(string param)
        {
            return "!!!" + param + "!!!";
        }

        public static string TestParam(int param)
        {
            return "!!!" + (param*10) + "!!!";
        }


        public static void TestPipe(NamedPipeClientStream pipe)
        {
            using (var writer = new StreamWriter(pipe))
            {
                writer.WriteLine("BlaBla!!!");
            }
        }
        static void Main(string[] args)
        {
            var manager = new ProcessThreads.ProcessManager();
            var result1 = manager.Start(TestMethod).Result;
            Console.WriteLine(result1);

            NamedPipeServerStream pipe;
            manager.Start(TestPipe, out pipe);
            using (var reader = new StreamReader(pipe))
            {
                Console.WriteLine(reader.ReadToEnd());
            }

            var result2 = manager.Start(TestParam, "123").Result;
            Console.WriteLine(result2);

            var result3 = manager.Start(TestParam, 15).Result;
            Console.WriteLine(result3);
        }
    }
}
