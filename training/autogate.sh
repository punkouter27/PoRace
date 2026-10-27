#!/usr/bin/env bash
# Gate every checkpoint of a run as it lands (GATE=gate300.sh by default, or gateall.sh): training/autogate.sh <run> [interval_s]
#   one runs/<run>_gate_<M>M.log per checkpoint (gate100.sh); summary lines appended to runs/<run>_gates.log
set -uo pipefail
RUN=$1; EVERY=${2:-60}; D="$(cd "$(dirname "$0")" && pwd)/runs"; SUM="$D/${RUN}_gates.log"; touch "$SUM"
while true; do
  fin=$(grep -c "^exit " "$D/$RUN.log" 2>/dev/null)
  for s in $(ls "$D/$RUN/checkpoints" 2>/dev/null | grep -E '^[0-9]+$' | sort -n); do
    n=$((10#$s)); [ $n -eq 0 ] && continue; grep -q "^$n " "$SUM" && continue
    age=$(( $(date +%s) - $(stat -c %Y "$D/$RUN/checkpoints/$s") )); [ $age -lt 60 ] && continue
    r=$("$(dirname "$0")/${GATE:-gate300.sh}" "$RUN" $n | tail -1); echo "$n $r" >> "$SUM"
  done
  [ "$fin" != "0" ] && break; sleep "$EVERY"
done
echo "autogate $RUN finished" >> "$SUM"
