# ntsc-rs integration

Upstream: https://github.com/ntsc-rs/ntsc-rs
Pinned revision: 8cfc51014092a6dc1da5eb0217bb9ae6516b124e
Core crate: ntsc-rs 0.1.2. MIT selected from MIT OR ISC OR Apache-2.0.
Copyright valadaptive. See LICENSE-MIT; source notices are retained.

The complete upstream processing core is vendored under native/core. A small
Rust C ABI wrapper in native/src/lib.rs processes RGBA float images and exports
the upstream setting descriptors, defaults, enumeration values and ranges.
No upstream GUI/GStreamer/plugin-host code is included.

The game performs tone mapping and gamma conversion, reads back the native
320x240 / 480x360 / 640x480 image, runs ntsc-rs, then uploads its result before
nearest-neighbor enlargement and Bayer dithering/posterization. Menus and HUD
are composited afterward. Both conversion boundaries flip the GL bottom-up
buffer so head switching and field processing use the correct orientation.
Intermediate CPU and YIQ buffers are reused. A 60 Hz effect clock keeps noise
animation independent of the rendering frame rate. OFF and zero strength skip
readback and processing. POST FX OFF also bypasses NTSC.

All 60 upstream effect settings are exposed in grouped settings pages. The
master enable and effect-strength blend are additional Beanrooms controls.
The defaults are the pinned upstream defaults, blended at 0.65 strength.
Playback/export/application preferences from the upstream GUI are not image
effect parameters and are not relevant to the game.

## Native rebuild

The checked-in beanrooms_ntsc.dll is the release Windows x64 library. Normal
.NET builds need no Rust installation. To rebuild it, install stable Rust with
the Windows MSVC toolchain and run `./build-native.ps1` from this directory.
Cargo.lock pins all Rust dependencies. The vendored core source is unchanged;
its standalone Cargo.toml removes workspace-only development/bench settings.

On Linux x64, run `cargo build --release --locked` in native and copy
native/target/release/libbeanrooms_ntsc.so beside the game executable. Rebuild
the library for any other OS/architecture before running the game there.

Run the native, settings, and rendering checks through Beanrooms.PortChecks.
