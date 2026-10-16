#!/usr/bin/env bash
# Builds and runs what docker-dotnet, cronjob-patterns and k6-load-testing show: the service image, the job
# image, the compose file and the load-test script, the way a developer would. Needs Docker and k6.
# Run by .github/workflows/infra-samples.yml.
set -euo pipefail
cd "$(dirname "$0")"
K6=${K6:-k6}
pass() { echo "ok: $1"; }
fail() { echo "::error::$1"; exit 1; }
cleanup() { docker rm -f sample-api sample-api-hc bad-kafka >/dev/null 2>&1 || true; docker compose down -v >/dev/null 2>&1 || true; }
trap cleanup EXIT

wait_for() {   # url
  for _ in $(seq 1 60); do curl -sf "$1" >/dev/null && return 0; sleep 1; done
  return 1
}

# --- The service image (docker-dotnet) ---
docker build -q -t sample-api -f src/OrderService.Api/Dockerfile --target final . >/dev/null
user=$(docker inspect -f '{{.Config.User}}' sample-api)
{ [ -n "$user" ] && [ "$user" != "0" ] && [ "$user" != "root" ]; } || fail "the image runs as root ('$user')"
pass "the service image runs as a non-root user ($user)"

docker run -d --name sample-api -p 5001:8080 sample-api >/dev/null
wait_for http://localhost:5001/health/live || fail "the container didn't answer /health/live"
pass "the container answers /health/live from outside"

if docker exec sample-api sh -c true >/dev/null 2>&1; then fail "the chiseled image has a shell"; fi
docker run -d --name sample-api-hc --health-cmd "curl -f http://localhost:8080/health/live" \
  --health-interval 2s --health-retries 2 sample-api >/dev/null
status=starting
for _ in $(seq 1 30); do
  status=$(docker inspect -f '{{.State.Health.Status}}' sample-api-hc)
  [ "$status" = unhealthy ] && break
  sleep 1
done
[ "$status" = unhealthy ] || fail "a curl health check on the chiseled image ended '$status'"
pass "story [docker-dotnet]: a curl HEALTHCHECK on a chiseled image is unhealthy for good, though the app answers"

env=$(docker inspect -f '{{json .Config.Env}}' sample-api)
echo "$env" | grep -q 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true' || fail "expected invariant mode in the chiseled image: $env"
docker pull -q mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra >/dev/null
extra=$(docker inspect -f '{{json .Config.Env}}' mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra)
if echo "$extra" | grep -q 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true'; then fail "chiseled-extra is invariant too: $extra"; fi
pass "production [docker-dotnet]: chiseled runs without ICU (invariant mode), chiseled-extra with it"

# --- The load test (k6-load-testing) ---
"$K6" run -q --stage 3s:5 --stage 5s:5 --stage 2s:0 -e BASE_URL=http://localhost:5001 ../k6/patients.js \
  || fail "k6 failed against the healthy endpoint"
pass "production [k6-load-testing]: the thresholds pass against the service in its chiseled image (no ICU, no tzdata)"
set +e
"$K6" run -q --stage 3s:5 --stage 5s:5 --stage 2s:0 -e BASE_URL=http://localhost:5001/slow ../k6/patients.js >/dev/null 2>&1
code=$?
set -e
[ "$code" -eq 99 ] || fail "k6 should exit 99 when a threshold is crossed, got $code"
pass "story [k6-load-testing]: an endpoint over p(95) < 500 ms fails the run (exit 99), so the pipeline stops"
docker rm -f sample-api >/dev/null

# --- The job image (cronjob-patterns) ---
docker build -q -t sample-job -f src/DailyReport.Job/Dockerfile . >/dev/null
docker run --rm sample-job || fail "a successful run exited non-zero"
set +e
docker run --rm -e JOB_FAIL=1 sample-job
code=$?
set -e
[ "$code" -eq 1 ] || fail "a failed run should exit 1, got $code"
pass "story [cronjob-patterns]: a failed run exits 1 from the container, so the CronJob's backoffLimit retries it"

# --- Compose (docker-dotnet) ---
docker compose up -d --build --wait --wait-timeout 300 || { docker compose logs --tail 50; fail "compose didn't come up"; }
wait_for http://localhost:5001/health/live || fail "the service in compose didn't answer"
pass "compose: postgres, redis and kafka healthy, the service up"
docker compose down -v >/dev/null

# The same broker with a name as its cluster id, as the skill had it: KRaft wants a base64 UUID.
docker run -d --name bad-kafka \
  -e KAFKA_NODE_ID=1 -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 -e CLUSTER_ID=order-service-local \
  confluentinc/cp-kafka:7.6.0 >/dev/null
running=true
for _ in $(seq 1 60); do
  running=$(docker inspect -f '{{.State.Running}}' bad-kafka)
  [ "$running" = false ] && break
  sleep 1
done
[ "$running" = false ] || fail "a broker with CLUSTER_ID=order-service-local kept running"
pass "story [docker-dotnet]: CLUSTER_ID=order-service-local stops the broker at start ($(docker logs bad-kafka 2>&1 | grep -io 'uuid[^\"]*' | head -1))"
