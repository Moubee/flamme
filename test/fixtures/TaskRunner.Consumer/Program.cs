using TaskRunner;

var createTask = GeneratedTasks.Create().Single();
Console.WriteLine(createTask().Run());
