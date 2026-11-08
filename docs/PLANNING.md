# PetFam — Desktop Pet Plan

> Status: draft v0.1 (2026-11-07)
> One-line definition: **An OS pet that turns your desktop into its habitat, lives behind your windows, and builds a bond with you.**

---

## 1. Why we are building it

### 1.1 The problem
- Desktop pets have been around for over 20 years (Shimeji, Desktop Goose, Bongo Cat and the like), but most of them remain "a sprite floating on top of the screen and wandering around". After a few days you turn them off.
- Three reasons people turn them off: ① they cover your work ② there is no relationship (they don't know you) ③ there is no space (you don't know where they live)

### 1.2 PetFam's answer
| Existing desktop pets | PetFam |
|---|---|
| Always float on top of windows | Lives **behind** windows by default (the desktop layer) and occasionally peeks out from a window's edge |
| The desktop is just a picture | The desktop is a **habitat**. If there's a house it goes in to rest; if there's a sea it swims |
| No state, or simple hunger | A **bond** builds up; neglect makes it drift away. Even then, it can come back |
| Random animations | It **observes** its owner (watches what you do, follows the cursor with its eyes, plays pranks) |

### 1.3 Differentiators (remember just 3)
1. **Layer life**: presence designed around moving between behind the window ↔ the window edge ↔ in front of the window
2. **Habitat = desktop**: change the wallpaper and the pet's life changes
3. **Bond**: the more you care for it the closer it gets, and the closer it is the more playful and visible it becomes

---

## 2. Target users and usage scenes

### 2.1 Target
- Primary: office workers, developers and students who spend 6+ hours a day at a PC. People who would like a pet but can't have one.
- Secondary: wallpaper and customisation enthusiasts (the Wallpaper Engine user base)
- Tertiary: streamers/VTubers (an on-screen companion)

### 2.2 A day in the life (Persian cat "Momo", Pomeranian "Peanut")
- 09:00 Turn on the PC → Momo, who was sleeping in front of the house on the desktop, stretches. The food bowl is empty, so you feed her (bond +)
- 10:30 Working with a big browser window open → Momo is behind the window, so she's not visible. Now and then just her ears peek out from the window's bottom-right corner
- 11:00 Typing a long document → Momo sits next to the cursor, then lies down on top of the word you just typed (10 seconds). Nudge her gently with the mouse and she moves aside
- 13:00 Away for lunch → Momo plays on the playground slide. When you return you pet her (bond +)
- 14:00 Switch to work virtual desktop 2 → 3 seconds later the dog "Peanut" runs in from the left edge of the screen wagging his tail. Momo is napping in her house on desktop 1 (the tray shows "Momo: sleeping on desktop 1")
- 15:30 Back to desktop 1 → Peanut seems to have been rolling in the yard's mud puddle; his belly is covered in mud. There are muddy paw prints at the house entrance. You drag the towel to clean him (bond +3)
- 18:00 Change desktop 1's wallpaper to "night sea" → Momo sits on the beach, Peanut dives into the waves, comes out with his fur slicked down and shakes himself off, and Momo, disgusted by the flying droplets, moves away
- No food for 3 days → Momo goes into the house and won't come out. No more peeking either. Peanut waits at the window with the empty bowl in his mouth. After you feed them, it takes a day for both to return to normal

---

## 3. Layer structure (designing presence)

The pet lives in three layers. How long it stays in which layer is its "presence", tuned by the bond and settings.

```
[Front yard]  Topmost overlay. Above windows. Only briefly (pranks, reacting to petting, telling you with its body)
[Window]      The edge of the foreground window. Peeking out. Only 20–40% of the body is visible
[Backyard]    The desktop layer. Above the wallpaper, behind windows. The default habitat
```

- **Backyard (default)**: 80%+ of the pet's time. Close all windows or press Win+D and you see its whole life.
- **Window**: when it's curious about what its owner is doing. It pokes out just its face, paws or tail from one of the four sides of the foreground window, watches for a few seconds and goes back in.
- **Front yard**: a few times a day, for a few seconds. The higher the bond, the more often. Always click-through and can be pushed away.

Principle: **by default the pet respects your "right not to see it".** Being visible is an event, and events are earned through the bond.

---

## 4. Pet system

### 4.1 Types and attributes
| Attribute | Habitable areas | Examples | Timing |
|---|---|---|---|
| Land | ground, house, play | cat, dog, hamster | MVP |
| Aquatic | water | goldfish, jellyfish | v0.5. Can't appear without water |
| Amphibious | ground + water | otter, frog, duck | v0.5 |
| Sky | sky + perch | sparrow, parrot | v0.5 |
| Special | anywhere | slime, ghost | Later |

MVP line-up: **two species, cat + dog**. Their contrasting personalities (independent vs. follower) show that "each pet lives differently". Water (the water zone) is validated in the MVP too, with the dog jumping in and the cat avoiding it. Aquatic pets (goldfish) are added in v0.5 along with the fish-tank widget and underwater-only movement.

### 4.1.1 MVP character sheet
| | Persian cat "Momo" (silver tabby) | Pomeranian "Peanut" (orange sable) |
|---|---|---|
| Personality in one line | Independent. Pretends not to care but sees everything | Follower. Follows its owner wherever they go |
| Default layer share | Backyard 80 / Window 15 / Front yard 5 | Backyard 60 / Window 30 / Front yard 10 |
| Signature behaviours | Lying on typed words, perching on window frames, leaving gifts | Fetching the ball, digging, rolling in mud, welcome jump when the owner returns, waiting at the window wagging its tail |
| Virtual desktop travel | Goes wherever she likes. Comes to find you if you stay long | Follows within 1–3 seconds of a desktop switch (bond 15+) |
| Disruptiveness | High (lying down, stepping on things) | Medium (chasing the cursor, sitting next to the caret) |
| Drifting-away staging | Won't come out of the house, ignores clicks | Doesn't follow, sits at the window with its back turned |
| Exclusive interactions | Chin scratch (petting direction) | "Sit / Stay" commands (pet card), throwing the ball, bathing |
| "Family" bond reward | Purring asleep on your lap (next to the caret) | Runs over from any desktop when you call its name |

Goldfish "Bubble", planned for v0.5: 100% backyard, moves only between cells with water, not disruptive, sinks to the bottom of the tank when drifting away, exclusive interaction is sprinkling food, swims after the cursor at the "Family" stage.

### 4.2 Needs
0–100. They decrease over time and are inputs to behaviour selection.

| Need | Decay rate | How to fill it |
|---|---|---|
| Hunger | Based on 2 meals a day | Food (drag and drop) |
| Energy | Proportional to activity | Sleeping in a house or bed (automatic) |
| Fun | Daily | Toys, play zone, pranks on the owner |
| Affection | Daily | Petting, calling its name |
| Curiosity | Rises while the owner is active | Peeking, observing (automatic) |

