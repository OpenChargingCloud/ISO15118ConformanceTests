#!/usr/bin/env bash
# Mirror the ct-dis worktree into ~/i15118-dis and build the interop project there; exit = build's exit.
SRC=/mnt/d/Coding/OpenChargingCloud/ct-dis/
DST=/home/ahzf/i15118-dis/
rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git --exclude .vs/ --exclude TestResults/ "$SRC" "$DST"
echo "rsync exit $?"
cd "$DST"
/usr/bin/dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
  --artifacts-path /home/ahzf/wsl-artifacts-dis -v q -nologo > /home/ahzf/everest/i15118-dis-build.log 2>&1
rc=$?
echo "build exit $rc"
grep -E "error CS|Fehler" /home/ahzf/everest/i15118-dis-build.log | head -10
exit $rc
