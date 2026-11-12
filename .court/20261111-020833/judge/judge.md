## Charges that stuck

1. Nobody has shown that the existing fairness checks (dodge route, safe cells, one-beat gap) work on hand-drawn 8×8 shapes and attacks placed at chosen notes. If they don't, the "reused" validator becomes a rewrite. 11 jurors.
2. Note numbers mean nothing until they are mapped to song sections (chorus, fast part, finale). The record has no such map. 4 jurors.
3. Bot clear rates are the only balance evidence, no human has played the game, and the balance targets must be re-measured once the random scheduler is gone. 3 jurors (a tie with the stale-branch problem).

## What the defense got right

1. It is the only option on the table that fixes both of the user's stated gaps: placing a chosen attack at a chosen point in the song, and drawing new shapes without writing C#.
2. It spends little first. The expensive clickable editor waits until real patterns show it is needed, and any editor or import would need the same parser and validator underneath anyway.

## Conditions for acquittal

1. On the battle-6 branch, run the current fairness checks on one hand-drawn mask at one authored note. Settle whether they can be reused or must be rewritten.
2. Make a rejected attack a hard error reported by note while the chart is written. Never let the game quietly defer or skip it.
3. Hand-write five patterns and a note-to-section map, then say honestly whether editing text grids feels workable.

## The sentence

The method may go ahead, on probation. Before writing a parser or a single full timeline, spend a day on the battle-6 branch proving that the fairness code accepts a drawn shape at a fixed note. If it does, build the format and the validator. If it doesn't, cost the rewrite and compare it with an editor or a Notion import before you commit.
