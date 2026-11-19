namespace Flamme.Common;

public interface ITask
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}