#!/usr/bin/env bash
# Exercise orchestration failures without depending on a native compiler or feeds.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
wrapper="$script_dir/run.sh"
fixture=$(mktemp -d "${TMPDIR:-/tmp}/nativeaot-wrapper.XXXXXX")
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/bin"
cat > "$fixture/bin/dotnet" <<'FAKE'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >> "$FAKE_TRACE"
phase=$1
artifacts_path= framework=net11.0 previous=
for argument in "$@"; do
    if [ "$previous" = --artifacts-path ]; then artifacts_path=$argument; fi
    if [ "$previous" = --framework ]; then framework=$argument; fi
    previous=$argument
done
if [ "${FAKE_FAIL_PHASE:-}" = "$phase" ]; then
    case "$phase" in restore) exit 37 ;; publish) exit 38 ;; msbuild) exit 39 ;; esac
fi
case "$phase" in
    --info) printf '%s\n' 'Fake .NET SDK 11.0.100' ;;
    restore)
        if [ "${FAKE_REQUIRE_FRESH_BUILD:-false}" = true ] && [ -f "$artifacts_path/compile-cache" ]; then exit 92; fi
        ;;
    msbuild) printf '%s\n' '{"Properties":{"TargetFramework":"net11.0"}}' ;;
    publish)
        project=$2
        output=
        while [ "$#" -gt 0 ]; do
            if [ "$1" = --output ]; then output=$2; shift; fi
            shift
        done
        if [ "${FAKE_IL_WARNING:-false}" = true ] && [ ! -f "$artifacts_path/compile-cache" ]; then
            printf '%s\n' 'Fixture.cs(1,1): warning IL2026: fixture source diagnostic'
        fi
        mkdir -p "$artifacts_path"
        touch "$artifacts_path/compile-cache"
        if [ "${FAKE_MISSING_BINARY:-false}" = true ]; then exit 0; fi
        mkdir -p "$output"
        app=$(basename "$project" .csproj)
        printf '#!/usr/bin/env bash\nfixture_framework=%q\n' "$framework" > "$output/$app"
        cat >> "$output/$app" <<'APP'
printf 'execute %s\n' "$*" >> "$FAKE_TRACE"
[ "$1" = --expect-mode ] || exit 90
if [ "${FAKE_RUNTIME_STATUS:-0}" -ne 0 ]; then exit "$FAKE_RUNTIME_STATUS"; fi
case "${0##*/}" in
    Elsa.NativeAot.Features.Smoke) probe=features ;;
    Elsa.NativeAot.Execution.Smoke) probe=execution ;;
    Elsa.NativeAot.Payloads.Smoke) probe=payloads ;;
    Elsa.NativeAot.Smoke) probe=integrated ;;
esac
mode=$2 provider=legacy-baseline scope=legacy-baseline
rid=$FAKE_EXPECTED_RID version=${fixture_framework#net} dynamic_code=True reflection_json=False
if [ "$mode" = native ]; then dynamic_code=False; fi
fault=${FAKE_PROTOCOL_FAULT:-}
case "$fault" in
    empty) exit 0 ;;
    wrong-probe) probe=wrong ;;
    wrong-mode) mode=wrong ;;
    wrong-provider) provider=wrong ;;
    wrong-scope) scope=harness-control ;;
    wrong-framework) version=8.0 ;;
    wrong-rid) rid=wrong ;;
    dynamic-code) dynamic_code=wrong ;;
    reflection-json) reflection_json=True ;;
esac
if [ "$fault" != missing-header ]; then
    printf 'probe=%s provider=%s build-mode=%s expected-mode=%s\n' "$probe" "$provider" "$mode" "$mode"
fi
if [ "$fault" != missing-runtime ]; then
    printf 'framework=.NET %s.0 rid=%s dynamic-code=%s reflection-json=%s\n' "$version" "$rid" "$dynamic_code" "$reflection_json"
