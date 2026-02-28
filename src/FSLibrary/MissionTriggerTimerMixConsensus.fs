// Copyright 2026 Stellar Development Foundation and contributors. Licensed
// under the Apache License, Version 2.0. See the COPYING file at the root
// of this distribution or at http://www.apache.org/licenses/LICENSE-2.0

// This mission tests the EXPERIMENTAL_TRIGGER_TIMER feature with a mix of
// nodes that have it enabled vs disabled, under configurable clock drift
// distributions. It uses generated pubnet topologies (--pubnet-data) and
// overlays trigger timer and clock offset settings onto the CoreSets.
//
// CLI parameters:
//   --trigger-timer-flag-pct N        percentage of nodes with the flag (0-100, default 100)
//   --drift-pct N                     percentage of nodes that drift (0-100, default 0)
//   --uniform-drift=lower,upper       uniform random drift in [lower,upper] signed ms (e.g. -2000,+2000)
//   --bimodal-drift=m1,M1,m2,M2      first half in [m1,M1], second half in [m2,M2] signed ms

module MissionTriggerTimerMixConsensus

open Logging
open StellarCoreHTTP
open StellarCorePeer
open StellarCoreSet
open StellarFormation
open StellarMissionContext
open StellarNetworkData
open StellarStatefulSets
open StellarSupercluster

type ClockDriftDistribution =
    | NoDrift
    | UniformDrift of lower: int * upper: int
    | BimodalDrift of min1: int * max1: int * min2: int * max2: int

let private parseDrift (context: MissionContext) : ClockDriftDistribution =
    match context.uniformDrift, context.bimodalDrift with
    | [], [] -> NoDrift
    | [ lower; upper ], [] ->
        if upper < lower then
            failwith (sprintf "uniform-drift requires lower <= upper, got %d,%d" lower upper)
        UniformDrift(lower, upper)
    | [], [ min1; max1; min2; max2 ] ->
        if max1 < min1 then
            failwith (sprintf "bimodal-drift first range requires min <= max, got %d,%d" min1 max1)
        if max2 < min2 then
            failwith (sprintf "bimodal-drift second range requires min <= max, got %d,%d" min2 max2)
        BimodalDrift(min1, max1, min2, max2)
    | _ :: _, _ :: _ -> failwith "Cannot specify both --uniform-drift and --bimodal-drift"
    | u, [] -> failwith (sprintf "--uniform-drift requires exactly 2 values (lower,upper), got %d" u.Length)
    | [], b -> failwith (sprintf "--bimodal-drift requires exactly 4 values (min1,max1,min2,max2), got %d" b.Length)

let triggerTimerMixConsensus (baseContext: MissionContext) =
    let drift = parseDrift baseContext
    let flagPct = baseContext.triggerTimerFlagPct
    let driftPct = baseContext.driftPct

    if flagPct < 0 || flagPct > 100 then
        failwith (sprintf "trigger-timer-flag-pct must be 0-100, got %d" flagPct)

    if driftPct < 0 || driftPct > 100 then
        failwith (sprintf "drift-pct must be 0-100, got %d" driftPct)

    let context =
        { baseContext with
              numAccounts = 20000
              numTxs = 40000
              txRate = 100
              coreResources = MediumTestResources
              genesisTestAccountCount = Some 20000
              installNetworkDelay = Some(baseContext.installNetworkDelay |> Option.defaultValue true)
              maxConnections = Some(baseContext.maxConnections |> Option.defaultValue 65) }

    let baseCoreSets = FullPubnetCoreSets context true false

    let totalNodes =
        List.sumBy (fun (cs: CoreSet) -> cs.options.nodeCount) baseCoreSets

    let numFlagEnabled = (totalNodes * flagPct + 99) / 100
    let numDrifting = (totalNodes * driftPct + 99) / 100

    match drift with
    | NoDrift when numDrifting > 0 ->
        failwith "drift-pct > 0 but no drift distribution specified (use --uniform-drift or --bimodal-drift)"
    | _ -> ()

    LogInfo
        "TriggerTimerMixConsensus: %d total nodes, %d flag-enabled (%d%%), %d drifting (%d%%)"
        totalNodes
        numFlagEnabled
        flagPct
        numDrifting
        driftPct

    // Generate drift offsets deterministically
    let rng = System.Random(context.randomSeed)

    let driftOffsets =
        match drift with
        | NoDrift -> []
        | UniformDrift (lower, upper) ->
            [ for _ in 1..numDrifting do
                  yield rng.Next(lower, upper + 1) ]
        | BimodalDrift (min1, max1, min2, max2) ->
            let firstHalf = numDrifting / 2
            let secondHalf = numDrifting - firstHalf

            let firstOffsets =
                [ for _ in 1..firstHalf do
                      yield rng.Next(min1, max1 + 1) ]

            let secondOffsets =
                [ for _ in 1..secondHalf do
                      yield rng.Next(min2, max2 + 1) ]

            firstOffsets @ secondOffsets

    let driftArr = List.toArray driftOffsets

    // Walk through CoreSets, assigning flag and drift per-CoreSet/per-node.
    // The flag is per-CoreSet (all nodes in a CoreSet share the same flag value).
    // Clock offsets are per-node within each CoreSet.
    let modifiedCoreSets, _ =
        baseCoreSets
        |> List.mapFold
            (fun (flagRemaining, offIdx) cs ->
                let nc = cs.options.nodeCount
                let flagEnabled = flagRemaining > 0
                let newFlagRemaining = max 0 (flagRemaining - nc)

                let nodeOffsets =
                    [ for j in 0 .. nc - 1 do
                          let idx = offIdx + j
                          if idx < driftArr.Length then driftArr.[idx] else 0 ]

                LogInfo
                    "  CoreSet %s: %d nodes, trigger_timer=%b, offsets=%A"
                    cs.name.StringName
                    nc
                    flagEnabled
                    nodeOffsets

                let modified =
                    { cs with
                          options =
                              { cs.options with
                                    experimentalTriggerTimer = Some flagEnabled
                                    clockOffsets = Some nodeOffsets } }

                modified, (newFlagRemaining, offIdx + nc))
            (numFlagEnabled, 0)

    let tier1 =
        List.filter (fun (cs: CoreSet) -> cs.options.tier1 = Some true) modifiedCoreSets

    let nonTier1 =
        List.filter (fun (cs: CoreSet) -> cs.options.tier1 <> Some true) modifiedCoreSets

    context.Execute
        modifiedCoreSets
        None
        (fun (formation: StellarFormation) ->
            formation.WaitUntilConnected modifiedCoreSets
            formation.ManualClose tier1
            formation.WaitUntilSynced modifiedCoreSets

            formation.UpgradeProtocolToLatest tier1
            formation.UpgradeMaxTxSetSize tier1 (context.txRate * 10)

            let loadPeer =
                if nonTier1.Length > 0 then nonTier1.[0] else tier1.[0]

            formation.RunLoadgen loadPeer context.GeneratePaymentLoad

            let peer = formation.NetworkCfg.GetPeer tier1.[0] 0
            peer.WaitForFewLedgers(60) // About 5 minutes of consensus under drift

            formation.CheckNoErrorsAndPairwiseConsistency()
            formation.EnsureAllNodesInSync modifiedCoreSets)
