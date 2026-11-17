#!/usr/bin/env bash
# Pack real runtime/producer assemblies and execute an external package consumer.
set -uo pipefail

usage() {
    cat <<'USAGE'
Usage: scripts/nativeaot/test-module-packages.sh [options]
  --mode all|managed|native           Default: all.
  --framework all|net10.0|net11.0     Default: all.
  --runtime-async all|default|disabled Default: all.
  --artifacts-root <directory>        Default: artifacts/nativeaot/module-packages.

Default coverage is managed/native on both targets, plus a separately rebuilt
net11.0 native runtime-async opt-out. Packages and consumer locks are unique to
this run. The consumer has no project references or repository build imports.
USAGE
}
fail() { printf '%s\n' "$*" >&2; exit 64; }
require_value() { [ "$#" -ge 2 ] && [ -n "$2" ] || fail "Missing value for $1"; }
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd) || exit 1
repo_root=$(cd -- "$script_dir/../.." && pwd) || exit 1
cd -- "$repo_root" || exit 1
requested_mode=all requested_framework=all requested_async=all
artifacts_root="$repo_root/artifacts/nativeaot/module-packages"
while [ "$#" -gt 0 ]; do
    case "$1" in
        --mode) require_value "$@"; requested_mode=$2; shift 2 ;;
        --framework) require_value "$@"; requested_framework=$2; shift 2 ;;
        --runtime-async) require_value "$@"; requested_async=$2; shift 2 ;;
        --artifacts-root) require_value "$@"; artifacts_root=$2; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done