### 4.3 Bond
0–100. The accumulated value of all relationship actions. Each new stage unlocks more of the behaviour repertoire.

| Stage | Range | Visible changes |
|---|---|---|
| Stranger | 0–14 | Only from afar. Runs away when clicked. No peeking |
| Wary | 15–29 | Comes to eat. Allows brief petting |
| Curious | 30–49 | Starts window peeking. Follows the cursor with its eyes |
| Familiar | 50–69 | Starts pranks (typing disruption level 1). Sleeps near the owner |
| Trusting | 70–89 | More time in the front yard, brings gifts |
| Family | 90–100 | Exclusive animations, responds to its name, introduces the owner to other pets |

**Increases**: food +2, petting +1 per time (daily cap), play +2, logging in at least once a day +1, responding when the pet peeks +1
**Decreases**: 24 hours without contact −1, after 48 hours −3/day, staying at hunger 0 −2/day. **Simply logging in stops the decrease** (no pressure).

**Drifting-away staging** (when the bond drops): stops peeking → won't come out of the house → ignores clicks → (bond at 0 for 7+ days) "gone out for a while" state. Only a note is left on the desktop. Fill the food bowl and it comes back within 1–2 days. **It never dies. No permanent loss.**

### 4.4 Personality parameters
Random at pet creation + changes as you raise it.
- Playfulness (0–1): frequency of disruptive behaviour
- Shyness (0–1): peek duration, flee threshold
- Activity (0–1): amount of movement in the backyard, share of time sleeping
- Homebody (0–1): preference for the house zone
- Tidiness (0–1): how much it avoids getting dirty, how often it grooms itself (see 4.5)

### 4.5 Appearance state: wet, muddy, and traces

What the pet did in its habitat **stays on its body**. After going into water its fur is wet and droopy and drips; after rolling in mud it walks around with mud on its belly and paws. This is the difference between "a picture that moves" and "an animal that lives".

#### 4.5.1 State list
Several states can overlap, and each has an intensity of 0–1.

| State | How it occurs | How it looks | How it goes away | MVP |
|---|---|---|---|---|
| Wet | Entering/leaving a water zone, being outside on a rain-variant wallpaper, splashing | Fur darker, clumped and droopy, slimmer silhouette, dripping water, wet footprints when walking | Air-drying (20 min by day, 40 min at night, 2× faster in a sun zone). Shaking off removes 50% instantly. Towel item removes it instantly | ★ |
| Muddy | Rolling or walking in a mud zone, lying on a ground (dirt) zone while wet | Mud stains on paws, belly and back, spreading with intensity, muddy footprints when walking | Entering water washes it off (→ turns into wet). When dry it becomes "dried dirt" and falls off after a day. Gradually through grooming. Bath item removes it instantly | ★ |
| Sandy | Lying or digging in a sand zone (beach) | Sand grains glinting in the fur, sand scatters when it shakes | Shaking off, water | v0.5 |
| Snowy | Snow zone / snow-variant wallpaper | Snow piles up on its back, melts into wet when it goes into the house | Indoors, time | v0.5 |
| Dust and cobwebs | Long neglect (while the bond is dropping) or hiding in a hide zone for a long time | Fur looks dry and scruffy, a single strand of cobweb | Petting, grooming, water | v0.5 |
| Grass and leaves | Rolling on the grass in a play zone | One or two leaves in the fur | Shaking off, time | v0.5 |

#### 4.5.2 Chains between states (the core of realism)
- Lie on dirt while wet → muddy. Go into water while muddy → clean but wet.
- Go into the house with snow on → it melts and becomes wet. Stay outside in the cold (night or snow variant) for long while wet → shivers and looks for the house.
- When a wet pet goes into the house, a wet mark stays on the house floor for a while. A muddy pet leaves mud marks at the house entrance.
- When a muddy pet peeks from the window, **paw prints are stamped on the window edge** (overlay, disappear after 20 seconds, avoid text areas). A wet pet leaves water marks.
- When a muddy pet does typing disruption level 2 (lying on a word), a mud mark stays there for a few seconds. Only at disruption level "Normal" or above.

#### 4.5.3 Differences by pet
| | Cat | Dog |
|---|---|---|
| Water | Dislikes it. Never goes in by itself. Heads home immediately when it rains. Dips just a front paw at the water's edge and shakes it off | Loves it. Seeks out water when fun is low. Always shakes itself off after coming out, spraying droplets around |
| Mud | Avoids it (high tidiness). If it does get muddy, cleans itself by grooming over the course of an hour | Loves it. Rolls in a mud zone once or twice a day if there is one. Can't clean itself → **the owner has to wash it** (bond +3, once a day) |
| Behaviour when wet | Stays still in a corner licking itself. Dodges attempts to pet it | Runs to its owner and shakes off (if it does this at the window, droplets on the window) |
| Drying | Struggles at first when you drag the towel; from bond 50+ it stays still | Loves the towel. Turns into towel play |

Aquatic pets (v0.5) are always underwater, so they have no appearance states.

#### 4.5.4 Dirt is not a punishment
- Being dirty doesn't lower needs or the bond. Dirt is **a chance for the owner to take care of it** (washing and drying = bond-raising interactions).
- Even if left alone it resolves naturally (mud dries and falls off, cats clean themselves). However, a dog's mud takes a day, so traces remain at the house entrance and the window in the meantime. This is the only "burden", and leaving traces can be turned off in settings.
- Pets with high tidiness rarely get dirty in the first place. The user can see this trait on the pet card.

---

### 4.6 Growth (age and life stages)

An adopted pet arrives as a **2-month-old baby** and grows by the number of **days spent together** with the user. A day counts when the PC is on and the user is active for 10+ minutes. It doesn't grow while the PC is off during a vacation (to keep the feeling of raising it together). A real year is too long, so time is compressed.

| Stage | Days together | Displayed age (cat / dog) |
|---|---|---|
| 🍼 Baby | 0–14 days | 2→6 months old |
| 🌱 Junior | 15–45 days | 6→12 months / 6→18 months |
| 🐾 Adult | 46–365 days | 1 (1.5)→7 years |
| 🍂 Senior | 366 days+ | 7 years+ (one year every two months) |

