using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

internal sealed class PackageFeed(bool disableRuntimeAsync)
{
    private const string Producer = "Elsa.NativeAot.Modules.Producer";
    private const string Abstractions = "Elsa.CodeGeneration.Abstractions";
    private const string BuildPackage = "Elsa.CodeGeneration.Build";
    private static readonly string[] RuntimePackages = [Abstractions, "Elsa.Features", Producer];
    private static readonly string[] CompilerPackages = ["Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers", BuildPackage];
    private static readonly Lazy<Task<PackageFeed>> Default = new(() => Pack(false));
    private static readonly Lazy<Task<PackageFeed>> AsyncDisabled = new(() => Pack(true));

    public string Version { get; } = $"0.0.0-test.{Guid.NewGuid():N}";
    public string Artifacts { get; } = BuildTest.CreateDirectory(disableRuntimeAsync ? "pack-async-disabled" : "pack");
    public string Feed => Path.Combine(Artifacts, "feed");

    public static Task<PackageFeed> Get(bool disableRuntimeAsync = false) =>
        (disableRuntimeAsync ? AsyncDisabled : Default).Value;

    private static async Task<PackageFeed> Pack(bool disableRuntimeAsync)
    {
        var packages = new PackageFeed(disableRuntimeAsync);
        Directory.CreateDirectory(packages.Feed);
        foreach (var id in RuntimePackages.Concat(CompilerPackages))
        {
            var project = Path.Combine(BuildTest.Root, id == Producer ? "test/fixtures" : "src/common", id, id + ".csproj");
            string[] options = disableRuntimeAsync ? ["-p:UseRuntimeAsync=false"] : [];
            var result = await BuildTest.Run(BuildTest.Root, Path.Combine(packages.Artifacts, $"pack-{id}.log"), "dotnet",
                ["pack", project, "-c", "Release", "--artifacts-path", Path.Combine(packages.Artifacts, "build"),
                "-p:IsPackable=true", $"-p:Version={packages.Version}", $"-p:PackageVersion={packages.Version}",
                $"-p:PackageOutputPath={packages.Feed}", $"-bl:{packages.Artifacts}/pack-{id}.binlog", ..options]);
            result.EnsureSuccess();
            packages.VerifyLayout(id);
        }
        return packages;
    }

    // An external project can consume only the public NuGet assets. Give every test its own SDK inputs.
    public string PrepareConsumer(string name)
    {
        var directory = Directory.CreateTempSubdirectory($"flamme-{name}-").FullName;
        File.Copy(Path.Combine(BuildTest.Root, "global.json"), Path.Combine(directory, "global.json"));
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        var ids = RuntimePackages.Concat(CompilerPackages).ToArray();
        var central = XElement.Load(Path.Combine(BuildTest.Root, "Directory.Packages.props"));
        central.Add(new XElement("ItemGroup", ids.Select(id =>
            new XElement("PackageVersion", new XAttribute("Include", id), new XAttribute("Version", $"[{Version}]")))));
        central.Save(Path.Combine(directory, "Directory.Packages.props"));
        new XElement("configuration",
            new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", Feed)),
                new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
            new XElement("packageSourceMapping", new XElement("clear"),
                new XElement("packageSource", new XAttribute("key", "fixture"),
                    ids.Select(id => new XElement("package", new XAttribute("pattern", id)))),
                new XElement("packageSource", new XAttribute("key", "nuget.org"), new XElement("package", new XAttribute("pattern", "*")))))
            .Save(Path.Combine(directory, "NuGet.Config"));
        return directory;
    }

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

    public void VerifyAssets(string assetsFile, bool allowSourceLibrary = false, params string[] omitted)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(assetsFile));
        var assets = file.RootElement;
        var libraries = assets.GetProperty("libraries");
        foreach (var library in libraries.EnumerateObject())
        {
            var type = library.Value.GetProperty("type").GetString();
            Require(type == "package" || allowSourceLibrary && type == "project" && library.Name.StartsWith("SourceLibrary/"),
                $"Unexpected project reference {library.Name} in {assetsFile}.");
            Require(!library.Name.StartsWith("Microsoft.CodeAnalysis"), $"Roslyn leaked into {assetsFile}.");
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