case "$requested_mode" in all|managed|native) ;; *) fail 'Invalid --mode' ;; esac
case "$requested_framework" in all|net10.0|net11.0) ;; *) fail 'Invalid --framework' ;; esac
case "$requested_async" in all|default|disabled) ;; *) fail 'Invalid --runtime-async' ;; esac
if [ "$requested_async" = disabled ] && [ "$requested_framework" = net10.0 ]; then fail 'Runtime-async opt-out coverage targets net11.0'; fi
framework_count=1; mode_count=1
if [ "$requested_framework" = all ]; then framework_count=2; fi
if [ "$requested_mode" = all ]; then mode_count=2; fi
expected_lanes=0
if [ "$requested_async" != disabled ]; then expected_lanes=$((framework_count * mode_count)); fi
if [ "$requested_async" != default ] && [ "$requested_framework" != net10.0 ]; then expected_lanes=$((expected_lanes + 1)); fi
completed=0
for dependency in dotnet python3; do command -v "$dependency" >/dev/null 2>&1 || { printf '%s is required\n' "$dependency" >&2; exit 69; }; done
case "$(uname -s)" in Darwin) host_os=osx ;; Linux) host_os=linux ;; *) fail 'Unsupported native host OS' ;; esac
case "$(uname -m)" in arm64|aarch64) host_arch=arm64 ;; x86_64|amd64) host_arch=x64 ;; *) fail 'Unsupported native host architecture' ;; esac
rid=$host_os-$host_arch
mkdir -p -- "$artifacts_root" || exit $?
artifacts_root=$(cd -- "$artifacts_root" && pwd) || exit 1
workspace=$(mktemp -d "$artifacts_root/run.XXXXXX") || exit $?
phase=prerequisites
finish() {
    status=$?
    trap - EXIT
    printf '{"phase":"%s","expectedLanes":%s,"completedLanes":%s,"exitCode":%s}\n' \
        "$phase" "$expected_lanes" "$completed" "$status" > "$workspace/result.json"
    printf 'Packed module evidence: %s (exit %s, phase %s)\n' "$workspace" "$status" "$phase"
    exit "$status"
}
trap finish EXIT
run_logged() {
    local logfile=$1
    shift
    "$@" 2>&1 | tee "$logfile"
    local statuses=("${PIPESTATUS[@]}")
    [ "${statuses[0]}" -eq 0 ] || return "${statuses[0]}"
    return "${statuses[1]}"
}
verify_protocol() {
    local logfile=$1 mode=$2 framework=$3 major dynamic_code=True
    major=${framework#net}; major=${major%%.*}
    if [ "$mode" = native ]; then dynamic_code=False; fi
    awk -v header="probe=module-packages provider=packed-modules build-mode=$mode expected-mode=$mode" \
        -v runtime="^framework=[.]NET $major[.][^ ]+ rid=$rid dynamic-code=$dynamic_code reflection-json=False$" '
        NF { last = $0 }
        $0 == header { if (state != 0) invalid = 1; state = 1; next }
        /^framework=/ { if (state != 1 || $0 !~ runtime) invalid = 1; state = 2; next }
        $0 == "scenario=export-composition result=passed" { if (state != 2) invalid = 1; state = 3; next }
        $0 == "scenario=host-isolation result=passed" { if (state != 3) invalid = 1; state = 4; next }
        $0 == "scenario=hosted-lifecycle result=passed" { if (state != 4) invalid = 1; state = 5; next }
        $0 == "scenario=missing-export result=passed" { if (state != 5) invalid = 1; state = 6; next }
        $0 == "probe=module-packages result=passed scope=packed-modules" { if (state != 6) invalid = 1; state = 7; next }
        /^(probe=|scenario=)/ { invalid = 1 }
        END { exit !(state == 7 && !invalid && last == "probe=module-packages result=passed scope=packed-modules") }
    ' "$logfile"
}
run_logged "$workspace/dotnet-info.log" dotnet --info || exit $?
if [ "$requested_mode" != managed ]; then
    command -v clang >/dev/null 2>&1 || { printf '%s\n' 'Native package-consumer validation requires clang' >&2; exit 69; }
    run_logged "$workspace/native-toolchain.log" clang --version || exit $?
fi
helper="$script_dir/module-consumer/package-fixture.py"
frameworks=(net10.0 net11.0)
if [ "$requested_framework" != all ]; then frameworks=("$requested_framework"); fi
modes=(managed native)
if [ "$requested_mode" != all ]; then modes=("$requested_mode"); fi
async_models=(default disabled)
if [ "$requested_async" != all ]; then async_models=("$requested_async"); fi
stamp=$(date -u +%Y%m%d%H%M%S)
for async_model in "${async_models[@]}"; do
    # The additional default-matrix opt-out is net11 native; net10 has no opt-in.
    if [ "$async_model" = disabled ] && [ "$requested_framework" = net10.0 ]; then continue; fi
    model_root="$workspace/$async_model"
    feed="$model_root/feed"
    # Keep the consuming project physically outside the source repository.
    consumer=$(mktemp -d "${TMPDIR:-/tmp}/flamme-module-consumer-$async_model.XXXXXX") || exit $?
    mkdir -p "$feed" || exit $?
    printf '%s\n' "$consumer" > "$model_root/consumer-path.txt"
    version="0.0.0-g01.$stamp.$$.$async_model"
    async_options=()
    if [ "$async_model" = disabled ]; then async_options=(-p:UseRuntimeAsync=false); fi
    phase=pack
    for project in \
        src/common/Elsa.CodeGeneration.Abstractions/Elsa.CodeGeneration.Abstractions.csproj \
        src/common/Elsa.Features/Elsa.Features.csproj \
        test/fixtures/Elsa.NativeAot.Modules.Producer/Elsa.NativeAot.Modules.Producer.csproj; do
        name=${project##*/}; name=${name%.csproj}
        run_logged "$model_root/pack-$name.log" dotnet pack "$repo_root/$project" --configuration Release \
            --artifacts-path "$model_root/producer-build" -p:IsPackable=true "-p:PackageOutputPath=$feed" \
            "-p:Version=$version" "-p:PackageVersion=$version" "-bl:$model_root/pack-$name.binlog" \
            "${async_options[@]}" || exit $?
        for producer_framework in net10.0 net11.0; do
            run_logged "$model_root/properties-$name-$producer_framework.log" dotnet msbuild "$repo_root/$project" -nologo \
                "-p:TargetFramework=$producer_framework" "-p:Version=$version" "-p:PackageVersion=$version" \
                "${async_options[@]}" -p:IsPackable=true -p:UseArtifactsOutput=true "-p:ArtifactsPath=$model_root/producer-build" \
                -getProperty:TargetFramework,UseRuntimeAsync,Features,WeaverConfiguration,IsPackable,PackageId,PackageVersion || exit $?
        done
    done
    run_logged "$model_root/package-layout.log" python3 "$helper" packages "$feed" "$version" || exit $?
    run_logged "$model_root/prepare.log" python3 "$helper" prepare "$repo_root" "$consumer" "$feed" "$version" || exit $?
    cp -R "$consumer" "$model_root/consumer-source" || exit $?

    phase=missing-package-control
    negative="$model_root/missing-package"
    run_logged "$model_root/missing-package-preparation.log" python3 "$helper" missing "$negative" "$version" || exit $?
    run_logged "$model_root/missing-package.log" dotnet restore "$negative/Missing.csproj" --configfile "$negative/NuGet.Config" \
        --packages "$negative/packages" -p:RestoreFallbackFolders= -p:NuGetAudit=false
    missing_status=$?
    if [ "$missing_status" -eq 0 ] || ! grep -Eq 'NU110[12].*Elsa.NativeAot.Modules.Producer' "$model_root/missing-package.log"; then
        printf '%s\n' 'Missing-package control failed to reject the unavailable producer package' >&2
        exit 65
    fi

    for mode in "${modes[@]}"; do
        if [ "$async_model" = disabled ] && [ "$requested_mode" = all ] && [ "$mode" = managed ]; then continue; fi
        for framework in "${frameworks[@]}"; do
            if [ "$async_model" = disabled ] && [ "$framework" != net11.0 ]; then continue; fi
            lane="$model_root/$mode/$framework"
            mkdir -p "$lane" || exit $?
            common=("-p:NativeAotMode=$mode" "-p:RuntimeIdentifier=$rid" -p:NoWarn=CS1591 \
                -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true -p:TrimmerSingleWarn=false \
                -p:SuppressTrimAnalysisWarnings=false -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true \
                "${async_options[@]}")
            phase=consumer-lock
            # This is a disposable consumer with run-specific package versions.
            # Generate both TFM groups, then prove the same restore is locked.
            (cd -- "$consumer" && run_logged "$lane/lock-generation.log" dotnet restore Consumer.csproj --force-evaluate \
                --artifacts-path "$lane/build" "${common[@]}") || exit $?
            run_logged "$lane/lock-validation.log" python3 "$helper" lock "$consumer/locks/$mode/$rid/packages.lock.json" || exit $?
            phase=consumer-restore
            (cd -- "$consumer" && run_logged "$lane/restore.log" dotnet restore Consumer.csproj --locked-mode \
                --artifacts-path "$lane/build" "${common[@]}") || exit $?
            run_logged "$lane/package-provenance.log" python3 "$helper" assets "$lane/build/obj/Consumer/project.assets.json" "$feed" "$version" || exit $?
            (cd -- "$consumer" && run_logged "$lane/properties.log" dotnet msbuild Consumer.csproj -nologo \
                "-p:TargetFramework=$framework" "${common[@]}" -p:UseArtifactsOutput=true "-p:ArtifactsPath=$lane/build" \
                -getProperty:TargetFramework,NativeAotMode,PublishAot,UseRuntimeAsync,Features,WeaverConfiguration,NuGetLockFilePath) || exit $?
            phase=consumer-publish
            (cd -- "$consumer" && run_logged "$lane/publish.log" dotnet publish Consumer.csproj --configuration Release \
                --framework "$framework" --no-restore --artifacts-path "$lane/build" --output "$lane/publish" \
                "-bl:$lane/publish.binlog" "${common[@]}") || exit $?
            phase=consumer-execute
            run_logged "$lane/execution.log" "$lane/publish/Consumer" --expect-mode "$mode" || exit $?
            phase=consumer-evidence
            verify_protocol "$lane/execution.log" "$mode" "$framework" || { printf '%s\n' 'Incomplete packed-consumer result protocol' >&2; exit 65; }
            if grep -Eq '(warning|error) IL[0-9]+' "$lane/publish.log"; then
                printf '%s\n' 'Unresolved consumer IL diagnostics remain' >&2
                exit 65
            fi
            sed 's/provider=packed-modules/provider=tampered/' "$lane/execution.log" > "$lane/tampered-output.log"
            if verify_protocol "$lane/tampered-output.log" "$mode" "$framework"; then printf '%s\n' 'Tampered-result control was accepted' >&2; exit 65; fi
            phase=injected-failure-control
            run_logged "$lane/injected-failure.log" "$lane/publish/Consumer" --expect-mode "$mode" --self-test-failure
            injected_status=$?
            if [ "$injected_status" -ne 1 ] || ! grep -q 'Injected packed consumer assertion failure' "$lane/injected-failure.log"; then
                printf '%s\n' 'Injected consumer assertion did not fail as expected' >&2
                exit 65
            fi
            printf '{"mode":"%s","framework":"%s","rid":"%s","runtimeAsync":"%s","packageVersion":"%s","exitCode":0}\n' \
                "$mode" "$framework" "$rid" "$async_model" "$version" > "$lane/result.json"
            completed=$((completed + 1))
        done
    done
    cp -R "$consumer/locks" "$model_root/consumer-locks" || exit $?
done
if [ "$completed" -ne "$expected_lanes" ]; then
    printf 'Incomplete package-consumer matrix: expected %s lanes, completed %s\n' "$expected_lanes" "$completed" >&2
    exit 65
fi
phase=completed
printf 'Passed %s/%s package-consumer lanes.\n' "$completed" "$expected_lanes"
