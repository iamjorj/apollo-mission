# Apollo Mission

**NASA Space Apps Challenge 2026.** Plan and fly your own Apollo 11 mission, from a launch date you pick to a landing at the point you pick on the Moon, using real NASA data at every step.

Made in Unity 2022.3 (URP). It runs on a laptop with integrated graphics.

## What you do

1. **Plan the launch.** On a real 3D Earth and Moon (NASA JPL Horizons positions, any day from Nov 2026 to Dec 2028), you pick:
   - **the date**
   - **the landing site:** any point on the Moon
   - **Eagle's landing fuel**
   - **life support**
   - **the route:** free return or hybrid

   The weather at the pad is a real NASA record for Kennedy.
2. **GO / NO-GO poll.** Each controller (flight path, rocket, life support, weather) checks your plan against the real rules: the Sun's angle at the site, the Earth above the horizon, the Saturn V's performance, thunderstorms and ground winds.
3. **Launch** from LC-39A in the Apollo 11 command module (the Smithsonian's laser scan of Columbia's interior), with the real 1969 air-to-ground radio.
4. **To the Moon:** Earth orbit, the TLI burn, docking with Eagle (you fly it), the 3-day coast with a course correction you burn by hand, and the lunar orbit burn behind the Moon.
5. **The landing** at your site. The real terrain comes from NASA LOLA heights, with craters and boulders generated where that data is too coarse. You fly the last 150 m by hand, with the real "60 seconds", "30 seconds" and "The Eagle has landed" radio.

## Real problems, caused by your choices

| Problem | Real flight | What triggers it |
|---|---|---|
| Lightning strike, SCE to AUX | Apollo 12 | launching into rain clouds (real weather) |
| Center engine out | Apollo 13 | climbs on days without rain clouds |
| Docking latches won't catch | Apollo 14 | random |
| 1202 / 1201 program alarms | Apollo 11 | leaving the rendezvous radar tracking |
| Landing radar won't lock | Apollo 14 | more likely over rough highlands |
| Fake abort signal (solder ball) | Apollo 14 | random; you type the real fix `V21 N01 E 1010 E 107 E` |
| Blinding dust | Apollo 12 | dusty mare sites, hovering low |

- **A wrong choice is played out** (abort, crash, tip-over), explained in plain words, then rewound to the moment you chose.
- **No scores.** The game shows what happened and why.

## NASA data used

| Data | Used for |
|---|---|
| NASA JPL Horizons ephemerides | Earth, Moon and Sun positions |
| NASA SVS CGI Moon Kit: LROC colour map and LOLA heights | the Moon and the landing terrain |
| Blue Marble, Black Marble, GIBS VIIRS clouds | the Earth |
| NASA POWER daily weather for Kennedy, 1981-2025 | the launch-day weather |
| Apollo Flight Journal and Apollo Lunar Surface Journal audio and transcripts | the radio |
| Boeing AS-506 postflight trajectory | the climb |
| NASA 3D Resources | the Lunar Module model |
| NASA SVS Deep Star Maps 2020 | the stars |
| Smithsonian 3D | the Apollo 11 command module scan |

Four source images are larger than GitHub's 100 MB limit and are not in this repository:
- `lroc_color_poles_16k.tif`: [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720)
- `starmap_2020_8k.exr` and `milkyway_2020_8k.exr`: [NASA SVS Deep Star Maps 2020](https://svs.gsfc.nasa.gov/4851)
- `kloofendal_48d_partly_cloudy_puresky_8k.hdr`: Poly Haven, no longer used

Download them into the paths listed in `.gitignore` before opening the project in Unity.

## Play

Download the Windows build from **Releases**, unzip it, and run `ApolloMission.exe`.

**Keys**

| Key | Action |
|---|---|
| Mouse drag | look around |
| C | outside view |
| U | float in zero g |
| W / S | fly the docking and the landing |
| 1–4 | answer a problem card |
| SPACE | continue |
