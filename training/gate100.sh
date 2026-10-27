#!/usr/bin/env bash
# 100-seed steeplechase graduation gate for checkpoints of a run (CPU): training/gate100.sh <run> <step> [<step>...]
#   converts the orbax checkpoint to params_<step>.pkl if needed; writes runs/<run>_gate_<M>M.log
set -uo pipefail
export MSYS_NO_PATHCONV=1
RUN=$1; shift; W="$(cygpath -w "$(cd "$(dirname "$0")" && pwd)")"
for s in "$@"; do
  M=$(awk "BEGIN{printf \"%.1f\", $s/1e6}")
  docker run --rm --init -e JAX_PLATFORMS=cpu -v "$W:/work" porace-trainer python -u -c "
from pathlib import Path; from brax.io import model; from brax.training.agents.ppo import checkpoint as ck
out = Path('runs/$RUN/params_%012d.pkl' % $s)
if not out.exists(): model.save_params(str(out), ck.load(str(Path('runs/$RUN/checkpoints/%012d' % $s).resolve())))
import eval as ev; ev.run_steeple(model.load_params(str(out)), 100)" 2>&1 | grep -E "^seed|STEEPLE|Error|Traceback" > "runs/${RUN}_gate_${M}M.log" &
done
wait
for s in "$@"; do M=$(awk "BEGIN{printf \"%.1f\", $s/1e6}"); echo "$RUN ${M}M: $(tail -1 runs/${RUN}_gate_${M}M.log)"; done
