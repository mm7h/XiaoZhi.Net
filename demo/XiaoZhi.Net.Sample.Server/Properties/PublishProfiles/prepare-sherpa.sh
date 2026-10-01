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
    osx-*)
        command -v install_name_tool >/dev/null 2>&1 || {
            echo 'The Xcode command line tools are required to prepare macOS native libraries.' >&2
            exit 1
        }
        cp "$source_dir/libsherpa-onnx-c-api.dylib" "$output_dir/libsherpa-onnx-c-api.dylib"
        cp "$source_dir/libonnxruntime.dylib" "$output_dir/sherpa/libsherpa-onnxruntime.dylib"
        chmod u+w "$output_dir/libsherpa-onnx-c-api.dylib" "$output_dir/sherpa/libsherpa-onnxruntime.dylib"
        old_name=$(otool -L "$output_dir/libsherpa-onnx-c-api.dylib" | awk '/libonnxruntime.*dylib/ {print $1; exit}')
        test -n "$old_name" || { echo 'Sherpa ONNX Runtime dependency was not found.' >&2; exit 1; }
        install_name_tool -id '@loader_path/libsherpa-onnxruntime.dylib' "$output_dir/sherpa/libsherpa-onnxruntime.dylib"
        install_name_tool -change "$old_name" '@loader_path/sherpa/libsherpa-onnxruntime.dylib' "$output_dir/libsherpa-onnx-c-api.dylib"
        codesign --force --sign - "$output_dir/sherpa/libsherpa-onnxruntime.dylib"
        codesign --force --sign - "$output_dir/libsherpa-onnx-c-api.dylib"
        ;;
    *) echo "Unsupported Sherpa preparation runtime: $rid" >&2; exit 1 ;;
esac
