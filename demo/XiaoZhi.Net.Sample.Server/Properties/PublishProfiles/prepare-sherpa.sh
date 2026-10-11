#!/bin/sh
set -eu

rid=$1
source_dir=$2
output_dir=$3
mkdir -p "$output_dir/sherpa"

# Modify copies only; never patch files in the NuGet cache.
case "$rid" in
    linux-*)
        command -v patchelf >/dev/null 2>&1 || {
            echo 'patchelf is required to isolate the Sherpa ONNX Runtime. Install it before publishing.' >&2
            exit 1
        }
        cp "$source_dir/libsherpa-onnx-c-api.so" "$output_dir/libsherpa-onnx-c-api.so"
        cp "$source_dir/libonnxruntime.so" "$output_dir/sherpa/libsherpa-onnxruntime.so"
        chmod u+w "$output_dir/libsherpa-onnx-c-api.so" "$output_dir/sherpa/libsherpa-onnxruntime.so"
        patchelf --set-soname libsherpa-onnxruntime.so "$output_dir/sherpa/libsherpa-onnxruntime.so"
        patchelf --replace-needed libonnxruntime.so libsherpa-onnxruntime.so "$output_dir/libsherpa-onnx-c-api.so"
        patchelf --set-rpath '$ORIGIN/sherpa' "$output_dir/libsherpa-onnx-c-api.so"
        ;;
    *) echo "Unsupported Sherpa preparation runtime: $rid" >&2; exit 1 ;;
esac
