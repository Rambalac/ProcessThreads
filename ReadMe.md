Process threads
===============

Runs static methods in separate process

Example
-------

```C#
public static string TestMethod(int param)
{
    return "*" + (param * 10) + "*";
}

public void StartProcess()
{
	var manager = new ProcessManager();
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
	var manager = new ProcessManager();
    var task = manager.Start(TestCancel);
    Thread.Sleep(200);
    manager[task].Cancel();
}
```