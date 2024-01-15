# In Place Upgrade

## Description

Allows the player to replace like-for like machines and belts.

Uses the games 'Variant' key bind (Default is V on PC) to swap variants if the machine the player is looking at is the same type of machine the are holding of a different revision.

I.e If you have a conveyor run from the early game with MK1 conveyors, you can replace them with Mk2 conveyors by holding 'v' with MK2 conveyors selected for placement.

## Known Issues:
- Assemblers in-place upgrade is currently disabled.
- Holding a different key before pressing the variant key can cause the variant key to be missed. (Trying to press variant while already moving can cause nothing to happen)
- When converting long straight segments, the rebuild can sometimes fail if you dont have twice as many belts as the longest single run. As a work around the script now expects you to have twice as many belts as you are trying to convert.

## ToDo
Future ToDo: When selecting a conveyor belt, ugprade the entire line instead of a single belt at a time.

## Youtube Demo

###0.0.3
https://youtu.be/HiNNA-ikqC8

###0.0.2
https://youtu.be/QmpWDCLM488

### Manual Install Instructions

Note: If you are playing on Gamepass, your game version is likely behind the steam version. Please check the version compatibility chart below.

Your game folder is likely in one of these places:  
    • Steam: (A-Z):/steam/steamapps/common/Techtonica  
    • Gamepass: (A-Z):/XboxGames/Techtonica/Content  
    • Gamepass: Could also be in C:/Program Data/WindowsApps  

1. Download BepInEx v5.4.21 from [here](https://github.com/BepInEx/BepInEx/releases)
2. Follow the installation instructions [here](https://docs.bepinex.dev/articles/user_guide/installation/index.html)
3. Extract the contents of the .zip file for this mod.
4. Drag the "BepInEx" folder into your game folder.
5. Change config options. 

## compatibility
| Mod Version | Game Version |
| --- | --- |
| 0.1.0 | 0.2.0 |
| 0.0.9 | 0.2.0 |
| 0.0.8 | 0.2.0 |
| 0.0.7 | 0.2.0 |
| 0.0.6 | 0.1.2a |
| 0.0.5 | 0.1.2a |
| 0.0.4 | 0.1.2a |
| 0.0.3 | 0.1.2a |
| 0.0.2 | 0.1.1d/0.1.2a |

## Changelog
### V0.1.0
- Move source to github and setup links.
- Seperate the traversal limit in aggressive mode from the replace limit.
	- larger traversal limits allows the tool to scan larger belt networks and find unupgraded belts further from the upgrade point.
	- Traversal limit applies to aggressive search only.
- Add SameBeltOverride
	- Lets a player start an upgrade if they are holding a conveyor.
		- If looking at the same type of belt, the mod will look for belts that need to be upgraded and are connected.
### V0.0.9
- Minor bug fixes.
	- Nothing functional, just reducing error logging.
    - Minor performance boost when running with logging enabled.
### V0.0.8
- Removed requirement for HideGameManagerObject
	- Special thanks to Ripπ for assistance with this. The dependance on game configuration had cause some users to be unable to use the mod, so this release is dedicated to getting rid of that requirement.
- Added configuration to allow logging to be enabled/disabled
- Added configuration for how many belts can be replaced at once. Be careful with this, belt traversal can cause stutters if set too high.

### V0.0.7

- 0.2.0 Patch update
  - Because Some things needed their capitalisation changed apparently. Thanks Devs!

### V0.0.6

- Performance improvements during belt traversal.
- Addition of new Aggressive belt search mode (Disabled by default)
  - Searching does not restrict by connections and travels outwards from belt looked at
  - Disabled by default, Enabled in settings.

### V0.0.5

Tweak to the ui feedback.

### V0.0.4

Patch release. 
- An issue was found where you need more belts in your inventory than you are replacing. A temporary work around is now in place, you need 2 times as many belts to guarantee all of the belt is rebuilt.
- Added UI feedback!

### V0.0.3

Belt Traversal!

The mod will now traverse across belt lines to find any other belts to upgrade!
- The search is limited to 40 blocks before and after the selected belt ( Performance/Unity reasons )

### V0.0.2

Initial release.

&nbsp;
## Disclaimer

Note: NEW Games must be loaded, saved, and reloaded for mods to take effect. Existing saves will auto-apply mods. 
Please be sure to backup your saves before using mods: AppData\LocalLow\Fire Hose Games\Techtonica 
USE AT YOUR OWN RISK! Techtonica Devs do not provide support for Mods, and cannot recover saves damaged by mod usage.

Some assets may come from Techtonica or from the website created and owned by Fire Hose Games, who hold the copyright of Techtonica. All trademarks and registered trademarks present in any images are proprietary to Fire Hose Games.
