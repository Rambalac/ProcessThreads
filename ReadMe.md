Process threads
===============
Runs methods in separate process.

All parameters and method targets must be Serializable.
Expression in ```Start``` method must be method call expressions. You still can use something like 
```
manager.Start(()=>("asd"+"fgh").MyExtensionMethod(var1+var2))
``` 
But you cannot use 
```
manager.Start(()=>MyMethod1(var)+MyMethod2()))
```
All method targets and parameters are computed on caller process before executing method on separate process.

Because method runs in separate process you can not access the same data in static fields. 
Any change in static data in one process has no effect for other. 
Because of the same reason any changes made in parameter objects will not be passed to other processes. 
If you pass one of parameters as result, returned object will be a new object with new reference not related with original parameter.

Installing
----------
To install from Nuget use.
```
Install-Package ProcessThreads
```

Simple example
--------------

```C#
public static string TestMethod(int param) // Executed in new process
{
    return "*" + (param * 10) + "*";
}

public void StartProcess()
{
    var manager = new ProcessThreadsManager();
    var task = manager.Start(()=>TestMethod(15)); // Returns immediately
    Console.WriteLine(task.Result); // Waits for result and types *150*
}

```

One of library purposes is catching ```StackOverflowException```. It also passes back to ```Task``` any other exceptions if possible.

```C#
public static void TestStackOverflowException()
{
    TestStackOverflowExceptionException();
}

public void StartTestStackOverflowException()
{
	var manager = new ProcessThreadsManager();
    var task = manager.Start(()=>TestStackOverflowException());
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
        ... //Do something
    }
}

public void StartTestCancel()
{
	var manager = new ProcessThreadsManager();
    var task = manager.Start(()=>TestCancel());
    ... //Do something
    manager[task].Cancel();
}
```
In this case ```Task``` will not have ```IsCanceled``` state, but usual ```IsCompleted```

You also can throw ```OperationCanceledException``` within ProcessThread method, in such case Task will have ```IsCanceled``` state.
Use ```ProcessManager.ThrowIfCancellationRequested``` to check cancellation and throw ```OperationCanceledException```

PipeStream
----------
For real-time data exchange you can use ```Start``` with ```NamedPipeServerStream ``` out parameter. ```NamedPipeClientStream``` will be passed as Lambda parameter to your method.
Your code is fully responsible for pipes, including proper closing on both sides.
Don't forget to call ```WaitForConnection``` on caller side to be sure pipe is connected.

```C#
public static void TestPipe(string param, NamedPipeClientStream pipe)
{
	... //Do something
	pipe.Close();
}

public void StartTestPipe()
{
	var manager = new ProcessThreadsManager();
	NamedPipeServerStream pipe;
	manager.Start((p) => TestPipe("blabla", p), out pipe);
    pipe.WaitForConnection();
	... //Do something
	pipe.Disconnect();
}
```
