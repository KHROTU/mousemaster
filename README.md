# MouseMaster

finally, a half decent autoclicker.

---

disclaimer: anyone with any competence can probably tell the algorithm is far from perfect, and most acs nowadays just check for the interception dll, so don't use this on hypixel or some shit and come back crying because you got banned, i'm telling you RIGHT NOW that you will probably get banned if you use this software like an idiot.

---

## Features

- gaussian rng: normal-distributed randomness using the box-muller transform variant. [learn more](https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform)
- ornstein-uhlenbeck process: four layered stochastic process with configurable mean-reversion rates to generate temporally correlated noise, preventing jittery uncorrelated intervals. [learn more](https://en.wikipedia.org/wiki/Ornstein%E2%80%93Uhlenbeck_process)
- momentum smoothing: basically makes it so your last click interval and current click interval are somewhat close.
- fatigue: simulates fatigue when clicking for a long time, with periodic recovery events.
- warm up: non-linear gradual speeding up phase for the first few clicks.
- log-normal micro-pauses: random pauses drawn from a log-normal distribution to simulate hesitation or small fuckups. [learn more](https://en.wikipedia.org/wiki/Log-normal_distribution)
- exponential distribution based events: occasional simulated big fuckups via exponential distribution to break overly uniform patterns. [learn more](https://en.wikipedia.org/wiki/Exponential_distribution)
- cursor jitter: 2D gaussian jitter with integrated drift dynamics for micro-movements during clicking. can fight with you when you're trying to move the mouse as well so just bear that in mind or turn it lower/off.
- interception support: optional kernel-level input via the interception driver for lower-level simulation and doesn't add flags like `mouse_event` does. again if the ac just looks for interception SPECIFICALLY then you're extra fucked since you would be banned instantly.
- high precision timing: hybrid `Stopwatch` + `Thread.SpinWait` solution for better interval control.

## Limitations

- not a lot of features. i might add more later but this is just for me, so unless i need a new feature i probably won't add it.
- can't go very fast. the theoretical limit on windows is 1000 cps, and even with overhead and other processes at whatnot it should still be around 800, but since the program is meant to be hard to detect with all the maths and whatnot it can only get to around 400-ish without dropping off massively. stats:

    |target|real|mean|stddev|bitwise dupes|near floor|
    |-|-|-|-|-|-|
    |5|4.8|209.62ms|21.75ms|0|0|
    |10|9.4|106.28ms|12.01ms|0|0|
    |20|18.8|53.25ms|5.43ms|0|0|
    |50|48.8|20.47ms|2.92ms|0|0|
    |400|395.6|2.53ms|0.23ms|0|0|
    |500|460.9|2.17ms|0.16ms|0|45|
    |600|442.7|2.26ms|0.15ms|0|19|

    notice how as the target cps increases the real cps gets capped at around 460.

## Installation

### Base code

you need the .net 8 sdk installed on your machine. if you don't have it just google a tutorial i won't be going over it.

1. clone or download the repo.
2. open a terminal in the project root.
3. build the project:

    ```bash
    dotnet build -c Release
    ```

    or just run it:

    ```bash
    dotnet run
    ```

4. the executable will be in `bin/Release/net8.0-windows/` if you built it.

### Interception driver

if you want to use interception as your input method you need to install that as well.

1. download [interception](https://github.com/oblitum/Interception/releases/tag/v1.0.1) (the zip file).
2. extract the files.
3. open cmd as administrator in the "command line installer" folder, and run this command:

    ```cmd
    install-interception.exe /install
    ```

4. reboot your machine.
5. copy `interception.dll` into the same folder as the executable file.

## Usage

- select your target mouse button (lmb/mmb/rmb).
- choose between "toggle" and "hold" mode. in toggle mode, press your hotkey to start/stop autoclicking, and in hold mode, hold your target mouse button to start/stop autoclicking, and press your hotkey to enable/disable autoclicking (e.g. when you need to hold your key without clicking it, press your hotkey to stop it from doing anything).
- adjust the humanisation settings. the default ones are fine but you can also fine-tune them.

---

## Notes

- be smart with interception. if it's a shitty ac then don't bother, and if it's a competent ac then using it will get you banned immediately or just not boot the game. use it for acs that check for flags but haven't blacklisted interception outright.
- for the last time, use this only for single player games or servers with friends that are just for shits and giggles. don't use this to fuck with other people, and don't bitch about getting banned on multiplayer games when you eventually do.
