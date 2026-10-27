#!/usr/bin/env bash
# Probe every new checkpoint of a run on the CPU while it trains: training/autoprobe.sh <run> [interval_s]
#   appends probe_ckpts.py lines (steeple 20, bank 10, R1, R2, top speed) to runs/<run>_probe.log; exits when the run
#   log ends with "exit N" and every checkpoint has been probed.
set -uo pipefail
export MSYS_NO_PATHCONV=1
RUN=$1; EVERY=${2:-60}
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; W="$(cygpath -w "$ROOT")"; D="$ROOT/training/runs"
LOG="$D/${RUN}_probe.log"; touch "$LOG"
while true; do
  done_run=$(grep -c "^exit " "$D/$RUN.log" 2>/dev/null)
  for s in $(ls "$D/$RUN/checkpoints" 2>/dev/null | grep -E '^[0-9]+$' | sort -n); do
    [ "$s" = "000000000000" ] && continue
    grep -q "^probed $s" "$LOG" && continue
    # orbax writes the step dir before it is complete: wait until it is a minute old
    age=$(( $(date +%s) - $(stat -c %Y "$D/$RUN/checkpoints/$s") )); [ $age -lt 60 ] && continue
    docker run --rm --init -e JAX_PLATFORMS=cpu -v "$W\training:/work" porace-trainer python -u probe_ckpts.py "runs/$RUN" $((10#$s)) 2>&1 \
      | grep -E "M  steeple|Traceback|Error" >> "$LOG"
    echo "probed $s" >> "$LOG"
  done
  [ "$done_run" != "0" ] && break
  sleep "$EVERY"
done
echo "autoprobe $RUN finished" >> "$LOG"
