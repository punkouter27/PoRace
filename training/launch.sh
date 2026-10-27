#!/usr/bin/env bash
# Background GPU training from Git Bash: training/launch.sh <name> <train.py args...>
#   writes training/runs/<name>.log (Warp solver warnings filtered), container name porace_<name>
set -uo pipefail
export MSYS_NO_PATHCONV=1
NAME=$1; shift
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; W="$(cygpath -w "$ROOT")"
docker run --rm --init --log-driver none --gpus all --name "porace_$NAME" \
  -v "$W\training:/work" -v "$W\docs:/docs" -v "$W\Assets:/assets" porace-trainer \
  python -u train.py --logdir "runs/$NAME" "$@" 2>&1 \
  | grep --line-buffered -v -E 'iterations limit reached|iterationtions|iteratioations|linesearch|To disable the print|warning: m.opt|^\s*$|NVIDIA|container image|nvidia.com|license|By pulling|convenience' \
  > "$ROOT/training/runs/$NAME.log"
echo "exit ${PIPESTATUS[0]}" >> "$ROOT/training/runs/$NAME.log"
