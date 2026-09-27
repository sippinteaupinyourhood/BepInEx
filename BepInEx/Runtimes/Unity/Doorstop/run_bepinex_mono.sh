#!/bin/sh











executable_name=""







enabled="1"



target_assembly="BepInEx/core/BepInEx.Unity.Mono.Preloader.dll"


boot_config_override=



ignore_disable_switch="0"









dll_search_path_override="BepInEx/core"


debug_enable="0"


debug_address="127.0.0.1:10000"


debug_suspend="0"




coreclr_path=""


corlib_dir=""



set -e




for a in "$@"; do
    if [ "$a" = "SteamLaunch" ]; then
        rotated=0; max=$#
        while [ $rotated -lt $max ]; do

            if [ "$1" != "${1#"${PWD%/}/"}" ]; then
                to_rotate=$(($# - rotated))
                set -- "$@" "$0"
                while [ $((to_rotate-=1)) -ge 0 ]; do
                    set -- "$@" "$1"
                    shift
                done
                exec "$@"
            else
                set -- "$@" "$1"
                shift
                rotated=$((rotated+1))
            fi
        done
        echo "Could not determine game executable launched by Steam" 1>&2
        exit 1
    fi
done


if [ -x "$1" ] ; then
    executable_name="$1"
    shift
fi

if [ -z "${executable_name}" ] || [ ! -x "${executable_name}" ]; then
    echo "Please set executable_name to a valid name in a text editor or as the first command line parameter" 1>&2
    exit 1
fi


a="/$0"; a=${a%/*}; a=${a#/}; a=${a:-.}; BASEDIR=$(cd "$a" || exit; pwd -P)

arch=""
executable_path=""
lib_extension=""

abs_path() {

    if [ "$1" = "${1#/}" ]; then
        set -- "${BASEDIR}/${1}"
    fi
    echo "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
}


os_type="$(uname -s)"
case ${os_type} in
    Linux*)
        executable_path="$(abs_path "$executable_name")"
        lib_extension="so"
    ;;
    Darwin*)
        real_executable_name="$(abs_path "$executable_name")"


        case $real_executable_name in
            *.app/Contents/MacOS/*)
                executable_path="${executable_name}"
            ;;
            *)

                if [ "$real_executable_name" = "${real_executable_name%.app}" ]; then
                    real_executable_name="${real_executable_name}.app"
                fi
                inner_executable_name=$(defaults read "${real_executable_name}/Contents/Info" CFBundleExecutable)
                executable_path="${real_executable_name}/Contents/MacOS/${inner_executable_name}"
            ;;
        esac
        lib_extension="dylib"


        cpu_type="$(sysctl -n machdep.cpu.brand_string)"
        case "${cpu_type}" in
            Apple*)
                is_apple_silicon=1
            ;;
        esac
    ;;
    *)

        echo "Unknown operating system ($(uname -s))" 1>&2
        echo "Make an issue at https://github.com/NeighTools/UnityDoorstop" 1>&2
        exit 1
    ;;
esac

_readlink() {

    ab_path="$(abs_path "$1")"
    link="$(readlink "${ab_path}")"
    case $link in
        /*);;
        *) link="$(dirname "$ab_path")/$link";;
    esac
    echo "$link"
}

resolve_executable_path () {
    e_path="$(abs_path "$1")"

    while [ -L "${e_path}" ]; do
        e_path=$(_readlink "${e_path}");
    done
    echo "${e_path}"
}


executable_path=$(resolve_executable_path "${executable_path}")


file_out="$(LD_PRELOAD="" file -b "${executable_path}")"
case "${file_out}" in
    *PE32*)
        echo "The executable is a Windows executable file. You must use Wine/Proton and BepInEx for Windows with this executable." 1>&2
        echo "Uninstall BepInEx for *nix and install BepInEx for Windows instead." 1>&2
        echo "More info: https://docs.bepinex.dev/articles/advanced/steam_interop.html#protonwine" 1>&2
        exit 1
    ;;
    *shell\ script*)


        arch="x64"
    ;;
    *64-bit*)
        arch="x64"
    ;;
    *32-bit*)
        arch="x86"
    ;;
    *)
        echo "The executable \"${executable_path}\" is not compiled for x86 or x64 (might be ARM?)" 1>&2
        echo "If you think this is a mistake (or would like to encourage support for other architectures)" 1>&2
        echo "Please make an issue at https://github.com/NeighTools/UnityDoorstop" 1>&2
        echo "Got: ${file_out}" 1>&2
        exit 1
    ;;
esac


doorstop_bool() {
    case "$1" in
        TRUE|true|t|T|1|Y|y|yes)
            echo "1"
        ;;
        FALSE|false|f|F|0|N|n|no)
            echo "0"
        ;;
    esac
}


i=0; max=$#
while [ $i -lt $max ]; do
    case "$1" in
        --doorstop_enabled) # For backwards compatibility. Renamed to --doorstop-enabled
            enabled="$(doorstop_bool "$2")"
            shift
            i=$((i+1))
        ;;
        --doorstop_target_assembly) # For backwards compatibility. Renamed to --doorstop-target-assembly
            target_assembly="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-enabled)
            enabled="$(doorstop_bool "$2")"
            shift
            i=$((i+1))
        ;;
        --doorstop-target-assembly)
            target_assembly="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-boot-config-override)
            boot_config_override="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-mono-dll-search-path-override)
            dll_search_path_override="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-mono-debug-enabled)
            debug_enable="$(doorstop_bool "$2")"
            shift
            i=$((i+1))
        ;;
        --doorstop-mono-debug-suspend)
            debug_suspend="$(doorstop_bool "$2")"
            shift
            i=$((i+1))
        ;;
        --doorstop-mono-debug-address)
            debug_address="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-clr-runtime-coreclr-path)
            coreclr_path="$2"
            shift
            i=$((i+1))
        ;;
        --doorstop-clr-corlib-dir)
            corlib_dir="$2"
            shift
            i=$((i+1))
        ;;
        *)
            set -- "$@" "$1"
        ;;
    esac
    shift
    i=$((i+1))
done

target_assembly="$(abs_path "$target_assembly")"


export DOORSTOP_ENABLED="$enabled"
export DOORSTOP_TARGET_ASSEMBLY="$target_assembly"
export DOORSTOP_BOOT_CONFIG_OVERRIDE="$boot_config_override"
export DOORSTOP_IGNORE_DISABLED_ENV="$ignore_disable_switch"
export DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE="$dll_search_path_override"
export DOORSTOP_MONO_DEBUG_ENABLED="$debug_enable"
export DOORSTOP_MONO_DEBUG_ADDRESS="$debug_address"
export DOORSTOP_MONO_DEBUG_SUSPEND="$debug_suspend"
export DOORSTOP_CLR_RUNTIME_CORECLR_PATH="$coreclr_path.$lib_extension"
export DOORSTOP_CLR_CORLIB_DIR="$corlib_dir"


doorstop_directory="${BASEDIR}/"
doorstop_name="libdoorstop.${lib_extension}"

export LD_LIBRARY_PATH="${doorstop_directory}:${corlib_dir}:${LD_LIBRARY_PATH}"
if [ -z "$LD_PRELOAD" ]; then
    export LD_PRELOAD="${doorstop_name}"
else
    export LD_PRELOAD="${doorstop_name}:${LD_PRELOAD}"
fi

export DYLD_LIBRARY_PATH="${doorstop_directory}:${DYLD_LIBRARY_PATH}"
if [ -z "$DYLD_INSERT_LIBRARIES" ]; then
    export DYLD_INSERT_LIBRARIES="${doorstop_name}"
else
    export DYLD_INSERT_LIBRARIES="${doorstop_name}:${DYLD_INSERT_LIBRARIES}"
fi

if [ -n "${is_apple_silicon}" ]; then
    export ARCHPREFERENCE="arm64,x86_64"





    exec arch -e DYLD_INSERT_LIBRARIES="${DYLD_INSERT_LIBRARIES}" "$executable_path" "$@"
else
    exec "$executable_path" "$@"
fi
