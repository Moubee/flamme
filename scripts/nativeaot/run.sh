#!/usr/bin/env bash
# Diagnostic baseline: failures are evidence and retain their real exit status.
set -uo pipefail

usage() {
    cat <<'USAGE'
Usage: scripts/nativeaot/run.sh --probe features|execution|payloads|integrated [options]
  --provider legacy-baseline|explicit-catalog
                                 Feature provider (default: legacy-baseline).
                                 explicit-catalog is supported only by features.
  --mode managed|native           Publication mode (default: managed).
  --framework net10.0|net11.0      Published target (default: net11.0).
  --rid <RID>                     linux[-musl]-x64/arm64 or osx-x64/arm64.
                                 Defaults to the current host RID.
  --runtime-async default|disabled Keep repository policy or rebuild with opt-out.
  --artifacts-root <directory>    Default: artifacts/nativeaot beneath the repo.
  --update-lock                  Regenerate the selected complete lock, then stop.
  --check                        Fail on IL diagnostics; linker/ILC warnings are errors.
  --help                         Show this help.

These probes default to the legacy-baseline execution provider. The explicit
catalog validates a runtime seam; it does not establish generated-mode acceptance.
Normal runs restore locked dependencies, publish,
execute assertions, and return the original failing command's status. Native
publication and execution require a host matching the target OS and architecture.
Lock maintenance can select another host's RID without a native compiler.
USAGE
}

fail() { printf '%s\n' "$*" >&2; exit 64; }
require_value() { [ "$#" -ge 2 ] && [ -n "$2" ] || fail "Missing value for $1"; }

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd) || exit 1
repo_root=$(cd -- "$script_dir/../.." && pwd) || exit 1
# SDK selection follows the working directory's global.json, not the csproj path.
cd -- "$repo_root" || exit 1
probe= provider=legacy-baseline mode=managed framework=net11.0 rid= runtime_async=default
artifacts_root="$repo_root/artifacts/nativeaot"
update_lock=false check=false
while [ "$#" -gt 0 ]; do
    case "$1" in
        --probe) require_value "$@"; probe=$2; shift 2 ;;
        --provider) require_value "$@"; provider=$2; shift 2 ;;
        --mode) require_value "$@"; mode=$2; shift 2 ;;
        --framework) require_value "$@"; framework=$2; shift 2 ;;
        --rid) require_value "$@"; rid=$2; shift 2 ;;
        --runtime-async) require_value "$@"; runtime_async=$2; shift 2 ;;
        --artifacts-root) require_value "$@"; artifacts_root=$2; shift 2 ;;
        --update-lock) update_lock=true; shift ;;
        --check) check=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done
case "$probe" in
    features) app=Elsa.NativeAot.Features.Smoke ;;
    execution) app=Elsa.NativeAot.Execution.Smoke ;;
    payloads) app=Elsa.NativeAot.Payloads.Smoke ;;
    integrated) app=Elsa.NativeAot.Smoke ;;
    *) fail '--probe must select features, execution, payloads, or integrated' ;;
esac
case "$provider" in
    legacy-baseline) feature_provider=legacy ;;
    explicit-catalog) feature_provider=explicit ;;
    *) fail 'Invalid --provider' ;;
esac
if [ "$provider" = explicit-catalog ] && [ "$probe" != features ]; then
    fail 'The explicit-catalog provider is supported only by the features probe'
fi
case "$mode" in managed|native) ;; *) fail 'Invalid --mode' ;; esac
case "$framework" in net10.0|net11.0) ;; *) fail 'Invalid --framework' ;; esac
case "$runtime_async" in default|disabled) ;; *) fail 'Invalid --runtime-async' ;; esac
case "$(uname -s)" in Darwin) host_os=osx ;; Linux) host_os=linux ;; *) fail 'Unsupported host operating system' ;; esac
case "$(uname -m)" in arm64|aarch64) host_arch=arm64 ;; x86_64|amd64) host_arch=x64 ;; *) fail 'Unsupported host architecture' ;; esac
rid=${rid:-$host_os-$host_arch}
case "$rid" in osx-arm64|osx-x64|linux-arm64|linux-x64|linux-musl-arm64|linux-musl-x64) ;; *) fail 'Unsupported --rid' ;; esac
if [ "$update_lock" = false ] && { [ "${rid%%-*}" != "$host_os" ] || [ "${rid##*-}" != "$host_arch" ]; }; then
    fail 'Publication and execution require a RID matching the host OS and architecture'