**Energy and behaviour by age** (adult = 1.0, based on real animals' life patterns)

| | Baby | Junior | Adult | Senior |
|---|---|---|---|---|
| Tiring rate | ×1.6 | ×1.0 | ×1.0 | ×1.3 |
| Recovery while sleeping | ×1.5 | ×1.1 | ×1.0 | ×0.7 |
| Fun need decay | ×1.3 | ×1.5 | ×1.0 | ×0.6 |
| Hunger rate | ×1.4 | ×1.2 | ×1.0 | ×0.9 |
| Walk / run | ×0.8 / ×1.1 | ×1.15 / ×1.1 | ×1.0 | ×0.75 / ×0.75 |
| Jump height | ×0.6 | ×1.2 | ×1.0 | ×0.5 |
| Sleepiness | From energy 70 and below, more often | 60 and below | 60 and below | From 70 and below, more often |

Cats sleep about 20% more than dogs at every stage.

**Appearance**: changes gradually over the days between stages. Babies have a 60% body, a relatively large head, short legs and big eyes. Juniors are leggy. Seniors' fur fades, their muzzle and chest turn white, and they hold their head a little lower. Babies speak clumsily ("Meow" → "Mew", "Woof" → "Yip").

**Events**: on a stage-up, a celebration speech bubble, confetti and a happy hop. On 30 days, 100 days, 1 year… together, it wears a party hat for that day. The info panel shows stage, age, days together, days left until the next stage, and adoption date.

**Principle**: there is no death. Senior is the final stage. Pets that already existed (saves from before the growth feature) start as adults.

## 5. Behaviour system

### 5.1 Structure
Utility AI (needs × context × personality → behaviour score) + a state machine per behaviour.

```
Input: needs, bond, personality, habitat_map, user_context, time
         ↓
Utility evaluation (every 2–5 seconds) → pick the highest-scoring behaviour (hysteresis prevents flip-flopping)
         ↓
Behaviour execution (pathfinding → animation → complete/abort)
```

user_context: whether idle, type of foreground window (browser/editor/game/meeting), whether typing, whether full screen, whether screen sharing, mouse position.

### 5.2 Behaviour catalogue (★ = MVP)

**Daily life (backyard)**
- ★ Walk, run, sit, lie down, sleep, stretch
- ★ Go into the house to rest (house zone) / sleep in a bed
- ★ Use the playground (play zone: going down the slide, chasing a ball)
- ★ Swim in water (water zone. Dog only in the MVP. Aquatic and amphibious pets in v0.5)
- ★ Dog: digging (ground zone), fetching the ball, guarding the front of the house
- ★ Dog: rolling in mud (mud zone), jumping into water
- ★ Shaking off (removes wet and sand, droplet and sand particles), cat grooming (gradually removes mud and dust)
- ★ Leaving wet and muddy footprints (backyard floor, house entrance) — see 4.5
- ★ Travel between monitors and virtual desktops (walks out at the screen edge and enters the neighbouring cell. See 6.4)
- Sitting on top of icons / hiding behind icons / walking on the taskbar (Dock)
- Perching on the top border of an open window (when visible from the behind-window layer)

**Observation (window)**
- ★ Peek: pokes its face out at a random position on one of the four sides of the foreground window (2–6 seconds)
- ★ Following the cursor with its eyes while peeking
- ★ Peeking while muddy or wet leaves paw prints or water marks on the window edge (disappear after 20 seconds, avoid text areas)
- ★ Dog: when the owner switches desktops it follows and settles at the window; when the owner returns from idle, a welcome jump
- Hides in surprise when the window moves
- Waits at the window when the owner has been idle for long

**Pranks (front yard) — bond 50+**
- ★ Typing disruption level 1: sits next to the caret (doesn't cover it)
- ★ Typing disruption level 2: lies on top of the word just typed (up to 10 seconds, moves away when the mouse approaches)
- Typing disruption level 3: walks along with the caret stepping on letters (bond 70+, opt-in in settings)
- Chasing the mouse cursor / catching the cursor
- Bringing gifts (leaves an item somewhere on the desktop)

**Reactions**
- ★ Reaction to petting (purring, tail)
- ★ Reaction to food
- Looks over when a notification toast appears
- Yawns when the battery is low, dozes after midnight

### 5.3 Safeguards for disruptive behaviour (non-negotiable)
Disruptive behaviour is the heart of the fun, but annoy someone once and the app gets uninstalled.

1. **Disruption level setting**: Off / Gentle / Normal / Mischievous. Default "Gentle" (level 1 only).
2. **Automatic do-not-disturb**: full-screen apps, detected screen sharing or recording, presentations, user-specified apps (IDEs, etc.), user-specified time ranges.
3. **Frequency cap**: at most N disruptive actions per hour (1/3/6 by level), each under 10 seconds.
4. **Instant shoo**: move the mouse over the pet and it moves aside. Press Ctrl twice → 30-minute break. "Quiet for 1 hour" in the tray.
5. **Click-through**: the front yard layer always passes input through. Clicks are never blocked by a pet (petting mode is the only exception).
6. **Never reads text content**: uses only the caret position and line height.

### 5.4 Multiple pets
- With 2 or more pets they recognise each other: playing together, squabbling over spots, a cat disgusted by droplets a dog shook off, (v0.5) a cat watching the fish.
- Bond is per pet. No jealousy (no designing for stress).

---

## 6. Habitat system (the desktop)

### 6.1 Habitat Map
A single wallpaper turned into data the pet can understand.

```json
{
  "wallpaper_hash": "…",
  "resolution": [3840, 2160],
  "scale": 1.0,
  "zones": [
    { "type": "ground", "polygon": [], "walkable": true },
    { "type": "water",  "polygon": [], "surface_y": 1400, "depth": "deep" },
    { "type": "house",  "polygon": [], "entrance": [820, 1380], "capacity": 2 },
    { "type": "play",   "polygon": [], "props": ["slide", "swing"] },
    { "type": "perch",  "points": [[2400, 600]] },
    { "type": "hide",   "polygon": [] },
    { "type": "sky",    "polygon": [] },
    { "type": "mud",    "polygon": [], "wet": true },
    { "type": "sand",   "polygon": [] },
    { "type": "sun",    "polygon": [] }
  ],
  "gravity_line": 1500,
  "surface": "dirt",
  "time_variants": { "night": "…hash", "rain": "…hash" }
}
```

Zone types: ground (walk) / water (swim) / house (rest, sleep) / play (play) / perch (sit) / hide (hide) / sky (fly) / blocked (no entry) / mud (mud; rolling in it makes the pet dirty) / sand (sand) / sun (sunny spot, 2× drying) / snow (snow, v0.5).
`surface` is the ground's material (dirt / grass / floor / stone); it decides whether a wet pet lying down turns muddy (dirt only) and whether footprints are left (floor, stone). When the `rain` variant is applied, pets outside get wet and mud zones become active.

### 6.2 Three ways to get a habitat map
| Route | Target | Accuracy | Timing |
|---|---|---|---|
| ① Official habitat wallpapers | Packs provided by PetFam | 100% (metadata included) | MVP |
| ② Manual zone editor | Any user wallpaper | Specified directly by the user | MVP |
| ③ Automatic detection | Any user wallpaper | Target 70–85% | v0.5 |

③ Automatic detection flow: detect wallpaper change → check hash cache → on-device segmentation (water/sky/ground/buildings) + horizon estimation → generate draft map → "Here's what I saw" confirmation popup (editable). On failure, a "flat ground" default map (the whole bottom of the screen is ground).

### 6.3 Fish rules (aquatic pets, v0.5)
In the MVP, water zones are used only as terrain the dog jumps into and the cat avoids. The rules below apply when goldfish are added in v0.5.
- Swims within the wallpaper only when there is a water zone. Never leaves the water zone.
- Without a water zone, waits in a corner of the desktop as a **small fish-tank widget**. Moves in a small space inside the tank and occasionally looks outside (a silent hint to change the wallpaper).
- The moment you switch to a sea or aquarium wallpaper, a "release" scene plays from the tank → it swims freely in the sea.
- Land pets only watch from the edge of a water zone (except amphibians).

### 6.4 Spatial structure: monitors and virtual desktops

The pet's world is not a single desktop but **every screen the user has**.

#### 6.4.1 World model
```
World    = virtual desktops (Windows virtual desktops / macOS Spaces) × monitors
Cell     = one (desktop d, monitor m) slot. One habitat map per cell (that cell's wallpaper)
Pet pos  = (d, m, x, y)
```
- Simulation continues even in cells that aren't visible (a summarised simulation with 2-second ticks, no rendering). When you come back, the pet is wherever its activities in the meantime have taken it.
- By giving each desktop a different wallpaper, the user can **arrange the world like rooms**. E.g. desktop 1 = house + yard, desktop 2 = sea, desktop 3 = work (flat ground). When hungry the pet finds its own way to the room with the bowl; when sleepy, to the room with the house.
- If every desktop has the same wallpaper, as on Windows 10: duplicate rooms of the same map. Even so, the pet is only in one room.

#### 6.4.2 Travel rules (doors and edges)
- **Between monitors**: follows the physical arrangement. Leave by the right edge of the left monitor and you enter at the left edge of the right monitor. If DPI differs, scaling is corrected to keep the pet's physical size.
- **Between virtual desktops**: follows the OS desktop order (Ctrl+Win+←/→, trackpad swipe direction) exactly. Leave by the left edge of the leftmost monitor on desktop 2 → enter at the right edge of the rightmost monitor on desktop 1. The pet's direction of travel matches the user's sense of space in the OS.
- **Staging**: walks out at the edge → 1–3 seconds empty → walks in from the opposite edge of the neighbouring cell. If the user happens to be looking at that desktop, they see it arrive.
- **No-entry cells**: desktops with a full-screen app (including macOS full-screen Spaces), desktops the user has marked as "No pets". The pet only passes through and never appears.
- **Fish (v0.5)**: travel only between cells that have a water zone (assuming an underwater passage). If no cell has water, the fish-tank widget follows the cell currently being viewed.

#### 6.4.3 Travel modes (setting, per pet)
| Mode | Behaviour | Default |
|---|---|---|
| Roam freely | Roams according to needs and curiosity. If the owner stays on one desktop for N+ minutes, comes out of curiosity (the higher the bond, the smaller N) | Cat |
| Follow me | Runs in from the edge within 1–3 seconds of a desktop switch | Dog |
| Stay home | Lives only on a specific desktop that has its habitat | — |

#### 6.4.4 Finding your pet
- The tray icon and pet card show its current location: "Momo: swimming on desktop 2 (sea), monitor 1".
- **Call** from the pet card → a pet with bond 30+ runs across desktops to the current cell (bond 15+ for dogs). With a low bond it ignores you.
- If a desktop is deleted, pets in that cell move to a neighbouring cell. If a monitor is disconnected, to the remaining monitors.

#### 6.4.5 OS elements as terrain
- Desktop icons: props it can sit on or hide behind. Icon positions are read from the OS (macOS needs Finder automation permission; if not granted, treated as having no icons).
- Taskbar/Dock: a floor line. It can walk on top of it. If the Dock auto-hides, only the floor line remains.
- Open windows (seen from the backyard): roofs. It perches on window frames.

### 6.5 Time and weather
- Official wallpapers come as a set of 3: day/evening/night. They switch automatically with system time.
- Weather integration (optional): when it rains the pet goes into the house. After v1.0.

---

## 7. Interaction and UI

### 7.1 Direct interaction
- **Feeding**: drag food from the tray or pet card and drop it near the pet. If you set up a bowl, it eats on its own from whatever you filled.
- **Petting**: press and hold the mouse on the pet (0.5 seconds) → petting mode. Reacts to the drag direction.
- **Toys**: throwing a ball (drag and release), a feather (follows the cursor).
- **Washing and drying**: drag the towel item onto the pet → rub it dry (wet removed instantly, mud halved). Using a bathtub or hose item, or setting the pet down in a water zone, washes off the mud and leaves it wet. Setting a wet pet down in a sun zone dries it quickly. Bond +3 once a day.
- **Wiping footprints**: sweep away mud marks on the floor or house entrance by dragging. Left alone they disappear naturally after 10 minutes.
- **Pick up and carry**: lift by dragging and set down elsewhere (bond 30+). Picking up a muddy pet briefly gets mud on the cursor.
- **Calling its name**: detects its name through the microphone → looks over. After v1.0, opt-in.

### 7.2 UI components
- **Tray icon**: pet status dot (hunger/mood), quick menu (Quiet for 1 hour, Food, Settings)
- **Pet card**: click the pet → small popover. Name, bond stage, need bars, a one-line diary entry for today
- **Habitat editor**: draw zone polygons over the wallpaper. In the preview the pet walks around right away
- **Settings**: disruption level, do-not-disturb apps and times, visibility frequency per layer, per-pet travel mode (Roam freely / Follow me / Stay home), "No pets" per desktop, reduce motion, launch at startup

### 7.3 Notification philosophy
- Almost no OS notifications. The pet tells you **with its body** (when hungry it appears at the window dragging the empty bowl).
- Optional once-a-day summary: "Momo went down the slide 12 times today"

---

## 8. Technical architecture

### 8.1 Modules
```
┌─ Core (platform-independent) ─────────────────────────┐
│ Behaviour engine · needs/bond simulation · habitat map │
│ Pathfinding (polygon navmesh) · save/load              │
└────────────────────────────────────────────────────────┘
┌─ Renderer ────────┐ ┌─ Platform ───────────────────────┐ ┌─ Vision ──────────┐
│ 2D sprites        │ │ Layer window mgmt (back/window/  │ │ Wallpaper change  │
│ Skeletal animation│ │ front)                           │ │ detection         │
│ (Spine etc.)      │ │ Foreground window rect, caret    │ │ Segmentation      │
│                   │ │ Icon and taskbar positions       │ │ (v0.5)            │
│                   │ │ DND signals (full screen/share)  │ │                   │
└───────────────────┘ └──────────────────────────────────┘ └───────────────────┘
```

### 8.1.1 Rendering appearance states (implementing 4.5)
Drawing separate sprites for each state makes art cost explode as number of animations × number of states. Instead we **composite state layers on top of the base sprite**.

```
Final pixel = base sprite
            → Wet: colour grading (darker, less saturated, proportional to intensity) + "wet silhouette" skin (clumped fur, skeletal slot swap)
            → Mud: per-part decal masks (paws, belly, back, face) × intensity → multiply mud texture
            → Sand, snow, grass: scatter point decal sprites within the mask area
            → Particles: water droplets (wet), falling dirt flakes (drying mud), radial burst when shaking
```

- Uses the **slots and skins** of skeletal animation (Spine or our own rig). Wet ears and tail are a skin swap; mud is a decal attachment on per-part slots. Animation clips are shared regardless of state.
- One set of part masks per species (paws, belly, back, face, tail). Mud intensity 0.3 covers only the paws, 0.7 reaches the belly, 1.0 spreads to the back and face.
- Traces (footprints, stains) are stamped into a **decal buffer** above the habitat layer and their alpha fades over time. Window-edge footprints are stamped the same way into the overlay window, but avoid the foreground window's text area (the lines around the caret).
- Art deliverables: per species, a base skin + 1 wet skin + 4–5 mud decals + 2 kinds of footprints. No additional animations.
- Performance: cap of 50 simultaneous particles; one decal buffer per monitor (RGBA, half the wallpaper resolution).

### 8.2 Windows implementation notes
| Task | Approach |
|---|---|
| Rendering behind windows and above the wallpaper | Send message 0x052C to Progman → SetParent the render window onto the spawned WorkerW (the Wallpaper Engine / Lively approach). It draws behind icons, so if it must appear "in front of" icons, read the icon positions and handle occlusion |
| Front yard overlay | Transparent window with WS_EX_LAYERED + WS_EX_TRANSPARENT + WS_EX_TOPMOST + WS_EX_NOACTIVATE. TRANSPARENT is cleared only while the cursor is over a pet sprite (petting) |
| Foreground window edges | GetForegroundWindow + DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS). Account for maximised and snapped states |
| Caret position | 1st choice UI Automation (TextPattern caret bounding rect), 2nd GetGUIThreadInfo(rcCaret), 3rd the IME composition window position. UIA coverage needs checking for Electron and browsers |
| Typing detection | No low-level keyboard hook. GetLastInputInfo + foreground changes + UIA text-changed events are enough (key contents are not collected) |
| Full-screen and sharing detection | SHQueryUserNotificationState (BUSY / D3D_FULL_SCREEN / PRESENTATION_MODE). Screen sharing is supplemented by whether a meeting app is in the foreground |
| Icon positions | LVM_GETITEMPOSITION on the desktop SysListView32, or UIA |
| Wallpaper change detection | IDesktopWallpaper COM (per monitor) + registry watch + file hash. On Windows 11 each virtual desktop can have a different wallpaper, so re-query after a desktop switch |
| Virtual desktops: detecting the current desktop | The public IVirtualDesktopManager API only tells you "is this window on the current desktop" → keep an invisible marker window on each desktop and check with IsWindowOnCurrentVirtualDesktop. Triggered by an EVENT_SYSTEM_FOREGROUND hook + 200ms polling. The private IVirtualDesktopNotification changes GUID per build, so wrap it in a version table and fall back to polling on failure |
| Virtual desktops: list and order | Watch the registry `HKCU\...\Explorer\VirtualDesktops\VirtualDesktopIDs` (an ordered array of GUIDs). Neighbour relationships are determined by this order |
| Virtual desktops: backyard window | A WorkerW child window is visible on every virtual desktop, so there is one window. Only the cell for the current desktop ID is drawn |
| Virtual desktops: window and front yard overlays | Normal windows are only visible on the desktop they were created on → when a switch is detected, move them to the current desktop with IVirtualDesktopManager::MoveWindowToDesktop (public). Pinning via the private Pinned API is an optional optimisation |
| Multiple monitors | EnumDisplayMonitors + PerMonitorV2 DPI. Detect layout changes via WM_DISPLAYCHANGE. One backyard window covering the whole virtual screen, one overlay per monitor |

### 8.3 macOS implementation notes (MVP)
| Task | Approach |
|---|---|
| Rendering behind windows and above the wallpaper | NSWindow level = kCGDesktopIconWindowLevel - 1 (above the wallpaper, below icons). collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]. If it must appear "in front of" icons, read Finder icon positions and handle occlusion |
| Front yard overlay | Transparent window at NSStatusWindowLevel, ignoresMouseEvents = true. false only in petting mode. Without .fullScreenAuxiliary it automatically won't appear in full-screen Spaces (the desired behaviour) |
| Foreground window edges | NSWorkspace.frontmostApplication → AX kAXFocusedWindowAttribute → position and size. Requires Accessibility permission |
| Caret position | AXFocusedUIElement → AXSelectedTextRange → kAXBoundsForRangeParameterizedAttribute. Good in native apps, partial support in Electron and terminals → if not detected, fall back to window-edge behaviour |
| Typing and idle detection | CGEventSource secondsSinceLastEventType (key contents not collected). No key-logging hooks |
| Full-screen and sharing detection | Foreground window frame == screen frame + whether the menu bar is hidden, NSApplication presentationOptions. Screen sharing is supplemented by whether a meeting app is in the foreground |
| Icon positions | Finder scripting (Automation permission, optional). If not granted, treated as having no icons |
| Dock position | Computed from the difference between NSScreen.frame and visibleFrame |
| Wallpaper change detection | Poll NSWorkspace.desktopImageURL(for: screen) (every 2 seconds) + file hash. It can differ per Space, so re-query after a Space switch |
| Space: switch detection | NSWorkspace.activeSpaceDidChangeNotification (public) |
| Space: ID and order | No public API. Use private CGS (CGSGetActiveSpace, CGSCopyManagedDisplaySpaces) wrapped per OS version. Fallback on failure: identify Spaces by wallpaper hash (same wallpaper is treated as the same cell) + order is "order first seen" |
| Space: window handling | Both backyard and overlay are .canJoinAllSpaces, so there is one window. Only the cell for the current Space is drawn |
| Multiple monitors | NSScreen.screens, backingScaleFactor. Detect layout changes via didChangeScreenParametersNotification |
| Permissions | Accessibility (required: caret, foreground), Automation-Finder (optional: icons), Microphone (v1.0 name calling, optional). Onboarding explains why; if not granted, only those features are missing |
| Launch at startup | SMAppService |

