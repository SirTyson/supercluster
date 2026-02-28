#!/bin/bash

IMAGE=docker-registry.services.stellar-ops.com/dev/stellar-core:25.1.2-3048.589044435.noble-perftests

PROJECT="/home/user/src/supercluster/src/App/App.fsproj"

# -- Drift distribution (uncomment one) --
# No drift:
#DRIFT_ARGS=""
# Uniform drift in [-2000, +2000]ms:
#DRIFT_ARGS="--uniform-drift=-2000,+2000 --drift-pct 70"
# Bimodal drift: first half [-5000,-2000]ms, second half [+2000,+5000]ms:
DRIFT_ARGS="--bimodal-drift=-5000,-2000,+2000,+5000 --drift-pct 70"

dotnet run --project $PROJECT clean --namespace=garand  && dotnet run --project $PROJECT --configuration Release \
  -- mission TriggerTimerMixConsensus \
  --image=$IMAGE \
  --netdelay-image=docker-registry.services.stellar-ops.com/dev/sdf-netdelay:latest \
  --postgres-image=docker-registry.services.stellar-ops.com/dev/postgres:9.5.22 \
  --nginx-image=docker-registry.services.stellar-ops.com/dev/nginx:latest \
  --prometheus-exporter-image=docker-registry.services.stellar-ops.com/dev/stellar-core-prometheus-exporter:latest \
  --ingress-internal-domain=stellar-supercluster.kube001-ssc-eks.services.stellar-ops.com \
  --avoid-node-labels=purpose:ssc \
  --namespace=garand \
  --export-to-prometheus \
  --pubnet-data=/home/user/src/stellar-supercluster/data/topologies/generated-overlay-topology-0.json \
  --trigger-timer-flag-pct 100 \
  $DRIFT_ARGS
