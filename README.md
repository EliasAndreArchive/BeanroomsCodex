# Beanrooms

Beanrooms is a low resolution 4:3 desktop game with procedural rooms, path traced and hybrid lighting, Bepu physics, and online multiplayer. The client uses Silk.NET/GLFW, OpenGL 4.3, OpenAL Soft for playback, Vercidium Audio v1.10.1 for raytraced source filtering and EAX room reverb, and Photon Realtime for online rooms. Nearby spatial sounds use Vercidium's grouped room effects; local, UI, and untracked distant sounds use the listener room. The Photon room master runs the authoritative cube, sandbox, and monster simulation and broadcasts snapshots. Photon v5.1.20 and its license are in `Beanrooms.Silk/ThirdParty/PhotonRealtime/`; Vercidium's DLL, API XML, and license are in `Beanrooms.Silk/ThirdParty/VercidiumAudio/`.

Varied concrete footsteps play for the local and online players. All physics objects use the original wood impact and concrete scrape sounds. Each object corner is tracked against the floor, walls, and other objects, so a landing corner clicks while a resting object stays quiet. Online scraping uses authority-measured contact slip speed. Nearby ceiling lights each emit a spatial hum. Sound source licenses are in `Nullrooms/assets/SFX-LICENSES.md`. The Photon room master simulates online object grabs and throws, and clients display its snapshots. Sandbox mode works in solo and is host-controlled online. Players can step onto low pallets; larger cubes remain obstacles.

## Run the client

Install the .NET 8 SDK and run:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game starts fullscreen. Press **F11** for windowed mode, **Tab** for settings, and **Escape** to release the mouse. The title menu offers solo and online play. A GPU driver with OpenGL 4.3 support is required.

To make a standalone Windows build:

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
```

Launch `publish/windows/Beanrooms.exe`. The Windows executable uses the GUI subsystem, so it opens without a command prompt.

For Linux, use `-r linux-x64` and a separate output folder. Run the published `Beanrooms` executable directly; Proton is not required.

Every push to `main` builds self-contained Windows and Linux archives and publishes them in [GitHub Releases](https://github.com/EliasAndreArchive/BeanroomsCodex/releases). Download the archive for your system, extract it, and run `Beanrooms.exe` on Windows or `Beanrooms` on Linux. The release workflow runs only after both builds succeed.

## Online rooms

Choose **JOIN ONLINE**, enter the same room name as the other players, then connect. The client uses the configured Photon App ID; set `BEANROOMS_PHOTON_APP_ID` to override it for another Photon application. `Nullrooms.Server` and `Beanrooms.RenderBridge` remain available for legacy dedicated-server testing.

## Checks

```powershell
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

Add `--photon` to run the live two-client Photon room smoke test.

See [client details](Beanrooms.Silk/README.md) for controls and additional local network checks.
