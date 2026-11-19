namespace Flamme.Testing.Shared;

public class TriggerChangeTokenSignalEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}