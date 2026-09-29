#!/usr/bin/env bash
# Chaos test for the Docker Compose stack.
#
# With every sensor reporting every tick, take the API down, then SQL Server, mid-stream.
# Afterwards, every sensor's readings across the whole window must be gap-free: readings taken
# during the outages were buffered by the simulator (store-and-forward) and stored once the
# system recovered. A lost reading would show up as a gap roughly the length of an outage.
#
# Usage: scripts/chaos-test.sh        (OUTAGE_SECONDS=30 by default)
set -euo pipefail
cd "$(dirname "$0")/.."

API=http://localhost:5278
SA="${SA_PASSWORD:-DevOnly_Passw0rd!}"
OUTAGE="${OUTAGE_SECONDS:-30}"
INTERVAL=2
MAX_GAP_MS=$((INTERVAL * 3 * 1000)) # a missed timer tick is tolerated; an outage-length gap is not

log() { echo "[$(date -u +%H:%M:%S)] $*"; }
utc() { date -u +%Y-%m-%dT%H:%M:%S; }

sql() {
  MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA" -C -h -1 -W -d IoTDigitalTwin -Q "SET NOCOUNT ON; $1"
}

wait_healthy() {
  for _ in $(seq 1 90); do
    curl -fsS "$API/health" >/dev/null 2>&1 && return 0
    sleep 2
  done
  log "API did not become healthy"; return 1
}

restore_simulator() {
  log "Restoring normal simulator settings"
  env -u SIM_INTERVAL_SECONDS -u SIM_ANOMALY_PROBABILITY docker compose up -d simulator >/dev/null 2>&1 || true
}
trap restore_simulator EXIT

log "Starting the stack with every sensor reporting every ${INTERVAL}s (anomalies off)"
SIM_INTERVAL_SECONDS=$INTERVAL SIM_ANOMALY_PROBABILITY=0 docker compose up -d --build >/dev/null
wait_healthy
sleep 15 # let the recreated simulator reach steady state

START=$(utc)
log "Window starts at $START"

log "Outage 1: stopping the API for ${OUTAGE}s"
docker compose stop api >/dev/null
sleep "$OUTAGE"
docker compose start api >/dev/null
wait_healthy
log "API recovered"
sleep 10

log "Outage 2: stopping SQL Server for ${OUTAGE}s"
docker compose stop sqlserver >/dev/null
sleep "$OUTAGE"
docker compose start sqlserver >/dev/null
wait_healthy
log "Database recovered"

END=$(utc)
log "Window ends at $END; waiting for the buffered backlog to drain"

# Delivery is oldest-first, so once every sensor has a reading newer than END, everything
# taken inside the window has been delivered (or was lost).
for i in $(seq 1 90); do
  behind=$(sql "SELECT COUNT(*) FROM metadata.Sensors s WHERE s.IsActive = 1 AND NOT EXISTS (
                  SELECT 1 FROM telemetry.SensorReadings r WHERE r.SensorId = s.Id AND r.[Timestamp] > '$END');" | tr -d '[:space:]')
  depth=$(curl -fsS "$API/ingest/stats" | sed -E 's/.*"queueDepth":([0-9]+).*/\1/')
  [ "$behind" = "0" ] && [ "$depth" = "0" ] && break
  [ "$i" = "90" ] && { log "Backlog did not drain (sensors behind: $behind, queue depth: $depth)"; exit 1; }
  sleep 2
done
log "Backlog drained"

log "Checking every sensor for gaps in [$START, $END]"
report=$(sql "
  WITH w AS (
    SELECT SensorId,
           DATEDIFF_BIG(millisecond, LAG([Timestamp]) OVER (PARTITION BY SensorId ORDER BY [Timestamp]), [Timestamp]) AS GapMs
    FROM telemetry.SensorReadings
    WHERE [Timestamp] BETWEEN '$START' AND '$END')
  SELECT SensorId + ' readings=' + CAST(COUNT(*) AS varchar) + ' maxGapMs=' + CAST(ISNULL(MAX(GapMs), 0) AS varchar)
  FROM w GROUP BY SensorId ORDER BY SensorId;")
echo "$report"

sensors=$(echo "$report" | grep -c "readings=" || true)
worst=$(echo "$report" | sed -nE 's/.*maxGapMs=([0-9]+).*/\1/p' | sort -n | tail -1)
expected=$(sql "SELECT COUNT(*) FROM metadata.Sensors WHERE IsActive = 1;" | tr -d '[:space:]')
stats=$(curl -fsS "$API/ingest/stats")

log "Sensors reporting: $sensors/$expected, worst gap: ${worst}ms (limit ${MAX_GAP_MS}ms)"
log "Ingest stats: $stats"

if [ "$sensors" != "$expected" ] || [ "${worst:-999999}" -gt "$MAX_GAP_MS" ]; then
  log "FAIL: readings were lost during the outages"
  exit 1
fi
log "PASS: no readings lost across an API outage and a database outage"
