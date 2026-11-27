#!/usr/bin/env bash
# Mirror the ct-repro worktree (with the receipt fix) into ~/i15118 and build it there.
set -u
SRC=/mnt/d/Coding/OpenChargingCloud/ct-repro/
DST=/home/ahzf/i15118/
rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git --exclude node_modules/ \
      --exclude 'docs/interop-runs/' --exclude onlyLocalDoNotCommit/ "$SRC" "$DST"
touch $DST/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_Session/StateMachines/Iso2/Evcc2.cs \
      $DST/ISO15118ConformanceTests.Simulation/Interop/InteropEnvironment.cs
grep -c "TamperMeteringReceipt" $DST/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_Session/StateMachines/Iso2/Evcc2.cs
cd $DST || exit 1
dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
  --artifacts-path /home/ahzf/wsl-artifacts 2>&1 | tail -3
echo "build exit ${PIPESTATUS[0]}"
ls -la --time-style=full-iso /home/ahzf/wsl-artifacts/bin/ISO15118ConformanceTests.Simulation/release/ISO15118ConformanceTests.Simulation.dll
