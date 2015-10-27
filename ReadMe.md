Process threads
===============

Runs static methods in separate process.

Because method run in separate process you can not access the same data in static fields. Any change in static data in one process has no effect for other. For realtime data exchange you can use Start with pipe as out parameter
```C#
var manager = new ProcessThreadsManager();
NamedPipeServerStream pipe;
manager.Start(staticMethod, out pipe);
```

Example
-------

```C#
public static string TestMethod(int param)
{
    return "*" + (param * 10) + "*";
}

public void StartProcess()
{
    var manager = new ProcessThreadsManager();
    var task = manager.Start(TestMethod, 15);
    Console.WriteLine(task.Result); // *150*
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
