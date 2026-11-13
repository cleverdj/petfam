#!/usr/bin/env bash
# Builds the macOS downloads: PetFam.app for Apple silicon and for Intel, each zipped
# (artifacts/PetFam-<version>-macos-arm64.zip, artifacts/PetFam-<version>-macos-x64.zip).
# Run on a Mac (or the macOS job in .github/workflows/release.yml):  bash build/build-macos.sh [version]
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
fi
echo "PetFam $VERSION for macOS"

for RID in osx-arm64 osx-x64; do
  OUT="artifacts/publish/$RID"
  rm -rf "$OUT"
  dotnet publish src/PetFam.App/PetFam.App.csproj -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:Version="$VERSION" -o "$OUT"

  STAGE="artifacts/macos-$RID"
  APP="$STAGE/PetFam.app"
  rm -rf "$STAGE"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$OUT"/. "$APP/Contents/MacOS/"
  cp src/PetFam.App/Assets/petfam.icns "$APP/Contents/Resources/PetFam.icns"
  sed "s/__VERSION__/$VERSION/g" installer/macos/Info.plist > "$APP/Contents/Info.plist"
  chmod +x "$APP/Contents/MacOS/PetFam"

  # Ad-hoc signature: Apple silicon refuses to run unsigned code. (A Developer ID signature and notarisation would
  # also remove the "unidentified developer" warning.)
  if command -v codesign >/dev/null; then
    codesign --force --deep --sign - "$APP"
  fi

  ARCH="${RID#osx-}"
  ZIP="artifacts/PetFam-$VERSION-macos-$ARCH.zip"
  rm -f "$ZIP"
  if command -v ditto >/dev/null; then
    ditto -c -k --keepParent "$APP" "$ZIP"
  else
    (cd "$STAGE" && zip -qry "../$(basename "$ZIP")" PetFam.app)
  fi
  echo "  $ZIP"
done