### 8.3.1 Platform parity (MVP goal)
| Feature | Windows | macOS | Notes |
|---|---|---|---|
| Backyard (behind windows) rendering | ○ WorkerW | ○ window level | Both below icons. Showing in front of icons: Win ○ / mac △ (permission) |
| Window peek | ○ | ○ | |
| Front yard click-through | ○ | ○ | |
| Caret-based pranks | ○ UIA / △ some Electron | ○ AX / △ Electron, terminals | If not detected, fall back to window-edge behaviour (both) |
| Automatic DND for full screen | ○ | ○ | |
| Virtual desktop travel | ○ (order: registry) | ○ (order: private API, with fallback) | |
| Habitat per desktop | ○ Win11 / △ Win10 (same wallpaper) | ○ | |
| Multiple monitors | ○ | ○ | |
| Icons as terrain | ○ | △ permission required | v0.5 |
| Taskbar/Dock as terrain | ○ | ○ | v0.5 |

### 8.4 Performance budget
- Idle CPU < 1%, active < 3% (per core)
- Memory < 150MB (2 pets, 4K habitat)
- 60fps cap, 30fps for the backyard. Stop rendering when fully covered by windows (check DWM occlusion)
- Battery mode: 50% fewer animation updates

### 8.5 Data
- Local storage: SQLite (pet state, bond log, habitat map cache). Works fully offline.
- Protection against clock tampering: use save timestamps together with a monotonic clock (lightly only).
- Cloud sync: after v1.0, optional.

