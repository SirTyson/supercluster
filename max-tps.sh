#!/bin/bash

# MISSION="SimplePayment"
MISSION="MaxTPSClassic"


# Baseline image (BL unordered_set branch)
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.1.1-2322.11edf7f45.focal-do-not-use-in-prd-perftests"
# Image with tx set compression
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.1.1-2351.0a8c01c41.focal-do-not-use-in-prd-perftests"
# Marta image
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.1.1-2339.df00dd702.focal-txBatching-perftests"
# Compress + unordered_set + txBatching
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.1.1-2356.5cc706c08.focal-do-not-use-in-prd-perftests"
# No medida
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.1.1-2358.fb1457d2d.focal-do-not-use-in-prd-perftests"
# Instrumented medida
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2362.e69924f37.focal-do-not-use-in-prd-perftests"
# unordered_set + txbatching, expensive metrics removed
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2363.86e1e0aee.focal-do-not-use-in-prd-perftests"
# unordered_set + txbatching, expensive metrics replaced with counters
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2364.4cb229071.focal-do-not-use-in-prd-perftests"
# unordered_set + txbatching, expensive metrics replaced with counters, trigger next ledger on accept
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2372.2dbb3b539.focal-do-not-use-in-prd-perftests"
# unordered_set + txbatching, expensive metrics replaced with counters, trigger next ledger on accept, no timer
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2375.bac0238a4.focal-do-not-use-in-prd-perftests"

# Master with metric fixes + unoredered_set
#IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2386.aee8b2f3f.focal-do-not-use-in-prd-perftests"

IMAGE="docker-registry.services.stellar-ops.com/dev/stellar-core:22.2.1-2392.32d61f514.focal~do~not~use~in~prd~perftests"
STARTING_RATE=1945
MAX_RATE=1955
NUM_RUNS_TO_AVERAGE=1


PROJECT="/home/user/src/supercluster/src/App/App.fsproj"

dotnet run --project $PROJECT clean && dotnet run --project $PROJECT --configuration Release \
  -- mission $MISSION \
  --image=$IMAGE \
  --netdelay-image=docker-registry.services.stellar-ops.com/dev/sdf-netdelay:latest \
  --postgres-image=docker-registry.services.stellar-ops.com/dev/postgres:9.5.22 \
  --nginx-image=docker-registry.services.stellar-ops.com/dev/nginx:latest \
  --prometheus-exporter-image=docker-registry.services.stellar-ops.com/dev/stellar-core-prometheus-exporter:latest \
  --ingress-internal-domain=stellar-supercluster.kube001-ssc.services.stellar-ops.com \
  --avoid-node-labels=purpose:ssc \
  --namespace=garand \
  --export-to-prometheus \
  --trace="ledger" \
  --pubnet-data=/home/user/src/supercluster/topologies/theoretical-max-tps.json \
  --tx-rate=$STARTING_RATE --max-tx-rate=$MAX_RATE  --num-runs=$NUM_RUNS_TO_AVERAGE

  # --pubnet-data=/home/user/src/stellar-supercluster/data/public-network-data-2024-08-01.json \
  # --tier1-keys=/home/user/src/stellar-supercluster/data/tier1keys.json \
