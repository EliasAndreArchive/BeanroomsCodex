# Beanrooms project guide

Beanrooms is a .NET 8 desktop game. The main client uses Silk.NET/GLFW, OpenGL 4.3, OpenAL Soft, Bepu rigidbodies, Vercidium raytraced acoustics, and Photon Realtime. Solo play runs the simulation locally. In an online room, the Photon room master simulates shared physics and monster behavior; guests render snapshots.

## Source map

| Path | Responsibility |
| --- | --- |
| `Beanrooms.Silk/Program.cs` | Startup, window events, and frame loop |
| `Beanrooms.Silk/GameWindow.Session.cs` | World generation and session transitions |
| `Beanrooms.Silk/GameWindow.Player.cs` | Mouse look, movement collision, and player network state |
| `Beanrooms.Silk/GameWindow.Update.cs` | Solo and online simulation updates |
| `Beanrooms.Silk/GameWindow.Rendering.cs` | Render targets, textures, object data, and GI probes |
| `Beanrooms.Silk/GameWindow.Presentation.cs` | Scene draw, post processing, and overlay projection |
| `Beanrooms.Silk/DebugMenu.cs`, `DebugMenu.Navigation.cs`, `DebugMenu.Settings.cs`, `DebugMenu.Controls.cs`, `DebugMenu.Terminal.cs`, `DebugMenu.Network.cs` | Menu state, terminal, pointer controls, settings, and network chat view |
| `Beanrooms.Silk/DebugMenu.Rendering.cs`, `DebugMenu.MenuRendering.cs` | HUD and menu drawing |
| `Beanrooms.Silk/PhotonNetClient.cs` | Photon transport and snapshot buffering |
| `Beanrooms.Silk/PhotonAuthority.cs` | Room master physics, monster, and flicker authority |
| `Beanrooms.Silk/GameAudio.cs`, `PhysicalSounds.cs`, `VercidiumAcoustics.cs` | Sound playback, physics contact audio, and raytraced acoustics |
| `Nullrooms/PhysicsBodies.cs`, `WorldProps.cs`, `CubeSpawner.cs` | Shared Bepu cube simulation, fixed props, command spawning, collisions, and pickup |
| `Nullrooms/NetworkClient.cs`, `NetProtocol.cs` | Solo/online transport contract and messages |
| `Nullrooms/WorldLayout.cs`, `MonsterBrain.cs`, `PlayerMotion.cs`, `LightFlicker.cs` | Shared game behavior and fixed world profiles |
| `Nullrooms/shaders/`, `Nullrooms/assets/` | Rendering source and licensed game assets |
| `Beanrooms.PortChecks/` | Headless checks for retained behavior |

## Ownership rules

- Keep shared game rules in `Nullrooms/`. The client presents state and gathers input; `PhotonAuthority` owns online shared physics and host controls.
- Do not predict held cube movement or throws on guests without reconciliation. `CubeState.HolderId` identifies a current holder.
- Keep world props consistent across solo and Photon authority setup.
- Route cube spawn commands through `CubeSpawner` in solo and Photon authority; validate size, color, amount, and world object count.
- Changes to object encoding must update both `GameWindow.Rendering.cs` and `pathtrace.frag`.
- Settings are for player preferences, including the lighting renderer and image post effects. Developer controls contain monster behavior, detailed GI parameters, and diagnostics. Host-only controls must stay host-only online. Flicker and object physics remain gameplay systems without tuning pages.
- Preserve raytraced audio and original impact/scrape sounds. Retain Photon, Vercidium, and sound asset licenses.
- The sandbox device, wire, weld, copy/place, Ponder, and legacy dedicated-server systems were removed. Avoid reintroducing their controls or protocol fields unintentionally.

## Build

```sh
dotnet build Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```