fi
if [ "$fault" != missing-control ]; then printf '%s\n' 'scenario=harness-control result=passed'; fi
if [ "$fault" = out-of-order ]; then printf 'scenario=%s result=passed\n' "$probe"; fi
printf 'scenario=%s result=started\n' "$probe"
if [ "$fault" != missing-production ]; then printf 'scenario=%s result=passed\n' "$probe"; fi
if [ "$fault" != truncated-final ]; then printf 'probe=%s result=passed scope=%s\n' "$probe" "$scope"; fi
if [ "$fault" = duplicate-final ]; then printf 'probe=%s result=passed scope=%s\n' "$probe" "$scope"; fi
if [ "$fault" = failure-marker ]; then printf 'probe=%s result=failed\n' "$probe"; fi
if [ "$fault" = trailing-output ]; then printf '%s\n' 'output after final marker'; fi
exit 0
APP
        chmod +x "$output/$app"
        ;;
    *) exit 91 ;;
esac
FAKE
cat > "$fixture/bin/clang" <<'FAKE'
#!/usr/bin/env bash
exit "${FAKE_CLANG_STATUS:-0}"
FAKE
chmod +x "$fixture/bin/dotnet" "$fixture/bin/clang"
export PATH="$fixture/bin:$PATH" FAKE_TRACE="$fixture/trace"
artifacts="$fixture/artifacts with spaces"
case "$(uname -s)" in Darwin) host_os=osx; other_rid=linux-x64 ;; *) host_os=linux; other_rid=osx-arm64 ;; esac
case "$(uname -m)" in arm64|aarch64) host_arch=arm64; other_arch=x64 ;; *) host_arch=x64; other_arch=arm64 ;; esac
host_rid=$host_os-$host_arch
export FAKE_EXPECTED_RID=$host_rid
passed=0
expect_status() {
    local expected=$1 title=$2 actual
    shift 2
    : > "$FAKE_TRACE"
    set +e
    /bin/bash "$wrapper" "$@" > "$fixture/last-output.log" 2>&1
    actual=$?
    set -e
    if [ "$actual" -ne "$expected" ]; then
        printf 'FAIL: %s: expected %s, got %s\n' "$title" "$expected" "$actual" >&2
        cat "$fixture/last-output.log" >&2
        exit 1
    fi
    passed=$((passed + 1))
}
assert_trace() { rg -q -- "$1" "$FAKE_TRACE" || { cat "$FAKE_TRACE" >&2; exit 1; }; }
assert_no_trace() { if rg -q -- "$1" "$FAKE_TRACE"; then cat "$FAKE_TRACE" >&2; exit 1; fi; }
args=(--probe features --rid "$host_rid" --artifacts-root "$artifacts")

/bin/bash -n "$wrapper"
expect_status 0 help --help
expect_status 64 'missing option value' --probe
expect_status 64 'invalid probe' --probe invalid
expect_status 64 'invalid mode' "${args[@]}" --mode invalid
expect_status 64 'invalid framework' "${args[@]}" --framework net8.0
expect_status 64 'invalid RID cannot become a path' "${args[@]}" --rid ../bad
expect_status 64 'invalid async option' "${args[@]}" --runtime-async invalid
expect_status 64 'unknown option' "${args[@]}" --bogus
expect_status 64 'cross-OS native rejection' "${args[@]}" --mode native --rid "$other_rid"
expect_status 64 'cross-architecture execution rejection' "${args[@]}" --mode native --rid "$host_os-$other_arch"
expect_status 0 'managed publication and execution' "${args[@]}"
assert_trace '^restore .*--locked-mode.*-p:NativeAotMode=managed'
assert_trace '^publish .*--no-restore.*-p:NativeAotMode=managed'
assert_trace '^execute --expect-mode managed$'
assert_no_trace '^restore .*TargetFramework='
result="$artifacts/legacy-baseline/features/managed/$host_rid/default/evidence/net11.0/result.json"
rg -q '"phase":"completed","exitCode":0' "$result"

export FAKE_REQUIRE_FRESH_BUILD=true FAKE_IL_WARNING=true
expect_status 0 'ordinary publication invalidates prior build caches' "${args[@]}"
rg -q IL2026 "$(dirname "$result")/il-diagnostics.log"
expect_status 0 'repeated ordinary publication retains current source diagnostics' "${args[@]}"
rg -q IL2026 "$(dirname "$result")/il-diagnostics.log"
unset FAKE_REQUIRE_FRESH_BUILD FAKE_IL_WARNING

