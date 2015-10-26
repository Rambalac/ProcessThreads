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
    var task = manager.Start(TestMethod, 15);
    Console.WriteLine(task.Result); // *150*
}

```

