using Flamme.Workflows.Activities;

var activity = new If(source: null, line: null);
return activity.Type == "ControlFlow.If" && activity.Version == 1 ? 0 : 1;
