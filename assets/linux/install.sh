#!/bin/sh
# Puts Blockage in the applications menu from wherever this folder is: a menu entry and an icon in
# your own ~/.local/share, nothing for the whole system and nothing that needs root. Run it again
# after moving the folder. ./install.sh --remove takes both away again.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
entry="$data/applications/blockage.desktop"
icon="$data/icons/hicolor/256x256/apps/blockage.png"

if [ "${1:-}" = "--remove" ]; then
    rm -f "$entry" "$icon"
    echo "Blockage is out of the applications menu."
    exit 0
fi

mkdir -p "$(dirname "$entry")" "$(dirname "$icon")"
cp "$here/blockage.png" "$icon"
cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Blockage
GenericName=Voxel level editor
Comment=Shape, paint and light voxel levels, and export them for a game engine
Exec="$here/Blockage" %f
Icon=blockage
Terminal=false
Categories=Graphics;3DGraphics;
EOF

# Some desktops only notice a new entry when their database of them is rebuilt.
update-desktop-database "$data/applications" 2> /dev/null || true
echo "Blockage is in the applications menu."
