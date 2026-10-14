# Beanrooms desktop client

The client uses Silk.NET/GLFW, OpenGL 4.3, Bepu rigidbodies, OpenAL Soft, Vercidium raytraced acoustics, and Photon Realtime. Shared world, movement, monster, flicker, protocol, shaders, and assets are in `../Nullrooms`.

Run from the repository root:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game starts fullscreen with a white command menu over a separate scene of white bean avatars moving under Bepu physics, with no active player or gameplay world. Click an option or type its name and press **Enter**. The current menu path appears at the input prompt, and previous commands remain in scrollback. **Page Up/Page Down** or the mouse wheel scrolls every menu item on long pages. **F11** toggles fullscreen. **Tab** opens the terminal over a blurred room while playing; **Escape** goes back in menus or releases the mouse in play. Every typed and printed character triggers its own terminal sound.

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

Choose solo or join an online room before movement and interaction are enabled. Leaving either kind of session returns to the main menu, stops the world feed, and releases the player. The pause terminal accepts **SETTINGS**, **DEVELOPER**, **HELP**, **LEAVE**, and **RESUME**. Named settings show `<CURRENT VALUE>` and cycle when clicked; numeric settings have draggable sliders. Commands use setting names, for example `LIGHTING`, `SET EXPOSURE 2.25`, **RESET**, and **BACK**. Long settings pages print every item into the scrollable transcript. **CLS** or **CLEAR** removes old transcript lines and redraws the current menu. Typed input appears in warm yellow and errors in red. Display contains the lighting renderer. Image contains post effects, dither, and posterization; HUD and player profile have their own pages. Developer contains detailed rendering, monster behavior, and diagnostics. Monster controls are host-only in Photon rooms. Flicker remains part of the world without a settings page.

From any in-game terminal page, type `SPAWN CUBE 1 WHITE 2` or `SPAWN GLOWCUBE 1 BLUE 2`: type, size in meters, color, then amount. Sizes range from 0.25 to 2.5, and a command can request up to eight cubes. Solo and online rooms both use the same spawn rules. The held-object hint displays **RMB TO ROTATE**.

Choose **ONLINE** on the title screen, then type `CONNECT BEAN` for a room named BEAN. `BEANROOMS_PHOTON_APP_ID` can override the included Photon App ID. The room master simulates online cubes and monster behavior; guests display its snapshots.

Open the pause terminal and choose **NETWORK**. Outside an online room it shows **OFFLINE**. In an online room it lists the room code, player count, host, and chat. Type directly at its prompt to chat; names use each player's game color. Type **PLAYERS** to see the full roster, then **BACK** to return to chat. Use the mouse wheel or Page Up/Page Down to browse older chat messages.

Two fixed cubes appear in each room before players spawn more. Pallets, the sandbox browser, devices, cables, welding, copy/place tools, and Ponder guide have been removed.

## Builds and checks

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

Windows builds launch without a command prompt. Linux builds are native. GPU drivers must support OpenGL 4.3. Photon and Vercidium licenses remain in their respective `ThirdParty` directories; sound licenses are in `../Nullrooms/assets/SFX-LICENSES.md`.
