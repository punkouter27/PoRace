#!/usr/bin/env bash
# F.9 faster-dog check for one checkpoint (CPU, parallel): speed x10 + R1 x20 + R2 x30 + bank x30.
#   training/gatespeed.sh <run> <step>  ->  one summary line; ALL-PASS only if every rung passes
set -uo pipefail
export MSYS_NO_PATHCONV=1
RUN=$1; s=$2; D="$(cd "$(dirname "$0")" && pwd)"; W="$(cygpath -w "$D")"; M=$(awk "BEGIN{printf \"%.1f\", $s/1e6}")
P="runs/$RUN/params_$(printf %012d $s).pkl"; OUT="$D/runs/${RUN}_${M}M"
[ -f "$D/$P" ] || docker run --rm --init -e JAX_PLATFORMS=cpu -v "$W:/work" porace-trainer python -c "
from pathlib import Path; from brax.io import model; from brax.training.agents.ppo import checkpoint as ck
model.save_params('$P', ck.load(str(Path('runs/$RUN/checkpoints/%012d' % $s).resolve())))" 2>/dev/null
T="docker run --rm --init -e JAX_PLATFORMS=cpu -v $W:/work porace-trainer python -u eval.py"
for r in "speed 10" "r1 20" "r2 30" "bank 30"; do set -- $r
  ( for try in 1 2 3; do $T --rung $1 --seeds $2 --params "$P" 2>&1 | grep -E "^seed|^[A-Z0-9 ]+: [0-9]+/" > "${OUT}_$1.log"; grep -q -E "^[A-Z0-9 ]+: [0-9]+/" "${OUT}_$1.log" && break; sleep 5; done ) &
done
wait; ok=1; parts=""
for r in speed r1 r2 bank; do l=$(tail -1 "${OUT}_$r.log"); echo "$l" | grep -q PASS || ok=0; parts="$parts | $(echo "$l" | grep -o -E '^[A-Z0-9 ]+: [0-9]+/[0-9]+')"; done
sp=$(grep "^seed" "${OUT}_speed.log" | awk '{a+=$5; b+=$8} END {if (NR) printf "mean 2.0->%.2f 2.5->%.2f", a/NR, b/NR}')
echo "$RUN ${M}M: ${parts# | } ($sp) -> $([ $ok = 1 ] && echo ALL-PASS || echo FAIL)"
