using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flamme.Persistence.EFCore.Converters;

public class JsonValueConverter<T>() : ValueConverter<T, string>(v => JsonValueConverterHelper.Serialize(v), v => JsonValueConverterHelper.Deserialize<T>(v))
    where T : class;