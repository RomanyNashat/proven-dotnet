#!/usr/bin/env bash
# The services the skill samples run against, one at a time, so each has its own step in CI.
#   bash tools/ci/service.sh start <name>   download and start it in the background, return at once
#   bash tools/ci/service.sh wait <name>    block until it answers; fail with its log if it can't
# Names: mongo redis kafka pg mssql nginx.
#
# The downloads run while the samples build; each service's own "wait" step then shows how long that
# service took, and a notice on the run says it ("SQL Server 2022: ready 74s after start"). A download or
# start that fails is an error annotation naming the service, with the last lines of its log.
set -uo pipefail
action=$1
name=$2
state=/tmp/ci-services
mkdir -p "$state"

label() {
  case $name in
    mongo) echo "MongoDB 7" ;; redis) echo "Redis 7" ;; kafka) echo "Kafka 3.9" ;;
    pg) echo "PostgreSQL 17" ;; mssql) echo "SQL Server 2022" ;; nginx) echo "nginx 1.27" ;;
    *) echo "$name" ;;
  esac
}

run_container() {
  case $name in
    mongo)
      # enableTestCommands: the transaction test uses a fail point. /scripts: the mongosh scripts the skill shows.
      docker run -d --name mongo -p 27017:27017 -v "$PWD/tests/SkillSamples.Tests/Mongo/scripts:/scripts:ro" \
        mongo:7 --replSet rs0 --bind_ip_all --setParameter enableTestCommands=1 ;;
    redis)
      docker run -d --name redis -p 6379:6379 redis:7 ;;
    kafka)
      docker run -d --name kafka -p 9092:9092 \
        -e KAFKA_NODE_ID=1 -e KAFKA_PROCESS_ROLES=broker,controller \
        -e KAFKA_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093 \
        -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
        -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
        -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
        -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 \
        -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
        -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 \
        -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 -e KAFKA_NUM_PARTITIONS=1 \
        apache/kafka:3.9.0 ;;
    pg)
      docker run -d --name pg -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:17 ;;
    mssql)
      # Developer edition: free for development and testing.
      docker run -d --name mssql -p 1433:1433 -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
        -e "MSSQL_SA_PASSWORD=Samples-2026!" mcr.microsoft.com/mssql/server:2022-latest ;;
    nginx)
      # Check the skill's configs first; host network, so nginx can reach the test app on :5099.
      docker run --rm -v "$PWD/tests/SkillSamples.Tests/Nginx/conf:/etc/nginx/conf.d:ro" nginx:1.27-alpine nginx -t &&
      docker run -d --name nginx --network host \
        -v "$PWD/tests/SkillSamples.Tests/Nginx/conf:/etc/nginx/conf.d:ro" \
        -v "$PWD/tests/SkillSamples.Tests/Nginx/site:/srv/repo:ro" nginx:1.27-alpine ;;
    *)
      echo "unknown service: $name"; return 2 ;;
  esac
}

answers() {
  case $name in
    mongo) [ "$(docker exec mongo mongosh --quiet --eval 'db.hello().isWritablePrimary' 2>/dev/null)" = "true" ] ;;
    redis) [ "$(docker exec redis redis-cli ping 2>/dev/null)" = "PONG" ] ;;
    kafka) docker exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list >/dev/null 2>&1 ;;
    pg) docker exec pg pg_isready -U postgres >/dev/null 2>&1 ;;
    mssql) docker exec mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Samples-2026!' -Q "SELECT 1" >/dev/null 2>&1 ;;
    nginx) curl -fs http://localhost:8088/home.json >/dev/null 2>&1 ;;
  esac
}

fail() {
  local detail
  detail=$( { tail -n 15 "$state/$name.log" 2>/dev/null; docker logs --tail 15 "$name" 2>&1; } | tr '\n' ' ' | cut -c1-1500)
  echo "::error title=$(label)::$1 ${detail}"
  exit 1
}

case $action in
  start)
    date +%s > "$state/$name.started"
    nohup bash "$0" run "$name" > "$state/$name.log" 2>&1 &
    echo "$(label): downloading and starting in the background"
    ;;
  run)
    run_container; echo $? > "$state/$name.exit"; date +%s > "$state/$name.done"
    ;;
  wait)
    started=$(cat "$state/$name.started")
    until [ -f "$state/$name.exit" ]; do sleep 1; done   # the step's timeout-minutes bounds this
    [ "$(cat "$state/$name.exit")" = 0 ] || fail "download or start failed."
    pulled=$(( $(cat "$state/$name.done") - started ))
    if [ "$name" = mongo ]; then
      # A single-node replica set: transactions and change streams need one.
      for _ in $(seq 1 60); do docker exec mongo mongosh --quiet --eval 'db.runCommand({ping:1}).ok' >/dev/null 2>&1 && break; sleep 1; done
      docker exec mongo mongosh --quiet --eval 'rs.initiate({_id:"rs0",members:[{_id:0,host:"localhost:27017"}]})' >/dev/null
    fi
    for _ in $(seq 1 120); do
      if answers; then
        echo "::notice title=$(label)::ready $(( $(date +%s) - started ))s after start (download and start ${pulled}s)"
        exit 0
      fi
      sleep 1
    done
    fail "started, but didn't answer within 120 s."
    ;;
  *)
    echo "usage: service.sh start|wait <name>"; exit 2 ;;
esac
