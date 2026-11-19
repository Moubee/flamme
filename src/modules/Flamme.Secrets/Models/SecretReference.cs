namespace Flamme.Secrets.Models;

public record SecretReference(string Name, string? TypeName = null, string? Scope = null);
