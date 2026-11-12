# Case

"보스의 패턴을 내가 직접 디자인하고 싶어. 어떤 방법이 효율적일까" — on trial: the proposed method, "write the boss's attacks as a text-file 'boss chart' (8×8 shapes drawn in ASCII + a timeline keyed to song note numbers), checked by an automatic validator that reuses the game's existing fairness code and bot simulation; build a clickable Unity editor window only later, if needed."

(The user asked an open question. The method above is the answer proposed to them earlier in the conversation; the trial is whether it is the efficient way for this user to design boss patterns by hand. Other options are listed below so they can be weighed against it.)

## The game

- "String As a Sword": a Unity (6000.x, URP) rhythm + board-combat prototype. The player stands on an 8×8 chessboard facing a boss (a king). Each song note is one action turn: attack, move one cell, or guard.
- The song is fixed: an authored rhythm chart (RCM chart "검을현", ~321 notes, 87 BPM, 40 hold notes). The battle is designed to end with the song; the boss's defeat lands on a fixed late note (note 304) in the newest build.
- The boss attacks by "telegraphing" a set of board cells some notes in advance (warning 1–5 notes); when the impact note arrives, any player on those cells is hit unless they guarded (or, for "dodge-only" attacks, unless they left the area).
- The user is the game's designer and writes design docs (GDD v0.7 in Notion). No human playtest has been done yet; balance has been tuned with bot simulations (thousands of seeded runs, three bot strategies, with and without 5% random input misses), reporting clear rate, attack/move/guard action mix (target about 3:3:1) and notes left over.

## How boss patterns work today (from the code)

- Attack shapes are a fixed C# enum of 9 kinds: Row, Column, LockedCell (player's cell), TopPressure (part of the top row), Cross (player's row+column), FullBoard, UpperHalf, Checker, Ring. A new shape requires editing C# code.
- Each pattern entry (PatternDefinition) has: name, kind, response (Either / GuardOnly / DodgeOnly), warning notes, hit count (1–8), hit spacing (1–4 notes), phase mask (which of 4 boss phases), selection weight. These are editable in the Unity Inspector on a ScriptableObject (StageDefinition asset).
- A random scheduler is on: it picks the next pattern by weight (never the same twice in a row if avoidable), picks a random interval between attacks from weights, and randomizes which row/column a Row/Column attack hits.
- Before publishing an attack, the code already checks fairness: that a dodge route exists within the warning (DodgeActions), that it does not invalidate safe cells already shown, and (newest build) that no note gap from the warning to the last hit is shorter than one beat (0.69 s). Rejected attacks are deferred or skipped.
- So today the designer can tune weights, hit counts and spacing, but cannot say "at this part of the song, do this specific attack," and cannot draw new shapes without code.

## The proposed method, in detail

1. Shapes as 8×8 ASCII drawings in a text file (`#` = danger, `.` = safe); player-relative shapes mark an anchor point. New shapes need no code.
2. A timeline keyed to note numbers, e.g. "note 120: X-shape, warning 3, dodge-only" / "notes 124–130: ring combo", so attacks can follow the song's structure (chorus, fast sections, finale).
3. A validator (Unity menu command) that runs the existing fairness checks on every authored attack and reports problems by note ("note 212: no dodge route"), plus the bot simulation for clear rate and action mix.
4. A clickable Unity editor window for painting cells only later, after some patterns have been written this way.

## Choice the user still has to make

- Fully fixed timeline: the same attacks every run (memorize-and-improve, like a rhythm game chart), simplest to balance.
- Section pools: hand-made attack groups per song section, one picked at random each run; more variety, more cases to validate.

## Other options that exist

- Keep the random scheduler and only tune Inspector values / add enum shapes in code.
- Build a visual Unity editor window first (click cells on a grid, place attacks on a note timeline).
- Author patterns in an external tool (spreadsheet, Figma, or the user's Notion GDD) and import them.

## Constraints and context

- Small project, the user is the designer; implementation is done with Claude Code assisting.
- Existing balance targets (clear rate ≥90% no-miss, ≥80% with 5% misses, action mix ~3:3:1, leftover-note median ~40) were tuned against the random scheduler; replacing it means re-measuring.
- The current working folder is behind: the newest build (battle-6, with finale and rebalanced boss HP) is on another branch.
