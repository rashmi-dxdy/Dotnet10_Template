#!/usr/bin/env bash
#
# Entrypoint of the development container (docker/Dockerfile.dev): runs `dotnet watch` and gives the app a
# graceful shutdown when the container stops.
#
# Why the wrapper: `dotnet watch` does not react to the SIGTERM (or SIGINT) that `docker compose stop` / `down`
# sends when it has no interactive console. Docker would then SIGKILL the app after stop_grace_period, without
# HostOptions:ShutdownTimeout draining in-flight requests and consumers. On a stop signal this script sends
# SIGTERM to the `dotnet run` process that dotnet watch started (it forwards it to Host.Api, which shuts down
# gracefully), waits for it, then ends dotnet watch.
set -uo pipefail

# The app process dotnet watch launches: `dotnet run --no-build ...`.
readonly APP_PATTERN='dotnet run --no-build'
# A little under the compose stop_grace_period (40 s) and above HostOptions__ShutdownTimeout (30 s).
readonly SHUTDOWN_WAIT_SECONDS=35

dotnet watch --project src/Host/Host.Api/Host.Api.csproj run --no-launch-profile &
watch_pid=$!

shutdown() {
    trap '' TERM INT
    echo "watch.sh: stop signal received, shutting the app down gracefully"
    pkill -TERM -f "$APP_PATTERN"
    for ((i = 0; i < SHUTDOWN_WAIT_SECONDS; i++)); do
        pkill -0 -f "$APP_PATTERN" || break
        sleep 1
    done
    kill -TERM "$watch_pid" 2>/dev/null
    sleep 1
    kill -KILL "$watch_pid" 2>/dev/null
    exit 0
}
trap shutdown TERM INT

# `wait` returns early when a trapped signal arrives; loop until dotnet watch itself exits.
while kill -0 "$watch_pid" 2>/dev/null; do
    wait "$watch_pid"
done
wait "$watch_pid"
