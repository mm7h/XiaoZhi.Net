#!/bin/sh
# 不启动容器，用替身检查导入、参数保存和失败行为。
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd -P)
test_root=$(mktemp -d "${TMPDIR:-/tmp}/xiaozhi-deploy-XXXXXX")
package="$test_root/package with spaces"
runtime="$test_root/runtime \$literal #1"
mkdir -p "$test_root/bin" "$package/docker" "$package/server/Configs/assets" "$package/server/models/vad/silero-native" "$package/ota"
cp "$script_dir/deploy.sh" "$package/docker/"
printf '%s\n' '{"RuntimeIdentifier":"linux-x64"}' > "$package/package.json"
printf '%s\n' '{}' > "$package/server/Configs/config.json"
printf '%s\n' '{"KeepExisting":true}' > "$package/server/Configs/config.Production.json"
printf '%s\n' 'model-placeholder' > "$package/server/models/vad/silero-native/model.onnx"
for file in server/XiaoZhi.Net.Sample.Server ota/XiaoZhi.Net.Sample.OTA.Server; do
    printf '\177ELF\002\001\000\000\000\000\000\000\000\000\000\000\000\000\076\000' > "$package/$file"
done
cat > "$test_root/bin/docker" <<'MOCK'
#!/bin/sh
set -eu
if [ "$1" = info ]; then printf '%s\n' "${TEST_ENGINE:-linux x86_64}"; exit 0; fi
shift
if [ "$1" = version ]; then exit 0; fi
while [ "$#" -gt 0 ]; do
    case "$1" in --project-name|--env-file|--file) shift 2 ;; *) break ;; esac
done
printf '%s|%s|%s|%s|%s|%s\n' "$*" "$XIAOZHI_PUBLIC_HOST" "$XIAOZHI_WS_PORT" "$XIAOZHI_OTA_PORT" "$XIAOZHI_BUILD_CONTEXT" "$XIAOZHI_RUNTIME_DIR" >> "$TEST_LOG"
case "$1" in
    build) [ "${TEST_BUILD_FAIL:-0}" = 0 ] ;;
    ps) printf '%s\n' 'sample-server running healthy' "ota-server running ${TEST_HEALTH:-healthy}" ;;
esac
MOCK
chmod +x "$test_root/bin/docker"
PATH="$test_root/bin:$PATH"
TEST_LOG="$test_root/docker.log"
export PATH TEST_LOG
deploy() { sh "$package/docker/deploy.sh" "$@" --runtime-directory "$runtime"; }
fail() { printf '%s\n' "$*" >&2; exit 1; }

deploy --public-host 192.168.1.100 --ws-port 14530 --ota-port 14531
[ -f "$runtime/models/vad/silero-native/model.onnx" ] && [ -d "$runtime/musics" ] || fail '首次资源导入失败。'
cmp "$package/server/Configs/config.Production.json" "$runtime/Configs/config.Production.json"
printf '%s\n' '{"LocalEdit":true}' > "$runtime/Configs/config.json"
printf '%s\n' '{"PackageEdit":true}' > "$package/server/Configs/config.json"
deploy up
grep -q LocalEdit "$runtime/Configs/config.json" || fail '重复部署覆盖了现有配置。'
grep -F "up --detach --force-recreate --wait --wait-timeout 240|192.168.1.100|14530|14531|$package|$runtime" "$TEST_LOG" >/dev/null
deploy check
deploy status
deploy logs
deploy down
deploy up --public-host server.local --ws-port 24530
deploy status
grep -F "ps --all|server.local|24530|14531|$package|$runtime" "$TEST_LOG" >/dev/null || fail '参数覆盖或保存失败。'

# 错误端口、架构、构建或健康状态都必须返回失败。
if deploy --ws-port 14531 > "$test_root/invalid-port.log" 2>&1; then fail '允许了重复端口。'; fi
TEST_ENGINE='linux aarch64'; export TEST_ENGINE
if deploy up > "$test_root/invalid-engine.log" 2>&1; then fail '允许了错误 Docker 架构。'; fi
unset TEST_ENGINE
printf '%s\n' 'not-an-ELF' > "$package/ota/XiaoZhi.Net.Sample.OTA.Server"
if deploy up > "$test_root/invalid-package.log" 2>&1; then fail '允许了错误运行包。'; fi
printf '\177ELF\002\001\000\000\000\000\000\000\000\000\000\000\000\000\076\000' > "$package/ota/XiaoZhi.Net.Sample.OTA.Server"
TEST_BUILD_FAIL=1; export TEST_BUILD_FAIL
before=$(wc -l < "$TEST_LOG")
if deploy up > "$test_root/build-failure.log" 2>&1; then fail '忽略了构建失败。'; fi
[ "$(wc -l < "$TEST_LOG")" -eq "$((before + 2))" ] || fail '构建失败后仍启动了容器。'
unset TEST_BUILD_FAIL
TEST_HEALTH=unhealthy; export TEST_HEALTH
if deploy check > "$test_root/unhealthy.log" 2>&1; then fail '忽略了不健康的容器。'; fi
unset TEST_HEALTH

# Source 模式不检查 Release 二进制；没有环境配置时自动补充容器监听配置。
mkdir "$test_root/source-runtime"
sh "$package/docker/deploy.sh" --mode source --resource-source "$package/server" --runtime-directory "$test_root/source-runtime" --public-host server.local
grep -q 'docker/Dockerfile' "$test_root/source-runtime/deployment.env"
mkdir "$test_root/new-runtime"
mv "$package/server/Configs/config.Production.json" "$test_root/production-backup.json"
sh "$package/docker/deploy.sh" --runtime-directory "$test_root/new-runtime" --public-host server.local
grep -q '0.0.0.0' "$test_root/new-runtime/Configs/config.Production.json"
mkdir -p "$test_root/partial-runtime/models"
printf '%s\n' 'keep' > "$test_root/partial-runtime/models/existing.txt"
if sh "$package/docker/deploy.sh" --runtime-directory "$test_root/partial-runtime" --public-host server.local > "$test_root/partial-import.log" 2>&1; then fail '覆盖了不完整的资源目录。'; fi
grep -qx keep "$test_root/partial-runtime/models/existing.txt"
mkdir "$test_root/missing-model-runtime"
mv "$package/server/models/vad/silero-native/model.onnx" "$test_root/model-backup.onnx"
if sh "$package/docker/deploy.sh" --runtime-directory "$test_root/missing-model-runtime" --public-host server.local > "$test_root/missing-model.log" 2>&1; then fail '允许了缺少必需模型的部署。'; fi
grep -q 'silero-native/model.onnx' "$test_root/missing-model.log"
printf '部署脚本检查通过；临时文件保留在 %s\n' "$test_root"