### 8.6 Recommended stack
Since we ship Windows and macOS together from the MVP, the premise is **one codebase + two thin native layers**.

**Recommendation: C# / .NET 8 + Avalonia (SkiaSharp rendering) + a native layer per platform**
- Core (behaviour engine, habitat, world model, saving) is pure .NET. Platform code sits behind an `IPlatform` interface (window layers, foreground and caret, desktop and monitor enumeration, wallpaper, DND signals).
- Windows layer: call Win32 and COM directly via P/Invoke. References for WorkerW, UIA and DWM (Lively Wallpaper, open source) use the same stack.
- macOS layer: take the Avalonia window's NSWindow handle and set the window level and collectionBehavior. Bundle AX, CGS and NSWorkspace calls into one small Swift dylib and P/Invoke it (easier to maintain than handling the objc runtime directly from C#).
- Rendering is a single Skia path on both. Sprite and skeletal animation code doesn't diverge per platform.
- Risk: the combination of Avalonia transparent windows + window levels must be validated on macOS in a spike within the first week. If it fails, swap only the macOS shell to Swift/AppKit and keep Core in .NET (as a NativeAOT library).

Alternatives:
- Rust (winit + wgpu/skia-safe) + windows-rs/objc2: best for performance and distribution size. But productivity for UI tooling (editor, settings screens) is low, and it depends on team expertise.
- Electron: already has cross-platform window control such as setVisibleOnAllWorkspaces and the 'desktop' window type. But with 3 layers × number of monitors worth of windows it easily exceeds the memory budget (150MB), and WorkerW needs a native add-on.
- Godot 4: the best productivity for animation and physics. Multi-layer windows and Space control have to be implemented ourselves via GDExtension.

