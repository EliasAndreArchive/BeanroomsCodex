# Beanrooms

Beanrooms is a low resolution 4:3 desktop game with procedural rooms, path traced and hybrid lighting, Bepu physics, and online multiplayer. The client uses Silk.NET/GLFW, OpenGL 4.3, OpenAL Soft for playback, Vercidium Audio v1.10.1 for raytraced source filtering and EAX room reverb, and Photon Realtime for online rooms. Nearby spatial sounds use Vercidium's grouped room effects; local, UI, and untracked distant sounds use the listener room. The Photon room master runs authoritative cube and monster simulation and broadcasts snapshots. Photon v5.1.20 and its license are in `Beanrooms.Silk/ThirdParty/PhotonRealtime/`; Vercidium's DLL, API XML, and license are in `Beanrooms.Silk/ThirdParty/VercidiumAudio/`.

The 256 m procedural map has mostly low ceilings, scattered rooms roughly 1.5 m higher, and rarer tall rooms with both full-height and shorter walls. Rare pit rooms contain either one large opening or a grid of smaller holes. Falling into a pit returns the player to a safe floor position. Ceiling height, lamps, collisions, sound occlusion, and the monster's route use the same generated layout.

Varied concrete footsteps play for the local and online players. Physics objects use the original wood impact and concrete scrape sounds. Each object corner is tracked against the floor, walls, and other objects, so a landing corner clicks while a resting object stays quiet. Online scraping uses authority-measured contact slip speed. Nearby ceiling lights each emit a spatial hum. Sound source licenses are in `Nullrooms/assets/SFX-LICENSES.md`. The Photon room master simulates online object grabs and throws, and clients display its snapshots. Each room has two fixed cubes; pallets have been removed.

## Run the client

Install the .NET 8 SDK and run:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game starts with a white command menu over a blurred scene of white bean avatars moving under Bepu physics, without a controllable player or gameplay world. Click an option or type its name and press **Enter**. Named settings show `<CURRENT VALUE>` and cycle when clicked; numeric settings have draggable sliders. Long pages print every item and scroll with **Page Up/Page Down** or the mouse wheel. The current path stays on the input line, and earlier prompts remain in scrollback. During play, **Tab** opens the terminal over a blurred room, **Escape** goes back, and **F11** toggles fullscreen. Type `SPAWN CUBE 1 WHITE 2` or `SPAWN GLOWCUBE 1 BLUE 2` from any in-game terminal page to create objects in solo or online play. `CLS` clears the transcript and redraws the current menu. Typed commands appear in warm yellow and errors in red. Every typed and printed character triggers its own terminal sound. A GPU driver with OpenGL 4.3 support is required.

To make a standalone Windows build:

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
```

Launch `publish/windows/Beanrooms.exe`. The Windows executable uses the GUI subsystem, so it opens without a command prompt.

For Linux, use `-r linux-x64` and a separate output folder. Run the published `Beanrooms` executable directly; Proton is not required.

Every push to `main` builds self-contained Windows and Linux archives and publishes them in [GitHub Releases](https://github.com/EliasAndreArchive/BeanroomsCodex/releases). Download the archive for your system, extract it, and run `Beanrooms.exe` on Windows or `Beanrooms` on Linux. The release workflow runs only after both builds succeed.

## Online rooms

Choose **ONLINE**, then type `CONNECT BEAN` to connect to the room named BEAN. While in an online room, open the pause terminal and choose **NETWORK** to see the room code, player count, host, and chat. Type a message at the network prompt and press **Enter**. Type **PLAYERS** to see everyone in the room, then **BACK** to return to chat. Player names use their in-game colors; the mouse wheel or Page Up/Page Down browses older messages. The client uses the configured Photon App ID; set `BEANROOMS_PHOTON_APP_ID` to override it for another Photon application. The old dedicated server and Render bridge have been removed.

## Checks

```powershell
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

See [client details](Beanrooms.Silk/README.md) for controls and menu details.
