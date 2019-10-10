// Copyright 2019 Stellar Development Foundation and contributors. Licensed
// under the Apache License, Version 2.0. See the COPYING file at the root
// of this distribution or at http://www.apache.org/licenses/LICENSE-2.0

module StellarMissionContext

open k8s

open StellarCoreHTTP
open StellarCorePeer
open StellarDataDump
open StellarDestination
open StellarNetworkCfg
open StellarPerformanceReporter
open StellarPersistentVolume
open StellarSupercluster
open StellarCoreSet

let GetOrDefault optional def =
    match optional with
    | Some(x) -> x
    | _ -> def

type MissionContext =
    { kube : Kubernetes
      destination : Destination
      image : string option
      oldImage : string option
      txRate : int
      maxTxRate : int
      numAccounts : int
      numTxs : int
      numNodes : int
      quotas: NetworkQuotas
      logLevels: LogLevels
      ingressDomain : string
      persistentVolume : PersistentVolume
      namespaceProperty : string
      keepData : bool
      probeTimeout : int }

    member self.MakeFormation (coreSetList: CoreSet list) (passphrase: NetworkPassphrase option) : ClusterFormation =
        let networkCfg =
            MakeNetworkCfg coreSetList
                self.namespaceProperty
                self.quotas
                self.logLevels
                self.ingressDomain passphrase
        self.kube.MakeFormation networkCfg (Some(self.persistentVolume)) self.keepData self.probeTimeout

    member self.MakeFormationForJob (opts:CoreSetOptions) (passphrase: NetworkPassphrase option) : ClusterFormation =
        let networkCfg =
            MakeNetworkCfg []
                self.namespaceProperty
                self.quotas
                self.logLevels
                self.ingressDomain passphrase
        let networkCfg = { networkCfg with jobCoreSetOptions = Some(opts) }
        self.kube.MakeFormation networkCfg None self.keepData self.probeTimeout

    member self.Execute (coreSetList: CoreSet list) (passphrase: NetworkPassphrase option) (run:ClusterFormation->unit) : unit =
      use formation = self.MakeFormation coreSetList passphrase
      try
          try
              formation.WaitUntilReady()
              run formation
              formation.CheckNoErrorsAndPairwiseConsistency()
          finally
             formation.DumpData self.destination
      with
      | x -> (
                if self.keepData then formation.KeepData()
                reraise()
             )

    member self.ExecuteWithPerformanceReporter
            (coreSetList: CoreSet list) (passphrase: NetworkPassphrase option)
            (run:ClusterFormation->PerformanceReporter->unit) : unit =
      use formation = self.MakeFormation coreSetList passphrase
      let performanceReporter = PerformanceReporter formation.NetworkCfg
      try
          try
              formation.WaitUntilReady()
              run formation performanceReporter
              formation.CheckNoErrorsAndPairwiseConsistency()
          finally
              performanceReporter.DumpPerformanceMetrics self.destination
              formation.DumpData self.destination
      with
      | x -> (
                if self.keepData then formation.KeepData()
                reraise()
             )

    member self.WithNominalLoad : MissionContext =
      { self with numTxs = 100; numAccounts = 100 }

    member self.GenerateAccountCreationLoad : LoadGen =
      { mode = GenerateAccountCreationLoad
        accounts = self.numAccounts
        txs = 0
        txrate = self.txRate
        offset = 0
        batchsize = 100 }

    member self.GeneratePaymentLoad : LoadGen=
      { mode = GeneratePaymentLoad
        accounts = self.numAccounts
        txs = self.numTxs
        txrate = self.txRate
        offset = 0
        batchsize = 100 }