---

## 9. Character art pipeline

### 9.1 Approach: skeletal (bone) animation
Characters are drawn as separate parts, attached to a skeleton and animated. One set of drawings; motion is data.

| | Skeletal (chosen) | Frame-by-frame pixel art |
|---|---|---|
| Producing 30 motions | One set of parts + keyframe data | 8–16 frames per motion × 30 = hundreds of hand-drawn frames |
| Appearance states (wet, mud) | Layered on via skin swaps and decal slots | Redraw everything for each state |
| 4K and DPI support | Lossless with vector or high-res parts | Clean only at integer scales |
| Sharing between cat and dog | Reuse the quadruped rig and walk motion | Not shareable |
| Weaknesses | Paper-doll feel; large rotations need parts per angle | Handcrafted feel, retro charm |

Tool: **Spine** (Essential or Pro; mesh deformation and IK require Pro). Hook the spine-csharp runtime into the Skia renderer. A free alternative is DragonBones.

### 9.2 Style guide
- **Size**: base height 112px (1x, 100% DPI). For 2x and 4K, parts are drawn at 448px and scaled down. Feels slightly larger than an icon (96px) and twice the taskbar (48px).
- **Silhouette first**: even scaled down to 48px, it must be clear whether it's a cat or a dog. Distinguish species by ear and tail shapes, and invest in outlines over detail.
- **Line and colour**: thin outline (1.5px @1x) + simple two-step cel shading. Outlines are mandatory so it reads on any wallpaper. Up to 6 base palette colours per species; costumes and skins are handled by palette swaps.
- **Expressions**: eyes and mouth are separate slots. Combinations of 6: neutral, sleepy, surprised, happy, grumpy, disgusted.
- **View**: side 3/4 view as the baseline. For window peeking, keep separate parts for a front-facing face and heads poking out from the top, bottom, left and right.
- **Tone of motion**: moderate exaggeration. Keep a sense of weight by referencing real animal footage (cats: supple and relaxed, dogs: bouncy and hasty).

### 9.3 Workflow and deliverables
| Step | Deliverables | Notes |
|---|---|---|
| 1. Concept | Per-species turnaround (front, side, back), expression sheet, colour palette | AI image generation can be used to explore direction. Final versions are cleaned up by a person |
| 2. Part separation | Layered PSD: per-part pieces, replacement pieces for wet fur (ears, tail, body, belly), head pieces for 4 directions | Hide part seams behind joints. 8px overlap margin |
| 3. Rigging | Spine project: attach parts to the shared quadruped rig, define slots, skins and decal slots | Follow the 9.4 spec |
| 4. Animation | 1st tier essentials → 2nd tier MVP → 3rd tier (9.5) | State for each motion whether it loops and its transition poses |
| 5. Export | Spine JSON + texture atlas (PNG, 2048², premultiplied alpha) | Don't split atlases per skin; use one atlas |
| 6. Integration review | Check silhouettes in the app at 1x, 2x and 4K on light and dark wallpapers, in all 3 layers (backyard, window, front yard) | 9.7 quality criteria |

### 9.4 Rig spec (shared quadruped rig)
Cats and dogs use the same skeleton structure. Only proportions differ.
- **Bones**: root → hip → spine1 → spine2 → neck → head (+ ear_L/R, jaw) / tail tail1–tail4 / front legs shoulder, elbow, paw × 2 / hind legs hip, knee, paw × 2. 24 or fewer in total.
- **IK**: one IK per paw, to plant the feet on the floor line (gravity_line) and slopes.
- **Slots**: determine draw order. Hind legs → tail → body → front legs → head → ears → expression.
- **Skins**: `base`, `wet` (swaps in wet ear, tail, body and belly pieces). Costumes are handled by adding skins.
- **Decal slots**: `mud_paw_FL/FR/BL/BR`, `mud_belly`, `mud_back`, `mud_face`. Empty by default; the runtime turns decal attachments on according to intensity. Sand, snow and grass are different attachments on the same slots.
- **Events**: embed Spine events for the frame a paw touches the ground (stamp footprint), the peak of a shake (emit particles), and peek complete (start gaze tracking).
- **Limits**: 24 bones, 40 slots, 600 mesh vertices per species. Two pets at once must fit within the 1% CPU budget.

