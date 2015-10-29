Process threads
===============

Runs methods in separate process.

All parameters and method targets must be Serializable.

Because method run in separate process you can not access the same data in static fields. 
Any change in static data in one process has no effect for other. 
Because of the same reason any changes made in parameter objects will not be passed to other processes. 
If you pass one of parameters as result returned object will new object with new reference.

Simple Example
--------------

```C#
public static string TestMethod(int param) // Executed in new process
{
    return "*" + (param * 10) + "*";
}

public void StartProcess()
{
    var manager = new ProcessThreadsManager();
    var task = manager.Start(TestMethod, 15); // Returns immediately
    Console.WriteLine(task.Result); // Waits for result and types *150*
}

```

One of purposes of library is catching ```StackOverflowException```. It also passes back to Task any other exceptions if possible.

```C#
public static void TestStackOverflowExceptionException()
{
    TestStackOverflowExceptionException();
}

public void StartTestStackOverflowException()
{
	var manager = new ProcessThreadsManager();
    var task = manager.Start(TestStackOverflowExceptionException);
	try{
	task.Wait();
	} catch(AggregateException e)
	{
		Console.WriteLine(e.InnerException); //Oops, StackOverflowException
	}
}

```

Cancellation
------------

```C#
public static void TestCancel()
{
    while (!ProcessManager.IsCancelled)
    {
        Thread.Sleep(50);
    }
}

public void StartTestCancel()
{
	var manager = new ProcessThreadsManager();
    var task = manager.Start(TestCancel);
    Thread.Sleep(200);
    manager[task].Cancel();
}
```

PipeStream
----------
For realtime data exchange you can use Start with pipe as out parameter. Your code is fully responsible for pipes, including proper closing.

```C#
public static void TestPipe(NamedPipeClientStream pipe)
{

}

public void StartTestPipe()
{
	var manager = new ProcessThreadsManager();
	NamedPipeServerStream pipe;
	manager.Start(TestPipe, out pipe);
}
```