for fault in empty missing-header missing-runtime missing-control missing-production truncated-final \
    wrong-probe wrong-mode wrong-provider wrong-scope wrong-framework wrong-rid dynamic-code \
    reflection-json duplicate-final failure-marker trailing-output out-of-order; do
    export FAKE_PROTOCOL_FAULT=$fault
    expect_status 65 "invalid result protocol: $fault" "${args[@]}"
    rg -q '"phase":"verify","exitCode":65' "$result"
done
unset FAKE_PROTOCOL_FAULT
expect_status 0 'valid net10 result markers' "${args[@]}" --framework net10.0
expect_status 0 'valid native result markers' "${args[@]}" --mode native

export FAKE_FAIL_PHASE=restore
expect_status 37 'restore failure preserved' "${args[@]}"
assert_no_trace '^publish '
[ ! -f "$(dirname "$result")/execution.log" ]
[ ! -s "$(dirname "$result")/il-diagnostics.log" ]
export FAKE_FAIL_PHASE=msbuild
expect_status 39 'evaluation failure preserved' "${args[@]}"
assert_no_trace '^publish '
export FAKE_FAIL_PHASE=publish
expect_status 38 'publish failure preserved' "${args[@]}"
assert_no_trace '^execute '
unset FAKE_FAIL_PHASE

export FAKE_RUNTIME_STATUS=42
expect_status 42 'probe failure preserved' "${args[@]}"
rg -q '"phase":"execute","exitCode":42' "$result"
unset FAKE_RUNTIME_STATUS
export FAKE_MISSING_BINARY=true
expect_status 66 'stale executable cannot mask missing output' "${args[@]}"
unset FAKE_MISSING_BINARY

export FAKE_IL_WARNING=true
expect_status 0 'diagnostic baseline retains warnings' "${args[@]}"
rg -q IL2026 "$(dirname "$result")/il-diagnostics.log"
expect_status 65 'strict source diagnostic gate' "${args[@]}" --check
assert_trace '^restore .*ILLinkTreatWarningsAsErrors=true.*IlcTreatWarningsAsErrors=true'
assert_trace '^publish .*ILLinkTreatWarningsAsErrors=true.*IlcTreatWarningsAsErrors=true'
unset FAKE_IL_WARNING

expect_status 0 'explicit complete lock maintenance' "${args[@]}" --update-lock
assert_trace '^restore .*--force-evaluate'
assert_no_trace '^restore .*--locked-mode'
assert_no_trace '^restore .*TargetFramework='
assert_no_trace '^publish '
export FAKE_CLANG_STATUS=43
expect_status 0 'cross-OS native lock maintenance needs no compiler' "${args[@]}" --mode native --rid "$other_rid" --update-lock
unset FAKE_CLANG_STATUS
assert_no_trace '^publish '
expect_status 0 'runtime async opt-out propagated' "${args[@]}" --runtime-async disabled
assert_trace '^restore .*UseRuntimeAsync=false'
assert_trace '^publish .*UseRuntimeAsync=false'

export FAKE_CLANG_STATUS=43
expect_status 43 'native toolchain error preserved' "${args[@]}" --mode native
assert_no_trace '^restore '
unset FAKE_CLANG_STATUS

# A PATH with only the utilities needed before the compiler check proves a
# missing compiler is distinguished from a restore or publication failure.
mkdir -p "$fixture/no-compiler"
for utility in uname dirname mkdir rm tee awk sort; do
    ln -s "$(command -v "$utility")" "$fixture/no-compiler/$utility"
done
ln -s /bin/bash "$fixture/no-compiler/bash"
ln -s "$fixture/bin/dotnet" "$fixture/no-compiler/dotnet"
original_path=$PATH
export PATH="$fixture/no-compiler"
expect_status 69 'missing native compiler' "${args[@]}" --mode native
export PATH=$original_path
assert_no_trace '^restore '
printf 'Passed %s wrapper checks.\n' "$passed"
