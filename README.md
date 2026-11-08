# PetFam

**Your pet family, living on your desktop.**

PetFam is a desktop pet for Windows. Adopt a puppy or a kitten, and it moves in *behind your windows*: it walks around on your wallpaper, treats the scenery as its home, naps in a little house you build for it, peeks out from the edge of the window you are working in, and grows up with you day by day.

PetFam is not a screensaver and does not get in your way. Your pets live on the desktop layer, under every app window, and only come to the front when you reach for them.

> Status: early development (technical spike). Windows 10 21H2+ / Windows 11. A macOS version is planned.

## Why PetFam

- **They live in your wallpaper.** PetFam reads your wallpaper and finds the floor, water, sunny spots, shade, mud and even houses in the picture. Dogs jump into a lake and shake off, cats sunbathe and avoid puddles. Change the wallpaper and they adapt within seconds.
- **Real breeds with real personalities.** 13 dogs and 12 cats, each with its own look, temperament, habits and way of talking: a Shiba that screams when picked up, a Ragdoll that goes limp in your arms, a Corgi who only talks about food, a Husky who talks back, a Siamese who never stops chatting.
- **They grow up with you.** Every pet arrives as a two-month-old baby and grows a day older on each day you spend together, through junior and adult to a dignified senior. Body, energy and behaviour change with age.
- **A home of their own.** Build and decorate houses, decide whose house is whose, and watch your pets walk in through the door to sleep or to get out of the rain.
- **The real weather.** When it rains where you live, it rains on your desktop. Cats run for cover, dogs play in the puddles, snow piles up on the roofs.
- **All of your desktops.** Pets travel between Windows virtual desktops, follow you when you switch, or stay home.
- **Gentle by design.** No chores, no guilt trips: pets never die, and simply being at your computer keeps your bond from fading. Full-screen apps and presentations put them on quiet mode.

## Features

### Life on the desktop
- Pets live between the wallpaper and your windows; when a window covers them, they are simply behind it.
- Hover over a pet and it comes to the front, looks at you and can be petted, picked up and carried.
- **Window peek:** a curious pet walks behind the window you are using and pokes its head out of an edge to look at you.
- Cats hunt a moving mouse pointer: crouch, stalk, wiggle, pounce.
- Pets play with each other and remember who their friends are and who is boss.

### Breeds and personas
| Dogs | | Cats | |
|---|---|---|---|
| Pomeranian | Fluffy chatterbox | Korean Shorthair | Tabby street boss |
| Maltese | Clingy little princess | Russian Blue | Shy silver gentleman |
| Toy Poodle | Clever trickster | British Shorthair | Laid-back teddy bear |
| Bichon Frisé | Smiling cotton candy | Scottish Fold | Folded-ear sweetheart |
| Shiba Inu | Proud and stubborn | Persian | Elegant aristocrat |
| Welsh Corgi | Short-legged foodie boss | Ragdoll | Melts when held |
| Jindo | Loyal Korean native | Munchkin | Short-legged troublemaker |
| Golden Retriever | Sunny best friend | Siamese | Chatty shadow |
| French Bulldog | Snoring comedian | Maine Coon | Gentle giant |
| Dachshund | Brave little sausage | Bengal | Mini leopard |
| Siberian Husky | Dramatic snow runner | Sphynx | Warm velvet cuddler |
| Chihuahua | Tiny bodyguard | Abyssinian | Curious explorer |
| Beagle | Nose-first adventurer | | |

Each breed has its own coat and build, temperament (playfulness, shyness, energy, homebody), habits (appetite, sleep, jumping, love of water and snow, dislike of rain), how quickly it warms up to you, and its own voice for greetings, hunger, sleepiness, petting, weather and more.

### Care and bond
- Sims-style pie menu on every pet: Pet, Chin scratch, Hug, Play ball, Give a treat, Feed, Towel dry, Sit!, Rest. Actions queue up and the pet does them in order.
- Needs (hunger, energy, fun, affection, cleanliness), mood and a bond that unlocks more as you get closer.
- Throw the ball yourself: grab it with the mouse and flick it; dogs fetch it back.
- Pets get wet and muddy, shake off and dry in the sun.

