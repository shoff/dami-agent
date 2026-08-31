#!/usr/bin/env bash
#
# Dami Core — build the desktop client and install it for the current user:
# a Release publish in ~/.local/opt/dami-gui, the icon set in the user hicolor
# theme, and a start-menu entry. Everything is user-local; no sudo anywhere.
#
#   tools/install-gui.sh            publish from the tree, then install
#   tools/install-gui.sh --no-build install what is already in ~/.local/opt/dami-gui
#
# The Exec path is written absolute because .desktop files do not expand $HOME.
set -euo pipefail

if [[ ${EUID} -eq 0 ]]; then
    echo "install-gui: run this as steve, not root — it installs into \$HOME." >&2
    exit 2
fi

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="$HOME/.local/opt/dami-gui"
APPS="$HOME/.local/share/applications"
ICONS="$HOME/.local/share/icons/hicolor"
ASSETS="$REPO/Dami/src/Dami.Gui/Assets"

BUILD=1
for arg in "$@"; do
    case "$arg" in
        --no-build) BUILD=0 ;;
        *) echo "install-gui: unknown option $arg" >&2; exit 2 ;;
    esac
done

if [[ $BUILD -eq 1 ]]; then
    dotnet publish "$REPO/Dami/src/Dami.Gui" -c Release -o "$TARGET"
fi

if [[ ! -x "$TARGET/Dami.Gui" ]]; then
    echo "install-gui: $TARGET/Dami.Gui is missing or not executable; nothing installed." >&2
    exit 1
fi

# PNGs only: this host's gdk-pixbuf has no SVG loader, so a scalable icon would
# silently not render in the menu. The SVG stays in Assets as the drawing.
for size in 16 24 32 48 64 128 256; do
    install -D -m 0644 "$ASSETS/icons/dami-$size.png" \
        "$ICONS/${size}x${size}/apps/dami.png"
done

install -d -m 0755 "$APPS"
cat > "$APPS/dami.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Dami
GenericName=Personal agent console
Comment=Conversation, execution graph, task boards, and the health dashboard over the local Dami runtime
Exec=$TARGET/Dami.Gui
Icon=dami
Terminal=false
Categories=Utility;
StartupWMClass=Dami.Gui
DESKTOP
chmod 0644 "$APPS/dami.desktop"

# Refresh the caches where the tools exist; both are optional on a user install.
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -f -t "$ICONS" >/dev/null 2>&1 || true

echo "install-gui: installed."
echo "  app     $TARGET/Dami.Gui"
echo "  menu    $APPS/dami.desktop  (Menu > Dami; may need a menu reopen to appear)"
echo "  icons   $ICONS/<size>/apps/dami.png"
