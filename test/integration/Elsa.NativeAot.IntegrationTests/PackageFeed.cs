using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

internal sealed class PackageFeed
{
    internal const string Producer = "Elsa.NativeAot.Modules.Producer";
    private const string Abstractions = "Elsa.CodeGeneration.Abstractions";
    private const string BuildPackage = "Elsa.CodeGeneration.Build";
    private static readonly string[] RuntimePackages = [Abstractions, "Elsa.Features", Producer];
    internal static readonly string[] CompilerPackages = ["Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers", BuildPackage];
    private static readonly Lazy<DirectoryInfo> Consumers = new(() => Directory.CreateTempSubdirectory("flamme-consumers-"));
    private static readonly Lazy<Task<PackageFeed>> Default = new(() => Pack(false));
    private static readonly Lazy<Task<PackageFeed>> AsyncDisabled = new(() => Pack(true));

    private PackageFeed(bool disableRuntimeAsync)
    {
        Artifacts = BuildTest.CreateDirectory(disableRuntimeAsync ? "pack-async-disabled" : "pack");
        string[] asyncModel = disableRuntimeAsync ? ["-p:UseRuntimeAsync=false"] : [];
        PackProperties =
        [
            "-p:Configuration=Release", "-p:IsPackable=true",
            $"-p:Version={Version}", $"-p:PackageVersion={Version}", $"-p:PackageOutputPath={Feed}",
            "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={Path.Combine(Artifacts, "build")}", .. asyncModel
        ];
    }

    public string[] PackProperties { get; }
    public string Version { get; } = $"0.0.0-test.{Guid.NewGuid():N}";
    public string Artifacts { get; }
    public string ProducerProject { get; private set; } = null!;
    public string Feed => Path.Combine(Artifacts, "feed");

    public static Task<PackageFeed> Get(bool disableRuntimeAsync = false) =>
        (disableRuntimeAsync ? AsyncDisabled : Default).Value;

    private static async Task<PackageFeed> Pack(bool disableRuntimeAsync)
    {
        var packages = new PackageFeed(disableRuntimeAsync);
        Directory.CreateDirectory(packages.Feed);
        foreach (var id in RuntimePackages.Except([Producer]).Concat(CompilerPackages))
        {
            var project = Path.Combine(BuildTest.Root, "src/common", id, id + ".csproj");
            await packages.PackProject(id, project);
        }

        // Build a third-party feature library against the staged public package, outside the repository.
        var producer = packages.PrepareConsumer("producer");
        packages.ProducerProject = Path.Combine(producer, Producer + ".csproj");
        File.Copy(Path.Combine(BuildTest.Root, "test/fixtures/nativeaot/codegen-build/Producer.csproj.in"), packages.ProducerProject);
        File.Copy(Path.Combine(BuildTest.Root, "test/fixtures", Producer, "ProducerExports.cs"), Path.Combine(producer, "ProducerExports.cs"));
        await packages.PackProject(Producer, packages.ProducerProject);
        var intermediate = Path.Combine(packages.Artifacts, "build", "obj", Producer);
        packages.VerifyAssets(Path.Combine(intermediate, "project.assets.json"), false, [Producer, .. CompilerPackages], ["Elsa.Features"]);
        var helpers = Directory.GetFiles(intermediate, "ElsaFeatureRegistrations.g.cs", SearchOption.AllDirectories);
        Require(helpers.Length == 2, "Expected producer bindings for both target frameworks.");
        foreach (var helper in helpers)
        {
            var source = File.ReadAllText(helper);
            Require(source.Contains(".UnselectedFeature>(", StringComparison.Ordinal) &&
                    source.Contains(".FoundationFeature>(", StringComparison.Ordinal),
                $"Missing selected or unselected feature in {helper}.");
        }
        return packages;
    }

    private async Task PackProject(string id, string project)
    {
        var result = await BuildTest.Run(Path.GetDirectoryName(project)!, Path.Combine(Artifacts, $"pack-{id}.log"), "dotnet",
            ["pack", project, .. PackProperties, "-p:EmitCompilerGeneratedFiles=true", $"-bl:{Artifacts}/pack-{id}.binlog"]);
        result.EnsureSuccess();
        VerifyLayout(id);
    }