### 9.5 Animation list
| Tier | Motions | Sharing | Notes |
|---|---|---|---|
| 1st tier essentials (10) | Idle, walk, run, sit, lie down, sleep, stretch, peek (poke head out), shake off, petting reaction | Walk, run, sit, lie down and sleep share the rig, with per-species timing adjustments | The minimum set for the prototype |
| 2nd tier MVP (14) | Eat, grooming (cat) / scratching (dog), perching on a window frame, playground (slide, chasing a ball), swimming (dog), disgust at water (cat), digging and rolling in mud (dog), sitting next to the caret, lying on a word, following the cursor with its eyes (head and eye rotation only), welcome jump (dog), leaving a gift (cat), entering and leaving the house, exiting and entering at the desktop edge | Mostly species-specific | |
| 3rd tier (high bond, pranks) | Stepping on letters, catching the cursor, purring on your lap, sitting with its back turned (drifting away), "gone out" note, being carried, struggling with the towel / towel play, bathing reaction | | Late v0.1 to v0.5 |

Rule: every motion starts from `idle` and ends in `idle`, or specifies its transition poses. Looping motions must have identical first and last frames.

### 9.6 Peek-only assets
The window layer shows only 20–40% of the body, so full-body motions aren't used.
- Head pieces for 4 directions (coming down from the top, coming up from the bottom, coming out from the left and right) + a pair of front paws + the tail tip.
- The runtime moves the pupils according to the cursor direction (pupils are a separate slot, with a limited range).
- Mud and wet states apply the same way via decal slots on the head and front paw pieces.

### 9.7 Quality criteria (review gates)
- 48px silhouette test: from a black-and-white silhouette alone, you must be able to tell the species and the motion (walk/sit/sleep).
- Wallpaper test: whether outlines hold up on a white background, a black background, and a busy photo.
- Paper-doll test: if part seams show when turning around or rolling over, add parts per angle.
- State test: play all 10 first-tier essentials with the `wet` skin + mud decal 1.0 enabled and check nothing breaks.
- Performance test: two pets playing simultaneously pass the CPU and memory budget (8.4).

### 9.8 Who makes it and schedule
- **Weeks 0–2 (technical spike)**: no art. Validate window layers and desktop travel with a placeholder pet made of ellipses and circles drawn in code.
- **Weeks 2–4**: lock the concept. Set the direction internally with AI generation and sketches, and finalise the turnaround with a contract animator.
- **Weeks 4–9**: one contract animator does part separation → rigging → 10 first-tier essentials → second-tier MVP. Finish the cat first, then duplicate the rig for the dog.
- **Weeks 9–12**: appearance-state skins and decals, peek assets, some of the third tier. Repeated integration reviews.
- Contractor requirements: Spine rigging experience, a portfolio of quadruped animal animation. Work is billed as a fixed price per species for "parts + rigging + 10 first-tier motions", then a unit price per motion.
- AI generation is limited to concept exploration and draft wallpaper (habitat) backgrounds. Character parts and animations are made by people for consistency. If generated images are used in final assets, verify licensing and the provenance of training data.

### 9.9 Asset naming and storage
- Path: under `assets/pets/{species}/`: `rig.json`, `atlas.png`, `atlas.atlas`, `concept/`, `src.psd`.
- Slot and skin names follow the 9.4 spec exactly. Code references them by name, so don't rename them arbitrarily.
- Source PSDs and Spine projects go in Git LFS. Only the exported atlases go in regular Git.

---

## 10. Installation and distribution

### 9.1 Form
- **An installed native desktop app.** Rendering behind windows, reading the wallpaper, other apps' caret positions and detecting virtual desktop switches are impossible from a browser.
- Works fully without a server. Pet state, bond and habitat maps are stored locally. A server only from v1.0 (cloud sync, marketplace).
- How it runs: a windowless background app. Only a tray icon on Windows and a menu bar icon on macOS are visible. Launches automatically at login (on by default, can be turned off in settings).

### 9.2 Packaging per platform
| | Windows | macOS |
|---|---|---|
| Distribution files | Installer .exe (Inno Setup) + portable .zip | .dmg (drag to Applications) |
| Build | .NET self-contained, single executable, x64 + ARM64 | .NET self-contained, Universal (arm64 + x64) .app bundle |
| Expected size | 70–90MB (runtime included) | 90–110MB (Universal) |
| Signing | Code signing certificate (EV recommended: avoids SmartScreen warnings immediately) | Developer ID signing + Apple notarization. Gatekeeper blocks it if not notarized |
| Store | Microsoft Store possible in parallel (MSIX package). However, the Store version may have restrictions on WorkerW etc., so this needs verification | **Mac App Store not possible.** The sandbox doesn't allow the Accessibility API, window level control or private APIs → direct distribution only |
| Install location | Per-user install (no admin rights needed) | /Applications or ~/Applications |
| Minimum OS | Windows 10 21H2 or later (the version where the virtual desktop API is stable). Windows 11 recommended | macOS 13 Ventura or later |

### 9.3 First-run onboarding
1. Choose a pet (one of cat or dog, give it a name)
2. Offer to apply one official habitat wallpaper ("Is it OK to change your desktop background now?" — replaced only if you agree; the original wallpaper is backed up)
3. Permission guidance
   - macOS: request Accessibility permission. Explain why in one line ("For your pet to peek from the edge of a window, it needs to know which window is in front"). Tell the user that even if they decline, backyard life and desktop travel still work and only window peeking and pranks are missing.
   - Windows: no extra permissions.
4. 30-second tutorial: actually shows the pet walking out from behind a window and peeking at the window edge → petting → following you when you switch desktops
5. Choose the disruption level (default "Gentle"), confirm launch at login

### 9.4 Updates
- Automatic in-app updates. Velopack (Squirrel family) on Windows, Sparkle on macOS. Both support delta updates, so each version's download is a few MB.
- Checks for updates once a day. Downloads in the background, applies on next launch (never forces a restart mid-work). No OS notifications apart from the pet telling you with its body, along the lines of "New clothes have arrived".
- Channels: stable / beta. Beta is opt-in in settings.
- Update server: static file hosting is enough (GitHub Releases or a CDN). The feed file (RSS/JSON) is signed.
- Habitat packs and pet packs are distributed as content bundles (.petfam files) separate from app updates. Double-click one and the app imports it.

### 9.5 Uninstalling and data
- On uninstall, pet data (including the bond history) is kept by default. The uninstaller provides a "Also delete pet data" checkbox.
- Data location: Windows `%LOCALAPPDATA%\PetFam`, macOS `~/Library/Application Support/PetFam`. Export/import (.zip) is supported for moving to another PC.
- A wallpaper replaced during onboarding is restored to the original on uninstall.

### 9.6 Build and release pipeline
- GitHub Actions: build, sign and notarize on a Windows runner and a macOS runner respectively → upload artifacts → update the update feed on a release tag.
- Signing keys and notarization credentials are kept only as CI secrets. No locally signed builds.
- Pre-release checks: automatic measurement of the performance budget (8.4), smoke tests on Windows 10/11 and macOS 13/14/15 (window layer creation, desktop switching, caret detection in 3 apps).

