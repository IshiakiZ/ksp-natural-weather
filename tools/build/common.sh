# Shared by every build script here (sourced, not run): finds the game, the C# compiler and the game's own
# libraries, and defines compile().
#
#   KSP_DIR=/path/to/KSP   where the game is, if it is not where Steam puts it
#
# Needs the .NET SDK (on a Mac: brew install dotnet). On Windows, run the build scripts in Git Bash.
if [ -z "${KSP_DIR:-}" ]; then
  for candidate in "$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program" \
                   "$HOME/.steam/steam/steamapps/common/Kerbal Space Program" \
                   "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" \
                   "/c/Program Files (x86)/Steam/steamapps/common/Kerbal Space Program" \
                   "/mnt/c/Program Files (x86)/Steam/steamapps/common/Kerbal Space Program"; do
    [ -d "$candidate" ] && KSP_DIR="$candidate" && break
  done
fi
KSP_DIR="${KSP_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program}"

MANAGED=""
for candidate in "$KSP_DIR/KSP.app/Contents/Resources/Data/Managed" "$KSP_DIR/KSP_x64_Data/Managed" "$KSP_DIR/KSP_Data/Managed"; do
  [ -f "$candidate/Assembly-CSharp.dll" ] && MANAGED="$candidate" && break
done
[ -n "$MANAGED" ] || { echo "error: no KSP install found at: $KSP_DIR (set KSP_DIR)" >&2; exit 1; }

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# (the dotnet on the path; or, where that one has no SDK, the one in DOTNET_ROOT: on Windows the SDK can be installed for one
# user alone, beside a dotnet in Program Files that has only the runtime)
DOTNET=dotnet
if [ -z "$(dotnet --list-sdks 2>/dev/null)" ] && [ -n "${DOTNET_ROOT:-}" ] && [ -n "$("$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null)" ]; then DOTNET="$DOTNET_ROOT/dotnet"; fi
command -v "$DOTNET" >/dev/null 2>&1 || { echo "error: the .NET SDK is needed to build (on a Mac: brew install dotnet)" >&2; exit 1; }
# (Windows ends each line it prints with a CR as well)
SDK_LINE="$("$DOTNET" --list-sdks | tr -d '\r' | tail -1)"
SDK_VERSION="${SDK_LINE%% *}"
SDK_ROOT="${SDK_LINE#*[}"; SDK_ROOT="${SDK_ROOT%]}"
CSC="$SDK_ROOT/$SDK_VERSION/Roslyn/bincore/csc.dll"
[ -f "$CSC" ] || { echo "error: C# compiler not found at $CSC" >&2; exit 1; }

REFS=()
for dll in mscorlib System System.Core Assembly-CSharp Assembly-CSharp-firstpass KSPAssets; do REFS+=("-r:$MANAGED/$dll.dll"); done
for dll in "$MANAGED"/UnityEngine*.dll; do REFS+=("-r:$dll"); done

compile() {  # compile <out> <defines> <source dir> [more source dirs]   (WITH="a.dll:b.dll": more libraries to build against)
  local out="$1" defines="$2"; shift 2
  local sources=() more=() dll
  while IFS= read -r -d '' f; do sources+=("$f"); done < <(find "$@" -name '*.cs' -print0 | sort -z)
  while IFS= read -r -d ':' dll; do [ -n "$dll" ] && more+=("-r:$dll"); done <<< "${WITH:-}:"
  "$DOTNET" "$CSC" -nologo -noconfig -nostdlib -target:library -optimize+ -debug- -langversion:9.0 \
    -nowarn:CS1701,CS1702,CS0649 ${defines:+-define:$defines} "${REFS[@]}" ${more[@]+"${more[@]}"} "-out:$out" "${sources[@]}"
}

TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
