#!/usr/bin/env bash
# Full graduation check for one checkpoint, all on the CPU in parallel: steeple 300 seeds (gate300.sh) + R2 x30 +
# bank x30 + R1 x20. s6 @ 54.1M graduated the steeple gate (273/300) but R2 had drifted to 23/30, so a steeple pass
# alone is not enough to ship.
#   training/gateall.sh <run> <step>  ->  one summary line; GRADUATED only if the steeple gate graduates and every rung passes
set -uo pipefail
export MSYS_NO_PATHCONV=1
RUN=$1; s=$2; D="$(cd "$(dirname "$0")" && pwd)"; W="$(cygpath -w "$D")"; M=$(awk "BEGIN{printf \"%.1f\", $s/1e6}")
P="runs/$RUN/params_$(printf %012d $s).pkl"; OUT="$D/runs/${RUN}_${M}M"
"$D/gate300.sh" "$RUN" "$s" | tail -1 > "${OUT}_gate300.txt" &
until [ -f "$D/$P" ]; do sleep 5; done; sleep 5   # gate300.sh converts the orbax checkpoint first
T="docker run --rm --init -e JAX_PLATFORMS=cpu -v $W:/work porace-trainer python -u eval.py"
$T --rung r2 --seeds 30 --params "$P" 2>&1 | grep -E "^R2" > "${OUT}_r2.log" &
$T --rung bank --seeds 30 --params "$P" 2>&1 | grep -E "^BANK" > "${OUT}_bank.log" &
$T --rung r1 --seeds 20 --params "$P" 2>&1 | grep -E "^R1" > "${OUT}_r1.log" &
wait
g=$(cat "${OUT}_gate300.txt"); ok=1
echo "$g" | grep -q GRADUATED || ok=0
rungs=""
for r in r2 bank r1; do l=$(tail -1 "${OUT}_$r.log" 2>/dev/null); echo "$l" | grep -q PASS || ok=0; rungs="$rungs $(echo "$l" | grep -o -E '^[A-Z0-9 ]+: [0-9]+/[0-9]+')"; done
echo "$g |$rungs -> $([ $ok = 1 ] && echo ALL-PASS || echo NOT-SHIPPABLE)"
