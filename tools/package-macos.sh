#!/usr/bin/env bash
# Makes Blockage.app out of a macOS publish, signs it, and zips it for download:
#
#   dotnet publish src/EditorApp -p:PublishProfile=osx-arm64
#   bash tools/package-macos.sh publish/osx-arm64 0.9.0 dist
#
# leaves dist/Blockage.app and dist/Blockage-0.9.0-osx-arm64.zip.
#
# The app is signed ad hoc, which is all Apple Silicon asks before it runs a program, unless
# MACOS_SIGNING_IDENTITY names a Developer ID certificate in the keychain. Then it is signed with that,
# with the hardened runtime and the entitlements .NET needs, ready to be notarized. codesign and ditto
# are the Mac's own: anywhere else the bundle is only assembled, so its layout can be looked over.
set -euo pipefail

publish=$1
version=$2
out=$3
root=$(cd "$(dirname "$0")/.." && pwd)
app="$out/Blockage.app"

# A bundle's version is numbers only; 0.9.0-dev is 0.9.0 to the Finder.
short=${version%%-*}

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish"/. "$app/Contents/MacOS/"
cp "$root/assets/icon/Blockage.icns" "$app/Contents/Resources/"
sed "s/@VERSION@/$short/g" "$root/assets/macos/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/Blockage"

if ! command -v codesign > /dev/null; then
    echo "Not on a Mac: $app is assembled, but neither signed nor zipped."
    exit 0
fi

if [ -n "${MACOS_SIGNING_IDENTITY:-}" ]; then
    # From the inside out, as Apple asks: the libraries, then the app round them.
    for library in "$app"/Contents/MacOS/*.dylib; do
        codesign --force --timestamp --options runtime --sign "$MACOS_SIGNING_IDENTITY" "$library"
    done
    codesign --force --timestamp --options runtime \
        --entitlements "$root/assets/macos/Blockage.entitlements" \
        --sign "$MACOS_SIGNING_IDENTITY" "$app"
else
    codesign --force --deep --sign - "$app"
fi

codesign --verify --deep --strict "$app"

# Without the files' extended attributes, which ditto would otherwise keep beside each as a ._ file:
# the Finder folds those back in, but a plain unzip leaves them in the bundle, and a bundle with
# files its signature does not list will not open.
ditto -c -k --norsrc --keepParent "$app" "$out/Blockage-$version-osx-arm64.zip"
