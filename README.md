# ClearSteps

A free, unofficial helper that sets up **ArtIsWar's free MW4 audio tune** (Warzone, DMZ) on a Windows 10/11 PC without the long manual guide.

**Download:** [ClearSteps.exe](https://github.com/etffmc-crypto/ClearSteps/raw/main/dist/ClearSteps.exe)

Windows will warn that the app is from an unknown publisher (it is not code-signed). Click **More info**, then **Run anyway**, then **Yes** on the administrator prompt.

## What it does

1. **Set up this PC** - runs ArtIsWar's own installer from his site (VB-CABLE, Equalizer APO, Voicemeeter, the tune library and sound engine), then does the Windows sound steps his guide has you do by hand: attaches Equalizer APO to *Art Tune +*, sets it to 16 channel / 24 bit / 48000 Hz, disables the old *Art Tune* device.
2. **Pick your headset** - configures Voicemeeter so the tuned game sound and your normal sound both reach your headset.
3. **Choose your tune** - style, own-gun volume and head model from drop-downs. Changes apply instantly.
4. **Headphone EQ** - walks you through the two-minute squig.link step and imports the result.
5. **In-game settings** - the list of MW4 audio settings the tune needs.

It checks for updates every time it opens and updates itself with one click.

## Credit

The tune, the sound engine and the installer are the work of **ArtIsWar** and are given away free:
[video guide](https://www.youtube.com/watch?v=9GRrP2UsRp4) - [ArtTuneDB](https://github.com/ArtIsWar/ArtTuneDB) - [Discord](https://discord.gg/artiswar).

ClearSteps bundles none of his files. It downloads everything from his official sources at install time and only automates the clicking. If you want the polished, supported experience (profiles, no Voicemeeter, clean stream audio), buy his app [ArtTuneKit](https://artiswar.io/arttunekit).

ClearSteps is not affiliated with or endorsed by ArtIsWar. It is free and must stay free (the tune library is licensed CC BY-NC-SA 4.0).

## Undo

Run ArtIsWar's installer (`irm artiswar.io/tools/ArtTuneGuided | iex` in Windows PowerShell as administrator) and choose **[4] Uninstall everything**, then set your normal speakers or headset as the default device in Windows sound settings.

## Building

`build.ps1` compiles `dist\ClearSteps.exe` with the C# compiler that ships in Windows. No SDK needed.

## Releasing an update

1. Change the code or `catalog.json` (tune list, descriptions, in-game settings).
2. Tune list only: raise `catalogVersion` in `catalog.json` and push. Apps pick it up on next launch; no new exe.
3. App change: raise `AppVersion` in `src/Setup.cs`, run `build.ps1`, set the same version in `update.json`, push.

## Licence

GPL-3.0. See `LICENSE`.
