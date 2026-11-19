namespace Flamme.Persistence.EFCore;

/// <summary>
/// Interface to provide custom Flamme Db Context Schema in Migration
/// </summary>
public interface IFlammeDbContextSchema
{
    /// <summary>
    /// Name of the Schema
    /// </summary>
    public string Schema { get; }
}
