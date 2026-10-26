#!/usr/bin/env bash
# Builds libmujoco.so for Android arm64-v8a at the version the org.mujoco package was generated from
# (Packages/org.mujoco/package.json), using the NDK, CMake and Ninja that ship with the Unity Android module.
# Output: Assets/Plugins/Android/arm64-v8a/libmujoco.so (the path PlayAab checks before building).
#   bash tools/android/build_mujoco_android.sh [work_dir]
# MuJoCo's configure step downloads its dependencies (lodepng, tinyxml2, qhull, ccd, ...) with FetchContent.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(grep -o '"version": *"[^"]*"' "$ROOT/Packages/org.mujoco/package.json" | grep -o '[0-9][0-9.]*')"
WORK="${1:-$ROOT/Temp/mujoco-android}"
ANDROID="C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/PlaybackEngines/AndroidPlayer"
OUT="$ROOT/Assets/Plugins/Android/arm64-v8a"

mkdir -p "$WORK"
[ -d "$WORK/mujoco" ] || git clone --depth 1 --branch "$VERSION" https://github.com/google-deepmind/mujoco.git "$WORK/mujoco"

# android-28: aligned_alloc is only declared from API 28 (PlayAab sets the same minimum).
# c++_static: no libc++_shared.so to ship or clash with Unity's. max-page-size 16384: Play rejects
# 4 KB-aligned native libraries for apps targeting Android 15+.
cmake -S "$WORK/mujoco" -B "$WORK/build-arm64" -G Ninja \
  -DCMAKE_MAKE_PROGRAM="$ANDROID/SDK/cmake/3.22.1/bin/ninja.exe" \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID/NDK/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-28 -DANDROID_STL=c++_static \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON \
  -DMUJOCO_BUILD_EXAMPLES=OFF -DMUJOCO_BUILD_SIMULATE=OFF -DMUJOCO_BUILD_TESTS=OFF -DMUJOCO_TEST_PYTHON_UTIL=OFF \
  -DCMAKE_SHARED_LINKER_FLAGS="-Wl,-z,max-page-size=16384"
cmake --build "$WORK/build-arm64" --target mujoco

mkdir -p "$OUT"
cp "$(find "$WORK/build-arm64" -name 'libmujoco.so*' -type f | head -1)" "$OUT/libmujoco.so"
"$ANDROID/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-strip.exe" --strip-unneeded "$OUT/libmujoco.so"
echo "built $OUT/libmujoco.so (MuJoCo $VERSION, $(du -h "$OUT/libmujoco.so" | cut -f1))"
