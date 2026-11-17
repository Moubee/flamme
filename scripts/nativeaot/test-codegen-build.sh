#!/usr/bin/env bash
# Validation lives in Python so subprocess argument lists preserve arbitrary paths.
set -uo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd) || exit 1
exec python3 "$script_dir/codegen-build/validate.py" "$@"
