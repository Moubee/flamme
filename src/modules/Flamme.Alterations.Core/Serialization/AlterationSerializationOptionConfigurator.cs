using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Options;
using Flamme.Workflows;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Flamme.Alterations.Core.Serialization;

/// <summary>
/// Add additional <see cref="JsonConverter"/> objects.
/// </summary>
[UsedImplicitly]
public class AlterationSerializationOptionConfigurator(IOptions<AlterationOptions> options) : SerializationOptionsConfiguratorBase
{
    /// <inheritdoc />
    public override IEnumerable<Action<JsonTypeInfo>> GetModifiers()
    {
        var alterationTypes = options.Value.AlterationTypes;

        yield return typeInfo =>
        {
            if (typeInfo.Type != typeof(IAlteration))
                return;

            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            var polymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "type"
            };

            foreach (var alterationType in alterationTypes.ToList())
            {
                polymorphismOptions.DerivedTypes.Add(new(alterationType, alterationType.Name));
            }

            typeInfo.PolymorphismOptions = polymorphismOptions;
        };
    }
}