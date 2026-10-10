#!/usr/bin/env bash
# 兼容 Bash 3.2（macOS 默认版本）；使用 curl 和 GNU/BSD tar，无需 Python 或 jq。
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
manifest="$script_dir/SherpaModels.psv"
release_base=https://github.com/k2-fsa/sherpa-onnx/releases/download
destination="$script_dir/../models"
models=''
all=0
keep_archives=0
force=0
temporary_archive=''

fail() { printf '%s\n' "$*" >&2; exit 1; }
cleanup() {
    # 只删除本次创建的单个临时压缩包。
    if [[ -n "$temporary_archive" && -f "$temporary_archive" ]]; then
        rm -f "$temporary_archive"
    fi
}
trap cleanup EXIT

usage() {
    printf '%s\n' \
        '用法：bash scripts/Download-SherpaModels.sh [选项]' \
        '  --models Silero,OnlineParaformer,Kokoro  选择模型（名称不区分大小写）' \
        '  --all                                  下载全部模型' \
        '  --destination PATH                     自定义 models 目录' \
        '  --keep-archives                        保留压缩包' \
        '  --force                                重新下载并覆盖模型文件' \
        '  --help                                 显示帮助' \
        '不带模型参数时只显示模型列表。'
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --models|--destination)
            [[ $# -ge 2 && -n "$2" && "$2" != --* ]] || fail "$1 缺少参数。"
            if [[ "$1" == --models ]]; then models="$2"; else destination="$2"; fi
            shift 2 ;;
        --all) all=1; shift ;;
        --keep-archives) keep_archives=1; shift ;;
        --force) force=1; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "未知参数：$1。使用 --help 查看用法。" ;;
    esac
done

if [[ "$all" == 0 && -z "${models//[[:space:],]/}" ]]; then
    usage
    awk -F '|' 'NR > 1 { printf "%s\t%s\t%s\n", $2, $1, $3 }' "$manifest"
    exit 0
fi

# 先验证全部名称，再开始下载；ENVIRON 避免 awk 对用户参数中的反斜杠进行转义。
selected=$(SHERPA_MODELS="$models" SHERPA_ALL="$all" awk -F '|' '
    BEGIN {
        count = split(ENVIRON["SHERPA_MODELS"], names, ",")
        for (i = 1; i <= count; i++) {
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", names[i])
            if (names[i] != "") requested[tolower(names[i])] = names[i]
        }
    }
    NR > 1 {
        key = tolower($1)
        if (ENVIRON["SHERPA_ALL"] == "1" || key in requested) print
        delete requested[key]
    }
    END {
        if (ENVIRON["SHERPA_ALL"] != "1") {
            for (key in requested) { print "未知模型：" requested[key] > "/dev/stderr"; invalid = 1 }
            if (invalid) exit 1
        }
    }
' "$manifest") || exit 1

command -v curl >/dev/null || fail '未找到 curl，请使用系统包管理器安装。'
if [[ "$selected" == *'.tar.bz2'* ]]; then
    command -v tar >/dev/null || fail '未找到 tar，请使用系统包管理器安装。'
    tar -tf "$script_dir/tar-bzip2-probe.tar.bz2" >/dev/null ||
        fail '当前 tar 无法读取 .tar.bz2。GNU tar 需要 bzip2；BSD tar 需要编译时启用 bzip2。无需 Python。'
fi
mkdir -p "$destination"
destination=$(cd "$destination" && pwd)

installed() {
    local required
    local files=()
    IFS=',' read -r -a files <<< "$1"
    for required in "${files[@]}"; do
        [[ -e "$target/$required" ]] || return 1
    done
}

download() {
    printf '下载 %s\n' "${1##*/}"
    # 不使用较新版本 curl 才提供的 --retry-all-errors，兼容 macOS 自带 curl。
    curl -fL --retry 5 --retry-delay 5 --connect-timeout 30 --output "$2" "$release_base/$1"
}

while IFS='|' read -r name category directory assets mappings required manual; do
    target="$destination/$directory"
    if installed "$required" && [[ "$force" == 0 ]]; then
        printf '已就绪，跳过 %s：%s\n' "$name" "$target"
    else
        if [[ -e "$target" && "$force" == 0 ]]; then
            fail "目标目录不完整：$target。请检查已有内容，确认需要覆盖后加 --force。"
        fi
        mkdir -p "$target"
        printf '处理 %s -> %s\n' "$name" "$target"
        IFS=';' read -r -a asset_list <<< "$assets"
        for asset in "${asset_list[@]}"; do
            if [[ "$asset" == *=* ]]; then
                output="$target/${asset#*=}"
                if [[ ! -e "$output" || "$force" == 1 ]]; then download "${asset%%=*}" "$output"; fi
            else
                if [[ "$keep_archives" == 1 ]]; then
                    mkdir -p "$destination/.sherpa-archives"
                    archive="$destination/.sherpa-archives/${asset##*/}"
                else
                    temporary_archive=$(mktemp "${TMPDIR:-/tmp}/xiaozhi-sherpa.XXXXXX")
                    archive="$temporary_archive"
                fi
                download "$asset" "$archive"
                printf '解压 %s\n' "${asset##*/}"
                tar -xf "$archive" -C "$target" --strip-components=1
                cleanup
                temporary_archive=''
            fi
        done

        if [[ "$mappings" != '-' ]]; then
            IFS=';' read -r -a mapping_list <<< "$mappings"
            for mapping in "${mapping_list[@]}"; do
                output_name=${mapping%%=*}
                kind=f
                if [[ "$output_name" == */ ]]; then kind=d; output_name=${output_name%/}; fi
                IFS=',' read -r -a candidates <<< "${mapping#*=}"
                found=''
                for candidate in "${candidates[@]}"; do
                    found=$(find "$target" -type "$kind" -name "$candidate" -print | sed -n '1p')
                    [[ -z "$found" ]] || break
                done
                [[ -n "$found" ]] || fail "未找到 $name 所需的 $output_name，候选：${mapping#*=}。"
                output="$target/$output_name"
                if [[ "$found" == "$output" ]]; then continue; fi
                if [[ -e "$output" ]]; then
                    if [[ "$force" == 0 || "$kind" == d ]]; then
                        printf '保留已有文件/目录：%s\n' "$output"
                        continue
                    fi
                fi
                mv -f "$found" "$output"
            done
        fi
        installed "$required" || fail "$name 安装后缺少项目所需文件。"
    fi
    if [[ "$manual" != '-' ]]; then printf '%s 还需要由你提供：%s。\n' "$name" "$manual" >&2; fi
done <<< "$selected"
