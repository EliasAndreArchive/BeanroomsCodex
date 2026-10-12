# Beanrooms desktop client

The client uses Silk.NET/GLFW, OpenGL 4.3, Bepu rigidbodies, OpenAL Soft, Vercidium raytraced acoustics, and Photon Realtime. Shared world, movement, monster, flicker, protocol, shaders, and assets are in `../Nullrooms`.

Run from the repository root:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game starts fullscreen with a low resolution 4:3 image. **F11** toggles fullscreen. **Tab** opens the pause menu; **Escape** goes back in menus or releases the mouse in play.

## Controls

| Input | Action |
| --- | --- |
| WASD | Move |
| Shift | Sprint |
| C or Ctrl | Crouch |
| Mouse | Look |
| Left mouse | Grab, carry, and release a cube |
| Right mouse + mouse movement while carrying | Rotate the held object |
| Mouse wheel while carrying | Change hold distance |
| Tab | Pause menu |
| F11 | Fullscreen |

The pause menu has separate **Settings** and **Developer** entries. Settings contains display, image (post effects, dither, and posterization), camera, HUD, and profile/online controls. Developer contains detailed rendering, color mixing, movement and physics tuning, monster behavior, flicker, and diagnostic overlays. Posterization uses a fixed 16-step palette. Monster and flicker controls are host-only in Photon rooms. Movement tuning remains disabled online.

Choose **JOIN ONLINE** on the title screen, enter the same Photon room name as the other players, then connect. `BEANROOMS_PHOTON_APP_ID` can override the included Photon App ID. The room master simulates online cubes and monster behavior; guests display its snapshots.

Physics pickup, throwing, and collision remain in both solo and online play. Pallets no longer spawn. Spawnable sandbox cubes, the sandbox browser, devices, cables, welding, copy/place tools, and Ponder guide have been removed.

## Builds and checks

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

Windows builds launch without a command prompt. Linux builds are native. GPU drivers must support OpenGL 4.3. Photon and Vercidium licenses remain in their respective `ThirdParty` directories; sound licenses are in `../Nullrooms/assets/SFX-LICENSES.md`.
