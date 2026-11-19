using System.Reflection;

namespace Flamme.Workflows;

public interface IActivityPropertyDefaultValueProvider
{
    object GetDefaultValue(PropertyInfo property);
}