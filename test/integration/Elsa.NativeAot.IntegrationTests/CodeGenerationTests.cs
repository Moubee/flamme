namespace Elsa.NativeAot.IntegrationTests;

[NotInParallel("NativeAotBuild")]
public class ManagedCodeGenerationTests
{
    [Test]
    [Arguments("Auto", "Dynamic")]
    [Arguments("Dynamic", "Dynamic")]
    [Arguments("Generated", "Generated")]
    public async Task ManagedCompilerSelectsProvider(string mode, string provider)
    {
        var consumer = await CodeGenerationConsumer.Create($"managed-{mode}", compiledFeatures: true);
        await consumer.Verify(provider, [$"-p:ElsaExecutionMode={mode}", "-p:PublishAot=false"]);
    }
}

[Explicit]
[Category("NativeAotAcceptance")]
[NotInParallel("NativeAotBuild")]
public class CodeGenerationTests
{
    [Test]
    [Arguments("Dynamic")]
    [Arguments("Generated")]
    public async Task ManualBootstrapRunsWithoutTheGenerator(string mode)
    {
        var consumer = await CodeGenerationConsumer.Create($"manual-{mode}", manual: true);
        await consumer.Verify(mode, [$"-p:ElsaExecutionMode={mode}", "-p:ElsaGenerateMetadata=false", "-p:FixtureIncludeGenerator=false", "-p:PublishAot=false"]);
    }

#if NET11_0_OR_GREATER
    [Test]
    public async Task RuntimeAsyncOptOutRunsPackedFeatures()
    {
        foreach (var (mode, native) in new[] { ("Dynamic", false), ("Generated", false), ("Generated", true) })
        {
            var consumer = await CodeGenerationConsumer.Create($"async-disabled-{mode}-{native}", compiledFeatures: true, disableRuntimeAsync: true);
            await consumer.Verify(mode, [$"-p:ElsaExecutionMode={mode}", "-p:UseRuntimeAsync=false"], native);
        }
    }

#endif

