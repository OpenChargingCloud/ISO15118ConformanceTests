#!/bin/bash
# Build the decoder (once) in WSL against the rig's build of our codec, then decode one trace.
#   dec-run.sh <trace.json> [out.txt]
set -u
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/decode-loop
B=/home/ahzf/everest/decode-loop
mkdir -p "$B"; cp "$SP/Program.cs" "$SP/dec.csproj" "$B/"
cd "$B"
/usr/bin/dotnet build -c Release -v q -nologo -p:ARTIFACTS=/home/ahzf/wsl-artifacts-dis -o "$B/out" > "$B/build.log" 2>&1 || { grep -E "error" "$B/build.log" | head; exit 1; }
if [ $# -gt 1 ]; then /usr/bin/dotnet "$B/out/dec.dll" "$1" > "$2"; echo "wrote $2"; else /usr/bin/dotnet "$B/out/dec.dll" "$1"; fi
