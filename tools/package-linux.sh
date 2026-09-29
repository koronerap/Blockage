#!/usr/bin/env bash
# Makes the Linux download out of a Linux publish:
#
#   dotnet publish src/EditorApp -p:PublishProfile=linux-x64
#   bash tools/package-linux.sh publish/linux-x64 0.9.0 dist
#
# leaves dist/Blockage-0.9.0-linux-x64.tar.gz: the build, the icon, and install.sh to put Blockage in
# the applications menu. Make it on Linux, so the tar records that Blockage and install.sh can be run.
set -euo pipefail

publish=$1
version=$2
out=$3
root=$(cd "$(dirname "$0")/.." && pwd)
name="Blockage-$version-linux-x64"
stage="$out/$name"

rm -rf "$stage"
mkdir -p "$stage"
cp -R "$publish"/. "$stage/"
cp "$root/assets/icon/Blockage.png" "$stage/blockage.png"
cp "$root/assets/linux/install.sh" "$stage/install.sh"
chmod +x "$stage/Blockage" "$stage/install.sh"
tar -czf "$out/$name.tar.gz" -C "$out" "$name"
