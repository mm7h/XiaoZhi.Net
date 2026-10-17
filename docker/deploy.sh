#!/bin/sh
# 宿主机只需 sh 和 Docker Compose；配置解析由服务端负责。
set -eu

fail() { printf '%s\n' "$*" >&2; exit 1; }
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
runtime="$script_dir/runtime"
action=up
resource_source=

# 先定位运行目录，再读取上次部署参数；不将文件作为 shell 代码执行。
previous=
for argument in "$@"; do
    if [ "$previous" = --runtime-directory ]; then runtime=$argument; fi
    previous=$argument
done
XIAOZHI_BUILD_CONTEXT=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
XIAOZHI_DOCKERFILE=docker/Dockerfile
XIAOZHI_PUBLIC_HOST=
XIAOZHI_WS_PORT=4530
XIAOZHI_OTA_PORT=4531
XIAOZHI_PROJECT_NAME=xiaozhi-samples
if [ -f "$XIAOZHI_BUILD_CONTEXT/package.json" ]; then XIAOZHI_DOCKERFILE=docker/Dockerfile.release; fi
if [ -f "$runtime/deployment.env" ]; then
    while IFS='=' read -r key value; do
        value=${value#\'}; value=${value%\'}
        case "$key" in
            XIAOZHI_BUILD_CONTEXT) XIAOZHI_BUILD_CONTEXT=$value ;;
            XIAOZHI_DOCKERFILE) XIAOZHI_DOCKERFILE=$value ;;
            XIAOZHI_PUBLIC_HOST) XIAOZHI_PUBLIC_HOST=$value ;;
            XIAOZHI_WS_PORT) XIAOZHI_WS_PORT=$value ;;
            XIAOZHI_OTA_PORT) XIAOZHI_OTA_PORT=$value ;;
            XIAOZHI_PROJECT_NAME) XIAOZHI_PROJECT_NAME=$value ;;
        esac
    done < "$runtime/deployment.env"
fi
while [ "$#" -gt 0 ]; do
    case "$1" in
        up|down|status|logs|check) action=$1; shift ;;
        --help)
            printf '%s\n' '用法：sh docker/deploy.sh [up|down|status|logs|check] [选项]' \
                '  --mode release|source       部署模式（运行包自动使用 release）' \
                '  --package-directory PATH    解压后的运行包根目录' \
                '  --resource-source PATH      首次导入 Configs、models、musics 的来源' \
                '  --runtime-directory PATH    持久资源目录，默认 docker/runtime' \
                '  --public-host IP_OR_DOMAIN  设备可访问的服务器地址，首次必填' \
                '  --ws-port PORT              宿主机 WebSocket 端口，默认 4530' \
                '  --ota-port PORT             宿主机 OTA 端口，默认 4531' \
                '  --project-name NAME         Compose 项目名，默认 xiaozhi-samples'
            exit 0 ;;
        --mode|--package-directory|--resource-source|--runtime-directory|--public-host|--ws-port|--ota-port|--project-name)
            [ "$#" -ge 2 ] && [ -n "$2" ] || fail "$1 缺少参数。"
            case "$1" in
                --mode) case "$2" in
                    release) XIAOZHI_DOCKERFILE=docker/Dockerfile.release ;;
                    source) XIAOZHI_DOCKERFILE=docker/Dockerfile ;;
                    *) fail '部署模式必须是 release 或 source。' ;;
                esac ;;
                --package-directory) XIAOZHI_BUILD_CONTEXT=$2 ;;
                --resource-source) resource_source=$2 ;;
                --runtime-directory) runtime=$2 ;;
                --public-host) XIAOZHI_PUBLIC_HOST=$2 ;;
                --ws-port) XIAOZHI_WS_PORT=$2 ;;
                --ota-port) XIAOZHI_OTA_PORT=$2 ;;
                --project-name) XIAOZHI_PROJECT_NAME=$2 ;;
            esac
            shift 2 ;;
        *) fail "未知参数：$1；使用 --help 查看用法。" ;;
    esac
done
case "$XIAOZHI_PUBLIC_HOST" in ''|*[!a-zA-Z0-9.-]*) fail '--public-host 请填写 IPv4 或域名，不带协议、端口和路径。' ;; esac
case "$XIAOZHI_PROJECT_NAME" in ''|[!a-z0-9]*|*[!a-z0-9_-]*) fail '项目名只能包含小写字母、数字、下划线和连字符，且以字母或数字开头。' ;; esac
for port in "$XIAOZHI_WS_PORT" "$XIAOZHI_OTA_PORT"; do
    case "$port" in ''|*[!0-9]*|0*) fail '端口须为 1 至 65535 的整数，不能带前导零。' ;; esac
    [ "${#port}" -le 5 ] && [ "$port" -le 65535 ] || fail '端口须为 1 至 65535。'
done
[ "$XIAOZHI_WS_PORT" != "$XIAOZHI_OTA_PORT" ] || fail '两个服务的宿主机端口不能相同。'
command -v docker >/dev/null 2>&1 || fail '请先安装 Docker 和 Docker Compose。'
[ "$(docker info --format '{{.OSType}} {{.Architecture}}')" = 'linux x86_64' ] || fail '需要 Linux x64 Docker 引擎。'
docker compose version >/dev/null
if [ "$action" = up ]; then
    XIAOZHI_BUILD_CONTEXT=$(CDPATH= cd -- "$XIAOZHI_BUILD_CONTEXT" && pwd -P)
    mkdir -p -- "$runtime"
fi
XIAOZHI_RUNTIME_DIR=$(CDPATH= cd -- "$runtime" && pwd -P)
# 单引号保护 Compose env 文件中的空格、$ 和 #；不支持单引号或换行路径。
for value in "$XIAOZHI_BUILD_CONTEXT" "$XIAOZHI_RUNTIME_DIR"; do
    case "$value" in *\'*|*'
