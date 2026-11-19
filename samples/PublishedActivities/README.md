# Publish two producers' activities

This external .NET application references two locally packed NuGet packages. It grants `north.mail` and `south.mail` to different publishers and imports both producers' `Send` operations into one workflow. North writes a line, waits for `Approved`, and writes its release-specific receipt. South writes its own receipt. Both recipes use contract major 1.

Pack the solution from the repository root with the SDK in `global.json`.

```sh
dotnet pack Flamme.slnx -c Release -p:Version=3.9.0-local.1 -o "$PWD/packages"
```

Set `FLAMME_FEED` to the absolute path of the resulting `packages` directory. Copy this sample to an empty directory outside the repository. Its NuGet configuration maps the Flamme family to that feed and restores third-party dependencies from NuGet.org.

Set `FLAMME_SAMPLE_DB` to an Npgsql connection string for a disposable PostgreSQL database. The sample's `init` command applies the management and runtime migrations. The verification script chooses a unique definition ID and launches a fresh host process for every operation.

```sh
export NUGET_PACKAGES="$PWD/.nuget-packages"
dotnet restore --configfile NuGet.Config
dotnet build -c Release --no-restore
python3 verify.py bin/Release/net10.0/PublishedActivities.dll
python3 verify.py bin/Release/net11.0/PublishedActivities.dll
```

In each run, the script publishes a first revision, starts an instance, publishes a second revision with a different North receipt, and delivers the event to the waiting instance. It checks the instance and revision IDs, output order, incident count, consumed bookmark and repeat delivery. It then starts a new instance and round-trips the first revision through the workflow definition exporter and importer.

To inspect the result yourself, choose a fresh `FLAMME_SAMPLE_DEFINITION`. Run `init`, `publish 1.0.0`, `start`, `publish 1.1.0`, and `resume INSTANCE_ID` as arguments to the built application. Use `export 1 first.json` to see the expanded primitive IDs and their producer source map.