fi
command -v dotnet >/dev/null 2>&1 || { printf '%s\n' 'dotnet is required' >&2; exit 69; }
project="$repo_root/src/apps/$app/$app.csproj"
[ -f "$project" ] || fail "Probe project does not exist: $project"
mkdir -p -- "$artifacts_root" || exit $?
artifacts_root=$(cd -- "$artifacts_root" && pwd) || exit 1
output_root="$artifacts_root/$provider/$probe/$mode/$rid/$runtime_async"
evidence="$output_root/evidence/$framework"
publish_dir="$output_root/publish/$framework"
rm -rf -- "$evidence" || exit $?
mkdir -p -- "$evidence" || exit $?
phase=prerequisites
finish() {
    status=$?
    trap - EXIT
    collect_diagnostics || { [ "$status" -ne 0 ] || status=74; }
    printf '%s\n' "$status" > "$evidence/exit-code.txt"
    printf '{"provider":"%s","probe":"%s","mode":"%s","framework":"%s","rid":"%s","runtimeAsync":"%s","phase":"%s","exitCode":%s}\n' \
        "$provider" "$probe" "$mode" "$framework" "$rid" "$runtime_async" "$phase" "$status" > "$evidence/result.json"
    printf 'Evidence: %s (exit %s, phase %s)\n' "$evidence" "$status" "$phase"
    exit "$status"
}
trap finish EXIT