'*) fail '目录路径不能包含单引号或换行。' ;; esac
done
export XIAOZHI_BUILD_CONTEXT XIAOZHI_DOCKERFILE XIAOZHI_RUNTIME_DIR XIAOZHI_PUBLIC_HOST
export XIAOZHI_WS_PORT XIAOZHI_OTA_PORT XIAOZHI_PROJECT_NAME
compose() { docker compose --project-name "$XIAOZHI_PROJECT_NAME" --env-file "$XIAOZHI_RUNTIME_DIR/deployment.env" --file "$script_dir/compose.yaml" "$@"; }

if [ "$action" = up ]; then
    if [ "$XIAOZHI_DOCKERFILE" = docker/Dockerfile.release ]; then
        grep -Eq '"RuntimeIdentifier"[[:space:]]*:[[:space:]]*"linux-x64"' "$XIAOZHI_BUILD_CONTEXT/package.json" || fail 'Release Docker 只支持 linux-x64 运行包。'
        for executable in server/XiaoZhi.Net.Sample.Server ota/XiaoZhi.Net.Sample.OTA.Server; do
            file="$XIAOZHI_BUILD_CONTEXT/$executable"
            [ -f "$file" ] || fail "缺少程序：$file"
            [ "$(od -An -t x1 -N 6 "$file" | tr -d ' \n')" = 7f454c460201 ] && \
                [ "$(od -An -t x1 -j 18 -N 2 "$file" | tr -d ' \n')" = 3e00 ] || fail "程序不是 Linux x64 ELF：$file"
        done
    fi
    if [ ! -f "$XIAOZHI_RUNTIME_DIR/Configs/config.json" ]; then
        for directory in Configs models musics; do
            [ ! -e "$XIAOZHI_RUNTIME_DIR/$directory" ] || fail "已有不完整的资源目录：$directory；请补齐或选择新的运行目录。"
        done
        if [ -z "$resource_source" ]; then
            if [ "$XIAOZHI_DOCKERFILE" = docker/Dockerfile.release ]; then resource_source="$XIAOZHI_BUILD_CONTEXT/server"
            else resource_source="$XIAOZHI_BUILD_CONTEXT/demo/XiaoZhi.Net.Sample.Server/bin/Debug/net8.0"; fi
        fi
        [ -f "$resource_source/Configs/config.json" ] && [ -d "$resource_source/models" ] || fail '资源来源必须包含 Configs/config.json 和 models 目录。'
        staging=$(mktemp -d "$XIAOZHI_RUNTIME_DIR/.import-XXXXXX")
        cp -R -- "$resource_source/Configs" "$resource_source/models" "$staging/"
        if [ -d "$resource_source/musics" ]; then cp -R -- "$resource_source/musics" "$staging/"
        else mkdir -- "$staging/musics"; fi
        for directory in Configs models musics; do mv -- "$staging/$directory" "$XIAOZHI_RUNTIME_DIR/$directory"; done
    fi
    # 已有环境配置不覆盖；JSON 的加载与校验交给服务端。
    production="$XIAOZHI_RUNTIME_DIR/Configs/config.Production.json"
    if [ ! -f "$production" ]; then
        printf '%s\n' '{"WebSocketServerOption":{"IP":"0.0.0.0","Port":4530,"Path":"/xiaozhi/v1/"}}' > "$production"
    fi
    for directory in Configs/assets models musics; do
        [ -d "$XIAOZHI_RUNTIME_DIR/$directory" ] || fail "缺少资源目录：$directory"
    done
    [ -f "$XIAOZHI_RUNTIME_DIR/models/vad/silero-native/model.onnx" ] || fail '缺少 models/vad/silero-native/model.onnx，请先准备模型。'
    {
        printf "XIAOZHI_BUILD_CONTEXT='%s'\nXIAOZHI_DOCKERFILE='%s'\nXIAOZHI_RUNTIME_DIR='%s'\n" "$XIAOZHI_BUILD_CONTEXT" "$XIAOZHI_DOCKERFILE" "$XIAOZHI_RUNTIME_DIR"
        printf "XIAOZHI_PUBLIC_HOST='%s'\nXIAOZHI_WS_PORT='%s'\nXIAOZHI_OTA_PORT='%s'\nXIAOZHI_PROJECT_NAME='%s'\n" "$XIAOZHI_PUBLIC_HOST" "$XIAOZHI_WS_PORT" "$XIAOZHI_OTA_PORT" "$XIAOZHI_PROJECT_NAME"
    } > "$XIAOZHI_RUNTIME_DIR/deployment.env"
    compose config --quiet
    compose build
    compose up --detach --force-recreate --wait --wait-timeout 240
    printf '服务已启动。设备 OTA 地址：http://%s:%s/xiaozhi/ota\n' "$XIAOZHI_PUBLIC_HOST" "$XIAOZHI_OTA_PORT"
else
    [ -f "$XIAOZHI_RUNTIME_DIR/deployment.env" ] || fail '找不到 deployment.env，请先使用此 sh 入口部署。'
    case "$action" in
        down) compose down ;;
        status) compose ps --all ;;
        logs) compose logs --tail 100 --follow ;;
        check)
            status=$(compose ps --all --format '{{.Service}} {{.State}} {{.Health}}')
            for service in sample-server ota-server; do
                printf '%s\n' "$status" | grep -qx "$service running healthy" || fail "容器未运行或不健康：$service"
            done
            printf '%s\n' '两个容器均运行且健康；请继续检查 OTA 返回地址和实际设备语音对话。' ;;
    esac
fi
