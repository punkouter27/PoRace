#!/usr/bin/env bash
# Steeplechase graduation gate on 300 seeds (three sets of 100, run in parallel on the CPU): training/gate300.sh <run> <step>
#   seeds 0-99 alone are used to pick checkpoints, so they overrate the pick (s5 @ 80.3M: 94 on 0-99, 84 and 87 held
#   out). Graduated = >= 90 % on every set, zero NaN. Logs: runs/<run>_gate_<M>M_s{0,100,200}.log
set -uo pipefail
export MSYS_NO_PATHCONV=1
RUN=$1; s=$2; W="$(cygpath -w "$(cd "$(dirname "$0")" && pwd)")"; M=$(awk "BEGIN{printf \"%.1f\", $s/1e6}")
P="runs/$RUN/params_$(printf %012d $s).pkl"
docker run --rm --init -e JAX_PLATFORMS=cpu -v "$W:/work" porace-trainer python -c "
from pathlib import Path; from brax.io import model; from brax.training.agents.ppo import checkpoint as ck
out = Path('$P')
if not out.exists(): model.save_params(str(out), ck.load(str(Path('runs/$RUN/checkpoints/%012d' % $s).resolve())))" 2>/dev/null
for f in 0 100 200; do
  ( for try in 1 2 3; do   # a container occasionally dies loading the model ("at least 4 vertices required"): retry
      docker run --rm --init -e JAX_PLATFORMS=cpu -v "$W:/work" porace-trainer python -u eval.py --rung steeple --seeds 100 --first_seed $f --params "$P" 2>&1 \
        | grep -E "^seed|STEEPLE|Error|Traceback" > "runs/${RUN}_gate_${M}M_s$f.log"
      grep -q STEEPLE "runs/${RUN}_gate_${M}M_s$f.log" && break; sleep $((5 * try)); done ) &
done
wait
tot=0; nan=0; all=1; parts=""
for f in 0 100 200; do
  l=$(tail -1 "runs/${RUN}_gate_${M}M_s$f.log"); p=$(echo "$l" | grep -o -E "[0-9]+/100" | cut -d/ -f1); n=$(echo "$l" | grep -o -E "NaN episodes [0-9]+" | grep -o -E "[0-9]+$")
  tot=$((tot + ${p:-0})); nan=$((nan + ${n:-1})); [ "${p:-0}" -lt 90 ] && all=0; parts="$parts ${p:-?}"
done
[ $nan -gt 0 ] && all=0
echo "$RUN ${M}M: GATE300 $tot/300 (sets:$parts) NaN $nan -> $([ $all = 1 ] && echo GRADUATED || echo FAIL)"
