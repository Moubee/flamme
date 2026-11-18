namespace TaskRunner;

public interface IStartupTask
{
    string Run();
}

internal sealed class WarmCache : IStartupTask
{
    public string Run() => "cache warmed";
}