    // An external project can consume only the public NuGet assets. Give every test its own SDK inputs.
    public string PrepareConsumer(string name)
    {
        var directory = Consumers.Value.CreateSubdirectory($"{name}-{Guid.NewGuid():N}").FullName;
        File.Copy(Path.Combine(BuildTest.Root, "global.json"), Path.Combine(directory, "global.json"));
        // Reuse installed SDK dependencies while keeping freshly staged packages out of the user's cache.
        using var features = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Artifacts, "build", "obj", "Elsa.Features", "project.assets.json")));
        var caches = features.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name);
        new XElement("Project", new XElement("PropertyGroup",
            new XElement("RestorePackagesPath", Path.Combine(Artifacts, "consumer-packages")),
            new XElement("RestoreAdditionalProjectFallbackFolders", string.Join(';', caches))))
            .Save(Path.Combine(directory, "Directory.Build.props"));
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        var ids = RuntimePackages.Concat(CompilerPackages).ToArray();
        var central = XElement.Load(Path.Combine(BuildTest.Root, "Directory.Packages.props"));
        central.Add(new XElement("ItemGroup", ids.Select(id =>
            new XElement("PackageVersion", new XAttribute("Include", id), new XAttribute("Version", $"[{Version}]")))));
        central.Save(Path.Combine(directory, "Directory.Packages.props"));
        CreateNuGetConfig(Feed).Save(Path.Combine(directory, "NuGet.Config"));
        return directory;
    }

    [After(Assembly)]
    public static void RemoveTemporaryConsumers()
    {
        if (Consumers.IsValueCreated)
            Consumers.Value.Delete(recursive: true);
    }

    internal static XElement CreateNuGetConfig(string feed) =>
        new XElement("configuration",
            new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", feed)),
                new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
            new XElement("packageSourceMapping", new XElement("clear"),
                new XElement("packageSource", new XAttribute("key", "fixture"),
                    RuntimePackages.Concat(CompilerPackages).Select(id => new XElement("package", new XAttribute("pattern", id)))),
                new XElement("packageSource", new XAttribute("key", "nuget.org"), new XElement("package", new XAttribute("pattern", "*")))));

    private string ArchivePath(string id) => Path.Combine(Feed, $"{id}.{Version}.nupkg");

    private void VerifyLayout(string id)
    {
        using var archive = ZipFile.OpenRead(ArchivePath(id));
        if (id != "Elsa.Features" && id != Producer)
        {
            using var nuspec = archive.Entries.Single(e => e.Name.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            Require(!XElement.Load(nuspec).Descendants().Any(e => e.Name.LocalName == "dependency"), $"{id} declares package dependencies.");
        }
        if (RuntimePackages.Contains(id))
        {
            if (id == "Elsa.Features")
            {
                foreach (var toolName in new[] { "Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers", "RegistrationCompiler" })
                    Require(archive.GetEntry($"tools/roslyn/{toolName}.dll") != null, $"Missing bundled {toolName}.");
                foreach (var suffix in new[] { "props", "targets" })
                    Require(archive.GetEntry($"buildTransitive/{id}.{suffix}") != null, $"Missing bundled {suffix}.");
            }
            Require(!archive.Entries.Any(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) &&
                e.Name.EndsWith(".dll", StringComparison.Ordinal) && e.Name != id + ".dll"), $"{id} ships extra runtime assemblies.");
            foreach (var tfm in new[] { "net10.0", "net11.0" })
                Require(archive.GetEntry($"lib/{tfm}/{id}.dll") != null, $"{id} lacks the {tfm} assembly.");
            return;
        }
        Require(!archive.Entries.Any(e => e.FullName.StartsWith("lib/") || e.Name.Contains("Microsoft.CodeAnalysis")),
            $"{id} ships runtime or compiler-host assemblies.");
        if (id == BuildPackage)
        {
            foreach (var suffix in new[] { "props", "targets" })
                Require(archive.GetEntry($"buildTransitive/{id}.{suffix}") != null, $"{id} lacks transitive {suffix}.");
            return;
        }
        var binary = archive.GetEntry($"analyzers/dotnet/cs/{id}.dll") ?? throw new InvalidDataException($"Missing analyzer {id}.");
        using var assembly = binary.Open();
        using var image = new MemoryStream();
        assembly.CopyTo(image);
        image.Position = 0;
        using var reader = new PEReader(image);
        var metadata = reader.GetMetadataReader();
        var targetFramework = metadata.GetAssemblyDefinition().GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Where(a => a.Constructor.Kind == HandleKind.MemberReference)
            .Single(a =>
            {
                var type = metadata.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
                if (type.Kind != HandleKind.TypeReference) return false;
                var reference = metadata.GetTypeReference((TypeReferenceHandle)type);
                return metadata.StringComparer.Equals(reference.Name, "TargetFrameworkAttribute")
                    && metadata.StringComparer.Equals(reference.Namespace, "System.Runtime.Versioning");
            });
        var value = metadata.GetBlobReader(targetFramework.Value);
        Require(value.ReadUInt16() == 1 && value.ReadSerializedString() == ".NETStandard,Version=v2.0",
            $"{id} must target netstandard2.0.");
    }

    public static void VerifyLock(string path)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(path));
        var groups = file.RootElement.GetProperty("dependencies");
        foreach (var tfm in new[] { "net10.0", "net11.0" })
            Require(groups.TryGetProperty(tfm, out _), $"Missing {tfm} in {path}.");
    }

    public void VerifyAssets(string assetsFile, bool allowSourceLibrary, string[] omitted, string[]? directElsaPackages = null)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(assetsFile));
        var assets = file.RootElement;
        if (directElsaPackages != null)
        {
            foreach (var framework in assets.GetProperty("project").GetProperty("frameworks").EnumerateObject())
            {
                var references = framework.Value.GetProperty("dependencies").EnumerateObject().Select(p => p.Name)
                    .Where(id => id.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase)).ToArray();
                Require(references.SequenceEqual(directElsaPackages), $"Unexpected direct Elsa references for {framework.Name}: {string.Join(", ", references)}.");
            }
        }
        var libraries = assets.GetProperty("libraries");
        foreach (var library in libraries.EnumerateObject())
        {
            var type = library.Value.GetProperty("type").GetString();
            Require(type == "package" || allowSourceLibrary && type == "project" && library.Name.StartsWith("SourceLibrary/"),
                $"Unexpected project reference {library.Name} in {assetsFile}.");
            Require(!library.Name.StartsWith("Microsoft.CodeAnalysis"), $"Roslyn leaked into {assetsFile}.");
        }
        var frameworks = assets.GetProperty("project").GetProperty("restore").GetProperty("frameworks");
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("projectReferences", out var references)) continue;
            var sourceLibrary = Path.Combine(Path.GetDirectoryName(assets.GetProperty("project").GetProperty("restore")
                .GetProperty("projectPath").GetString())!, "SourceLibrary", "SourceLibrary.csproj");
            foreach (var reference in references.EnumerateObject())
                Require(allowSourceLibrary && Path.GetFullPath(reference.Name) == sourceLibrary,
                    $"Unexpected restore project reference {reference.Name} in {assetsFile}.");
        }
        foreach (var id in RuntimePackages.Concat(CompilerPackages))
        {
            var present = libraries.TryGetProperty($"{id}/{Version}", out var package);
            if (omitted.Contains(id))
            {
                Require(!libraries.EnumerateObject().Any(p => p.Name.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase)),
                    $"Excluded package {id} was resolved.");
                continue;
            }
            Require(present, $"Missing {id}/{Version} in {assetsFile}.");
            using var archive = File.OpenRead(ArchivePath(id));
            var hash = Convert.ToBase64String(SHA512.HashData(archive));
            Require(package.GetProperty("sha512").GetString() == hash, $"{id} resolved different bytes from the staged package.");
            var relative = package.GetProperty("path").GetString()!;
            var installed = assets.GetProperty("packageFolders").EnumerateObject()
                .Select(p => Path.Combine(p.Name, relative, $"{id.ToLowerInvariant()}.{Version}.nupkg.sha512"));
            Require(installed.Any(p => File.Exists(p) && File.ReadAllText(p).Trim() == hash),
                $"Installed package hash mismatch for {id}.");
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidDataException(message);
    }
}
