# Beanrooms Silk.NET client

This is the desktop Beanrooms client using Silk.NET/GLFW for the window and input,
the existing OpenGL 4.3 shaders for rendering, OpenAL Soft for audio, and the
existing Bepu physics and multiplayer implementation.

The game content and tuning are shared with `../Nullrooms`: physics, world layout,
monster logic, movement, flicker, protocol, shaders, and assets are linked from
that project. `DebugMenu.cs` is the same settings/menu logic; only its drawing
surface was replaced so it works without Windows GDI+.

Build and run from the repository root:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game opens fullscreen with a 4:3 image and black side bars.
Use **Tab** for the menu, **F11** for windowed/full screen, **WASD** to move,
and **Escape** to release the mouse. The title menu has solo and online play.
Online play uses Photon Realtime. Choose **JOIN ONLINE**, enter a shared room
name, and connect. The client includes Photon Realtime v5.1.20 and its license
under `ThirdParty/PhotonRealtime/`; `BEANROOMS_PHOTON_APP_ID` can override the
configured Photon App ID for a different deployment.

Sandbox mode is available in solo and online. In solo, toggle it in
**Tab → Session → Sandbox Mode** and press **B** to open the sandbox browser.
Online, only the host can change that setting; guests cannot. Cubes remain
available everywhere with **F** (glow cubes require sandbox mode). Sandbox
devices are physical and can be grabbed like cubes. In sandbox sessions, use
**E** on a button or lever, **G** to toggle the wire tool and click a source
then target, or **K** to toggle the weld tool and click two nearby grabbable
objects. The sandbox browser uses **C** to cycle cube color and **X** to clear
cubes. Hold the left mouse button to carry an object, use the wheel to move
it closer or farther, and hold the right mouse button while moving the mouse
to rotate its target. Fixed path tracing is the default renderer. Realtime GI
is available in the settings menu when a faster dynamic diffuse radiance cache
is useful. Post FX includes optional current-frame spatial denoising for path
tracing. A random roomwide flicker can leave the lights blacked out until the
next flicker restores them.

Build standalone packages:

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
```

The GPU and its driver must support OpenGL 4.3. The Linux build is native;
Proton is not needed. OpenAL Soft is included with both standalone builds.

The headless checks live in `../Beanrooms.PortChecks`. Run them with:

```powershell
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

Run `--photon` to open two temporary Photon clients and verify room joining,
authoritative snapshots, and host-only sandbox mode. The TCP server checks are
kept for the legacy dedicated-server project.