### 9.7 Enterprise environments
- Group Policy (Windows) and configuration profiles (macOS) can cap the disruption level, turn off automatic updates, and lock the do-not-disturb app list.
- Silent install options (`/SILENT`, unattended pkg install). After v1.0.

---

## 11. Privacy, safety and ethics
- **Never reads screen content.** Only the caret position, window size and app name. Keystroke contents are not collected. Stated on the first line of the settings screen.
- All processing is local. Automatic habitat detection is on-device first. Cloud detection is opt-in + consent to send the wallpaper image.
- **No guilt-driven design**: no push notifications like "Momo is starving". Pets don't die, and there is no permanent loss. Drifting away is reversible.
- Accessibility: a reduce-motion option. The front yard layer is disabled automatically when a screen reader is in use.
- Work environments: the disruption level cap can be locked by admin policy.

---

## 12. Business model
- Free: 1 pet (cat or dog), 3 official habitats, manual editor
- Paid packs (one-time): pet packs (per species), habitat packs (3–5 per theme, including day/night), costumes
- PetFam Plus (monthly subscription, optional): advanced model for automatic habitat detection, cloud sync, unlimited pets, seasonal limited habitats
- **What we won't do**: selling food, selling bond boosters, ads
- Creator marketplace (after v1.0): revenue sharing for creators of habitat wallpapers and pet skins. The Wallpaper Engine Workshop model.

---

## 13. Roadmap
| Version | Goal | Scope |
|---|---|---|
| v0.1 (MVP, 12–14 weeks) | Prove "it lives behind windows, understands the desktop, and roams across all my screens" | **Windows + macOS together**. Cat + dog. 3 official habitats (house + playground (with a mud puddle) / sea (sandy beach) / room). 3 layers. Virtual desktop and multi-monitor travel (3 travel modes, calling). Basic needs and bond. 2 appearance states (wet, mud) + footprint traces + shaking off and grooming. Feeding, petting, ball throwing, washing, drying. Disruption levels 1–2. Manual zone editor. Tray |
| v0.5 | Habitat automation + more pet variety | Automatic habitat detection. Goldfish (fish-tank widget, underwater-only movement), amphibious and sky pets. 3 pets at once. Icons, taskbar and Dock as terrain. Day/night/rain sets. More appearance states (sand, snow, dust, grass). Personality parameters |
| v1.0 | Ecosystem | Creator marketplace. Cloud sync. Calling by name. Weather |
| Later | The feeling of living together | Social behaviour between pets, pets visiting friends' PCs, mobile sync (check on them from your phone after work) |

MVP validation questions: is it still running after 2 weeks? (D14 retention). Is the top reason for turning it off "disruption" or "boredom"?

---

## 14. Risks and mitigations
| Risk | Impact | Mitigation |
|---|---|---|
| Disruptive behaviour causes annoyance → uninstall | Critical | Default "Gentle", caps, shooing, automatic DND. Compare retention by disruption level in the beta |
| The WorkerW approach breaks with a Windows update | High | Track the Lively and WE communities. On failure, give up on "behind icons" and fall back to a bottom-most normal window |
| Private virtual desktop APIs (Windows notification interface, macOS CGS) break with an OS update | Medium | Wrap private APIs in a version table and always keep a public API fallback (Windows: marker window polling, macOS: identification by wallpaper hash). Even in fallback mode the core of "the pet roams around" works |
| MVP delayed by developing for two platforms at once | High | Finish Core first and develop platform layers in parallel against the `IPlatform` contract. Pull technical risk forward with window layer spikes on both in the first 2 weeks |
| macOS Accessibility permission denied | Medium | Backyard life and virtual desktop travel work without permission. Explain in onboarding that only window peeking and pranks are missing |
| Users "think the pet disappeared" because it's on another desktop | Medium | Always show the current location in the tray, the call feature, and demonstrate desktop travel in the first-run tutorial |
| Apps where the caret position can't be obtained (some Electron apps, games) | Medium | If the caret isn't detected, fall back to window-edge behaviour. Publish a list of supported apps |
| False positives in automatic habitat detection (floor detected as water) | Medium | Confirmation popup, manual correction, "flat ground" fallback when confidence is low |
| Bond decay works as guilt | Reputation | Logging in stops the decay, no notifications, easy to come back |
| Performance (an always-on app) | High | Measure the performance budget in CI, stop rendering when covered |
| Unsigned distribution triggers "unknown developer" warnings → install drop-off | High | Obtain EV code signing (Windows) and Developer ID + notarization (macOS) before launch. Certificates take 2–4 weeks to issue, so start mid-MVP |
| Art production cost (30+ animations per species) | High | Skeletal animation + shared quadruped rig (shared by cat and dog). Focus on the first 2 species. Pipeline and review gates in section 9 |
| Appearance states multiply the number of animations | High | Don't draw per-state sprites; composite layers instead (8.1.1). Limit art to 1 wet skin + a few decals per species |
| Footprints and stains cause discomfort that "my screen got dirty" | Medium | Traces always disappear naturally (10–20 seconds / 10 minutes), avoid text areas, can be turned off in settings. Compare retention with traces on/off in the beta |

---

## 15. Success metrics
- D14 active retention (app running) ≥ 40%
- Average daily interactions (feeding, petting, reacting to pranks) ≥ 3
- Share of users who set the disruption level to "Off" ≤ 15%
- Number of habitat wallpaper changes (a signal that users are "choosing" habitats)
- Share of users reaching bond "Familiar" or higher within 2 weeks

---

## 16. Decisions

### Decided
- Platforms: Windows + macOS launched together (from the MVP)
- 2 MVP pets: cat and dog. Goldfish in v0.5
- Pets move freely across virtual desktops and multiple monitors. Travel mode (Roam freely / Follow me / Stay home) is a per-pet setting

### To be decided
1. Art style: pixel (easy to produce, retro) vs. vector/skeletal (smooth, better for 4K). Given 4K habitats, 2 species × 30+ animations, and appearance-state layers, skeletal is recommended
2. Final pet names (Momo and Peanut proposed)
3. Whether to include disruption level 3 (stepping on letters) in the MVP: validating fun vs. risk
4. How far to allow private OS APIs: whether to use private APIs for virtual desktop order and switch detection (accurate), or only public API fallbacks (safe, but on macOS can't tell apart Spaces with the same wallpaper)
5. Automatic habitat detection: on-device model vs. cloud VLM (cost, privacy)
6. Whether to keep the "empty 1–3 seconds" when travelling between virtual desktops, or appear instantly (sense of space vs. responsiveness)
7. Whether to include window-edge footprints (leaving mud and water marks on the overlay) in the MVP: the realism payoff is big, but so is the backlash that "it dirties my screen". Recommended: include it, off by default