    [Test]
    public async Task UnselectedDynamicCodeProducerExposesItsNativeRequirement()
    {
        var consumer = await CodeGenerationConsumer.Create("optional-dynamic-plugin", compiledFeatures: true, unsafeProducer: true);
        await consumer.Verify("Generated", ["-p:ElsaExecutionMode=Generated"]);
        string[] native = ["-p:PublishAot=true", $"-p:RuntimeIdentifier={BuildTest.Rid}"];
        await consumer.RestoreLocked(native);
        var publish = await consumer.Build(native, native: true);
        await Assert.That(publish.ExitCode).IsNotEqualTo(0);
        await Assert.That(publish.Output).Matches(@"error IL3050[^\r\n]*OptionalPlugin\.DynamicFeature");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AutoDefaultsToDynamicWithOrWithoutTrimming(bool trimmed)
    {
        var consumer = await CodeGenerationConsumer.Create($"auto-trim-{trimmed}");
        await consumer.Verify("Dynamic", trimmed ? ["-p:PublishTrimmed=true", "-p:PublishAot=false"] : []);
    }

    [Test]
    public async Task ModeAndGenerationSettingsAreCaseInsensitive()
    {
        var consumer = await CodeGenerationConsumer.Create("case-insensitive");
        await consumer.Verify("Generated", ["-p:ElsaExecutionMode=gEnErAtEd", "-p:ElsaGenerateMetadata=TrUe"]);
    }

    [Test]
    [Arguments("Dynamic")]
    [Arguments("Generated")]
    public async Task AnalyzerAcceptsParenthesizedManualReturn(string mode)
    {
        var consumer = await CodeGenerationConsumer.Create($"manual-parenthesized-{mode}", manual: true);
        await consumer.Verify(mode, [$"-p:ElsaExecutionMode={mode}", "-p:FixtureHostCase=FIXTURE_PARENTHESIZED_RETURN"]);
    }

    [Test]
    public async Task LateDirectoryBuildTargetsSelectsGeneratedBootstrap()
    {
        var consumer = await CodeGenerationConsumer.Create("late-app-setting", lateMode: true);
        await consumer.Verify("Generated", []);
    }

    [Test]
    [Arguments(true, "Dynamic")]
    [Arguments(false, "Generated")]
    public async Task SourceLibraryOnlyInheritsTheGlobalMode(bool appLocal, string childProvider)
    {
        var consumer = await CodeGenerationConsumer.Create($"source-library-{childProvider}", sourceLibrary: true, localMode: appLocal);
        await consumer.Verify("Generated", appLocal ? [] : ["-p:ElsaExecutionMode=Generated"]);
        await consumer.VerifyChildMode(childProvider);
    }

    [Test]
    public async Task NonHostExecutableCanSupplyAnOrdinaryManualEntryPoint()
    {
        var consumer = await CodeGenerationConsumer.Create("explicit-nonhost", manual: true);
        await consumer.Verify("Dynamic", ["-p:ElsaIsHost=false", "-p:FixtureHostCase=FIXTURE_WRONG_HOOK"]);
    }

    [Test]
    [Arguments("ElsaExecutionMode=invalid", "ELSACG001")]
    [Arguments("ElsaGenerateMetadata=invalid", "ELSACG002")]
    [Arguments("ElsaIsHost=invalid", "ELSACG003")]
    [Arguments("ElsaHostBootstrap=invalid", "ELSACG004")]
    [Arguments("ElsaGenerateMetadata=false", "ELSACG006")]
    [Arguments("FixtureIncludeGenerator=false", "ELSACG007")]
    [Arguments("FixtureIncludeAnalyzer=false", "ELSACG008")]
    [Arguments("RunAnalyzersDuringBuild=false", "ELSACG009")]
    [Arguments("GenerateMSBuildEditorConfigFile=false", "ELSACG010")]
    [Arguments("FixtureIncludeBuild=false", "ELSAGEN001")]
    public async Task InvalidBuildConfigurationReportsItsDiagnostic(string property, string diagnostic)
    {
        var consumer = await CodeGenerationConsumer.Create(diagnostic, manual: diagnostic == "ELSAGEN001");
        await VerifyFailure(consumer, [$"-p:{property}"], diagnostic);
    }

    [Test]
    public async Task NativePublishRejectsDynamicMode()
    {
        var consumer = await CodeGenerationConsumer.Create("dynamic-aot");
        await VerifyFailure(consumer, ["-p:ElsaExecutionMode=Dynamic", "-p:PublishAot=true", $"-p:RuntimeIdentifier={BuildTest.Rid}"], "ELSACG005");
    }

    [Test]
    [Arguments("FIXTURE_MISSING_EXPORT_HOOK", "ELSAGEN003")]
    [Arguments("FIXTURE_NULLABLE_ENTRY", "ELSAGEN002")]
    public async Task InvalidCompilerBootstrapReportsItsDiagnostic(string hostCase, string diagnostic)
    {
        var consumer = await CodeGenerationConsumer.Create(hostCase);
        await VerifyFailure(consumer, ["-p:ElsaExecutionMode=Generated", $"-p:FixtureHostCase={hostCase}"], diagnostic);
    }

    [Test]
    [Arguments("FIXTURE_WRONG_HOOK", false)]
    [Arguments("FIXTURE_WRONG_HOOK", true)]
    [Arguments("FIXTURE_DISCARD_EXPORT", false)]
    [Arguments("FIXTURE_UNREACHABLE_EXPORT", false)]
    public async Task ManualBootstrapMustReturnTheSelectedProvider(string hostCase, bool generatedFilename)
    {
        var consumer = await CodeGenerationConsumer.Create($"manual-{hostCase}", manual: true, generatedFilename: generatedFilename);
        await VerifyFailure(consumer, ["-p:ElsaExecutionMode=Generated", $"-p:FixtureHostCase={hostCase}"], "ELSAGEN004");
    }

    private static async Task VerifyFailure(CodeGenerationConsumer consumer, string[] settings, string diagnostic)
    {
        // NuGet's imported compiler items may first become available after restore.
        var result = await consumer.Restore(settings);
        if (result.ExitCode == 0)
            result = await consumer.Build(settings);
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains($"error {diagnostic}:");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyExportsFailRuntimeAdmission(bool manual)
    {
        var consumer = await CodeGenerationConsumer.Create($"empty-exports-manual-{manual}", manual);
        string[] settings = ["-p:ElsaExecutionMode=Generated", "-p:FixtureHostCase=FIXTURE_EMPTY_EXPORTS"];
        if (manual)
            settings = [.. settings, "-p:ElsaGenerateMetadata=false", "-p:FixtureIncludeGenerator=false"];

        await consumer.RestoreLocked(settings);
        (await consumer.Build(settings)).EnsureSuccess();
        var result = await consumer.Execute("Generated");
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("probe=codegen-build result=failed");
        await Assert.That(result.Output).Contains("at least one selected FeatureModuleExport");
    }

    [Test]
    public async Task IncrementalBuildSwitchesProvidersInTheSameOutputDirectory()
    {
        var consumer = await CodeGenerationConsumer.Create("mode-transitions");
        foreach (var mode in new[] { "Dynamic", "Generated", "Dynamic" })
        {
            string[] settings = [$"-p:ElsaExecutionMode={mode}", "-p:PublishAot=false"];
            await consumer.RestoreLocked(settings);
            (await consumer.Build(settings)).EnsureSuccess();
            await consumer.VerifyExecution(mode);
        }
    }

    [Test]
    [Arguments("Auto", false)]
    [Arguments("Generated", false)]
    [Arguments("Generated", true)]
    public async Task NativeConsumerRunsGeneratedProvider(string mode, bool manual)
    {
        var consumer = await CodeGenerationConsumer.Create($"native-{mode}-manual-{manual}", manual, compiledFeatures: !manual);
        string[] settings = [$"-p:ElsaExecutionMode={mode}"];
        if (manual)
            settings = [.. settings, "-p:ElsaGenerateMetadata=false", "-p:FixtureIncludeGenerator=false"];
        await consumer.Verify("Generated", settings, native: true);
    }
}