### Growing up
Baby (days 0–14) → Junior (15–45) → Adult (46–365) → Senior (366+). A day counts when you have been at the computer for ten minutes, so pets do not age while the computer is off. Milestones are celebrated with confetti and a party hat.

### Houses
Build up to three houses with a choice of shapes, colours and decorations. Give each one an owner or make it everybody's. Pets walk in through the door, lie in the doorway looking out, and come back out when they wake.

### Weather
Optional real weather from [Open-Meteo](https://open-meteo.com) for a city you choose: rain with splashes, thunder and lightning, snow that settles and melts, cloud and fog. Pets react in character.

### Day and night
Day and night follow your computer's clock and the real sunrise and sunset where you are (worked out on your computer for the weather city, no network needed). Dusk and dawn glow warm, nights turn the desktop deep blue with stars on clear nights, the house windows light up, and pets get sleepy after dark.

### Virtual desktops
Each desktop has its own habitat from its own wallpaper. Pets walk off one edge and arrive at the other, follow you when you switch desktops (Follow me), wander on their own (Roam freely) or Stay home, and can be called over from the tray.

### Languages
English by default, Korean optional (tray → Language / 언어). Everything switches, including breed personas and what the pets say.

## Getting started

Requirements: Windows 10 21H2 or later (Windows 11 recommended) and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet run --project src/PetFam.App
```

PetFam has no main window: it lives in the system tray. On the first run it opens the adoption window: choose dogs or cats, pick a breed, name your baby and adopt it.

### Using PetFam
- **Tray menu:** My pets (where they are, call them over, travel mode), Weather, put out a food bowl, build a house, add a pet, language, quit.
- **Mouse:** hover a pet to bring it forward; click for its menu; press and rub to pet; drag to carry. Click a house to decorate it, drag it to move it.
- **Panel:** clicking a pet opens its panel with mood, needs, bond, age and its to-do queue.

## Privacy
- PetFam reads only window positions, the cursor position and idle time, never what is inside your windows.
- The wallpaper is analysed on your computer. Nothing is uploaded.
- The only network request is the optional weather lookup, which sends the coordinates of the city you picked (no IP lookup, no location permission). Turn it off in the tray.

## For developers

```
dotnet build PetFam.sln
dotnet test
```

```
src/PetFam.Core              Platform-independent: pets, breeds, needs, bond, growth, habitat, behaviour, weather, saving
src/PetFam.Platform.Windows  Win32: WorkerW desktop layer, click-through, foreground window, virtual desktops
src/PetFam.Platform.MacOS    Stub (planned)
src/PetFam.App               Avalonia app: windows, tray, 3D (SkSL SDF) and vector renderers, weather service
tests/PetFam.Core.Tests      xUnit
docs/PLANNING.md             Product and technical plan
```

- Data: `%LOCALAPPDATA%\PetFam\save.json` (saved every minute and on exit), log `petfam.log`, snapshots and debug sheets. Data from the old `ZooZoo` folder is copied over on the first run.
- Frame timing: if the animation ever stutters, `petfam.log` records a `Hitch:` line with what took the time.

### Diagnostic environment variables

| Variable | Effect |
|---|---|
| `PETFAM_NO_PEEK=1` | Disables peeking (the bond stage gate applies) |
| `PETFAM_CALM_FPS=<n>` | Idle frame rate (default 12) |
| `PETFAM_PAINT_WALLPAPER=1` | Paints the wallpaper directly into the backyard window (fallback when transparency doesn't work) |
| `PETFAM_NO_ATTACH=1` | Doesn't attach to WorkerW; uses a normal window |
| `PETFAM_RENDER=software\|angle\|wgl` | Forces the Avalonia render backend |
| `PETFAM_COMP=winui\|dcomp\|redirect` | Forces the Avalonia composition mode |
| `PETFAM_UI_RENDER=1` | Renders on the UI thread |
| `PETFAM_SNAPSHOT=1` | Automatically saves a backyard snapshot after startup (`PETFAM_SNAPSHOT_DELAY=<seconds>`, default 3) |
| `PETFAM_STYLE=fur` | Draws in the fur-texture 3D style instead of cartoon |
| `PETFAM_WALLPAPER=<image path>` | Leaves the real wallpaper alone and lays this image in the backyard to test habitat detection and behaviour |
| `PETFAM_ADOPT=cat,dog,shiba,…` | Brings in more pets at startup: `cat`, `dog` or a breed id (`pomeranian` `maltese` `poodle` `bichon` `shiba` `corgi` `jindo` `korean-shorthair` `russian-blue` `british-shorthair` `scottish-fold` `persian` `ragdoll` `munchkin` `golden-retriever` `french-bulldog` `dachshund` `siberian-husky` `chihuahua` `beagle` `siamese` `maine-coon` `bengal` `sphynx` `abyssinian`); saved |
| `PETFAM_SHOW_HABITAT=1` | Shows the detected floor in green and the horizon as a yellow line on screen (also toggled from the tray's "(Test) Show habitat detection") |
| `PETFAM_BOND=<0~100>` | Sets every pet's bond to this value at startup (also adjustable from the tray menu "(Test) Bond") |
| `PETFAM_DEMO_MENU=menu\|queue` | Automatically opens the first pet's pie menu 4 seconds after startup (queue: pre-fills the queue with a few actions) |
| `PETFAM_POSE_SHEET=1` | Saves `pose-sheet.png` with every pose drawn on one sheet at startup (for checking the rig) |
| `PETFAM_TURN_SHEET=cat\|dog` | Saves `turn-sheet.png` with poses × 10 facing directions on one sheet (for checking side and back views) |
| `PETFAM_MOTION_SHEET=cat\|dog` | Saves `motion-sheet.png` with frames of turn around, start, walk, stop, sit, and get up and run, driven at 30fps and drawn in sequence (for checking transitions) |
| `PETFAM_DEMO=ball` | Starts ball play with the first dog 3 seconds after launch (for checking throwing) |
| `PETFAM_DEMO=house` | Opens the build-a-new-house window 3 seconds after launch |
| `PETFAM_DEMO=travel` | 3 seconds after launch the first pet walks out to the neighbouring desktop, and is called back 14 seconds later |
| `PETFAM_BREED_SHEET=1` | Saves `breed-sheet.png` with every breed drawn as an adult, two directions each |
| `PETFAM_ONBOARDING=1` / `cat` | Opens the adoption window at startup (`cat`: Cat tab) |
| `PETFAM_TIME=HH:mm` | Pretends it is that time of day (also in the tray: Weather → (Test) Try a time of day) |
| `PETFAM_LANG=en` / `ko` | Starts in that language (overrides the saved choice) |
| `PETFAM_WEATHER=<weather>` | Instead of real weather: `clear`, `cloudy`, `fog`, `drizzle`, `rain`, `heavyrain`, `thunder`, `snow`, `heavysnow` (with snow already piled up) |
| `PETFAM_DOOR_SHEET=1` | Saves `door-sheet.png` showing the stages of entering a house through the door, sleeping inside, and window-edge peeking (cat and dog, four directions) |
| `PETFAM_DEMO=grow` | 4 seconds after launch, advances (the selected pet, or all pets if none) to the next growth stage |
| `PETFAM_AGE_DAYS=<days>` | Sets every pet's days together to this value at startup |
| `PETFAM_GROWTH_SHEET=1` | Saves `growth-sheet.png` with the cat and dog drawn at ages from 0 to 500 days |
| `PETFAM_SAVE=<file>` | Uses a different save file (if the file doesn't exist, starts from first-run onboarding; your real pets are untouched) |

## Roadmap
- macOS version (Spaces, menu bar)
- Pets moving between multiple monitors
- A habitat editor for fixing what the wallpaper analysis gets wrong
- Official habitat wallpapers and more breeds

## Documentation
- [Product and technical plan](docs/PLANNING.md)