# Capture output without replacing a failing command's status with tee's status.
run_logged() {
    local logfile=$1
    shift
    "$@" 2>&1 | tee "$logfile"
    local statuses=("${PIPESTATUS[@]}")
    [ "${statuses[0]}" -eq 0 ] || return "${statuses[0]}"
    return "${statuses[1]}"
}
collect_diagnostics() {
    # Full original lines remain available; no warning-count baseline is used.
    local logs=() file
    for file in "$evidence"/*.log; do
        [ -f "$file" ] && [ "$file" != "$evidence/il-diagnostics.log" ] && logs+=("$file")
    done
    if [ "${#logs[@]}" -eq 0 ]; then : > "$evidence/il-diagnostics.log"; return; fi
    awk '/(warning|error) IL[0-9]+/ { print }' "${logs[@]}" | LC_ALL=C sort -u > "$evidence/il-diagnostics.log"
}
run_logged "$evidence/dotnet-info.log" dotnet --info || exit $?
if [ "$mode" = native ] && [ "$update_lock" = false ]; then
    command -v clang >/dev/null 2>&1 || { printf '%s\n' 'Native publication requires clang and target development libraries' >&2; exit 69; }
    run_logged "$evidence/native-toolchain.log" clang --version || exit $?
fi

common=(
    "-p:NativeAotMode=$mode" "-p:NativeAotFeatureProvider=$feature_provider" "-p:RuntimeIdentifier=$rid"
    '-p:EnableTrimAnalyzer=true' '-p:EnableAotAnalyzer=true'
    '-p:NoWarn=CS1591' '-p:TrimmerSingleWarn=false'
    '-p:SuppressTrimAnalysisWarnings=false'
    "-p:ILLinkTreatWarningsAsErrors=$check" "-p:IlcTreatWarningsAsErrors=$check"
)
if [ "$runtime_async" = disabled ]; then common+=('-p:UseRuntimeAsync=false'); fi
printf 'provider=%s\nprobe=%s\nmode=%s\nframework=%s\nrid=%s\nruntime_async=%s\ncheck=%s\n' \
    "$provider" "$probe" "$mode" "$framework" "$rid" "$runtime_async" "$check" > "$evidence/inputs.txt"

phase=restore
restore_options=(--locked-mode)
if [ "$update_lock" = true ]; then restore_options=(--force-evaluate); fi
if [ "$update_lock" = false ]; then
    # Compiler and ILC warning output is not replayed by incremental no-op builds.
    # Every publication must produce its complete current diagnostic inventory.
    rm -rf -- "$output_root/build" || exit $?
fi
# No TargetFramework here: every committed lock contains both supported targets.
run_logged "$evidence/restore.log" dotnet restore "$project" "${restore_options[@]}" \
    --artifacts-path "$output_root/build" "${common[@]}" || exit $?

phase=evaluate
properties=TargetFramework,TargetFrameworks,RuntimeIdentifier,NativeAotMode,NativeAotFeatureProvider,PublishAot,JsonSerializerIsReflectionEnabledByDefault,EnableTrimAnalyzer,EnableAotAnalyzer,NoWarn,TrimmerSingleWarn,SuppressTrimAnalysisWarnings,ILLinkTreatWarningsAsErrors,IlcTreatWarningsAsErrors,NuGetLockFilePath,UseRuntimeAsync,Features,WeaverConfiguration,ArtifactsPath
evaluation=("${common[@]}" "-p:TargetFramework=$framework" '-p:UseArtifactsOutput=true' "-p:ArtifactsPath=$output_root/build")
run_logged "$evidence/probe-properties.log" dotnet msbuild "$project" -nologo "${evaluation[@]}" "-getProperty:$properties" || exit $?
representative="$repo_root/src/common/Elsa.Features/Elsa.Features.csproj"
if [ "$probe" != features ]; then representative="$repo_root/src/modules/Elsa.Workflows.Core/Elsa.Workflows.Core.csproj"; fi
run_logged "$evidence/library-properties.log" dotnet msbuild "$representative" -nologo "${evaluation[@]}" "-getProperty:$properties" || exit $?
if [ "$update_lock" = true ]; then
    phase=lock-updated
    printf '%s\n' 'Lock regenerated for both targets. Review and commit the lock before a normal run.'
    exit 0
fi

phase=publish
# A failed publish must never leave a previous executable available for this run.
rm -rf -- "$publish_dir" || exit $?
run_logged "$evidence/publish.log" dotnet publish "$project" --configuration Release \
    --framework "$framework" --no-restore --artifacts-path "$output_root/build" \
    --output "$publish_dir" "-bl:$evidence/publish.binlog" "${common[@]}"
status=$?
collect_diagnostics || exit $?
[ "$status" -eq 0 ] || exit "$status"

phase=execute
executable="$publish_dir/$app"
[ -x "$executable" ] || { printf '%s\n' "Published executable is missing: $executable" >&2; exit 66; }
run_logged "$evidence/execution.log" "$executable" --expect-mode "$mode"
status=$?
[ "$status" -eq 0 ] || exit "$status"
phase=verify
framework_version=${framework#net}
framework_major=${framework_version%%.*}
dynamic_code=True
if [ "$mode" = native ]; then dynamic_code=False; fi
# Exit zero alone cannot prove that the selected production scenario ran. Validate
# the ordered protocol, including identities and the final scope, independently.
awk -v header="probe=$probe provider=$provider build-mode=$mode expected-mode=$mode" \
    -v runtime="^framework=[.]NET $framework_major[.][^ ]+ rid=$rid dynamic-code=$dynamic_code reflection-json=False$" \
    -v started="scenario=$probe result=started" -v passed="scenario=$probe result=passed" \
    -v final="probe=$probe result=passed scope=$provider" '
    NF { last = $0 }
    $0 == header { if (state != 0) invalid = 1; state = 1; next }
    /^framework=/ { if (state != 1 || $0 !~ runtime) invalid = 1; state = 2; next }
    $0 == "scenario=harness-control result=passed" { if (state != 2) invalid = 1; state = 3; next }
    $0 == started { if (state != 3) invalid = 1; state = 4; next }
    $0 == passed { if (state != 4) invalid = 1; state = 5; next }
    $0 == final { if (state != 5) invalid = 1; state = 6; next }
    /^(probe=|scenario=)/ { invalid = 1 }
    END { exit !(state == 6 && !invalid && last == final) }
' "$evidence/execution.log" || {
    printf '%s\n' 'Missing, inconsistent, or incomplete probe result markers; inspect execution.log' >&2
    exit 65
}
if [ "$check" = true ] && [ -s "$evidence/il-diagnostics.log" ]; then
    phase=diagnostics
    printf '%s\n' 'Unresolved IL diagnostics remain; inspect il-diagnostics.log' >&2
    exit 65
fi
phase=completed
exit 0
