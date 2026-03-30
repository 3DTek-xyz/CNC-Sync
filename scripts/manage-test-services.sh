#!/bin/bash

set -euo pipefail

WORKSPACE_DIR="/Users/benharper/Coding/CBWSS-Sync"
DOCKER_COMPOSE_FILE="$WORKSPACE_DIR/docker-compose.test-services.yml"
DOCKER_CONTEXT_NAME="${DOCKER_CONTEXT_NAME:-remote-server}"

log() {
  echo "[cncsync-test-services] $1"
}

docker_remote() {
  docker --context "$DOCKER_CONTEXT_NAME" "$@"
}

docker_compose_remote() {
  docker --context "$DOCKER_CONTEXT_NAME" compose -f "$DOCKER_COMPOSE_FILE" "$@"
}

require_context() {
  docker context inspect "$DOCKER_CONTEXT_NAME" >/dev/null 2>&1 || {
    echo "Docker context '$DOCKER_CONTEXT_NAME' was not found."
    exit 1
  }
}

case "${1:-}" in
  start)
    require_context
    log "Starting remote SFTP test service on context '$DOCKER_CONTEXT_NAME'..."
    docker_compose_remote up -d sftp-test
    docker_compose_remote ps
    ;;
  stop)
    require_context
    log "Stopping remote SFTP test service on context '$DOCKER_CONTEXT_NAME'..."
    docker_compose_remote stop sftp-test
    ;;
  down)
    require_context
    log "Removing remote SFTP test service on context '$DOCKER_CONTEXT_NAME'..."
    docker_compose_remote down
    ;;
  status)
    require_context
    docker_compose_remote ps
    ;;
  logs)
    require_context
    docker_compose_remote logs --tail=200 sftp-test
    ;;
  *)
    echo "Usage: ./scripts/manage-test-services.sh {start|stop|down|status|logs}"
    exit 1
    ;;
esac
