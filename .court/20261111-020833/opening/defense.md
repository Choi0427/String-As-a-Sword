# Opening: Defense

1. **It closes exactly the gap the user has.** The case file says that today the designer "cannot say 'at this part of the song, do this specific attack,' and cannot draw new shapes without code." The method answers both: shapes drawn as "8×8 ASCII drawings... New shapes need no code," and a "timeline keyed to note numbers." The song is fixed ("~321 notes", defeat on "note 304"), so note numbers are a stable address. Inspector tuning, the main alternative, leaves both gaps open by definition.

2. **The safety net is reused, not new.** The code "already checks fairness: that a dodge route exists... that it does not invalidate safe cells... no note gap... shorter than one beat." The balance setup already runs "thousands of seeded runs, three bot strategies." The validator puts these behind one menu command that reports "by note ('note 212: no dodge route')." The objection is that replacing the scheduler means "re-measuring." That cost is real, but this method makes re-measuring a single command, not a new project.

3. **Cheapest first, GUI only when earned.** An 8×8 grid of `#` and `.` is small enough to type and read at a glance. The project is "small," and "implementation is done with Claude Code assisting." Plain text is what Claude Code reads and writes most easily, and it diffs cleanly in git. The visual editor is not rejected, only put off "until after some patterns have been written this way." Building the GUI first would spend effort before the user knows which controls they need.

4. **It leaves the open choice open.** A "fully fixed timeline" is "simplest to balance" and fits a rhythm game's "memorize-and-improve" loop. "Section pools" can use the same shape and timeline format later. The method does not lock the user out of variety.

**Concessions:**
- The validator measures fairness and clear rate, not fun. "No human playtest has been done yet."
- The record gives no effort estimates, and it does not say how comfortable the user is editing text by hand.
- The work must start from the battle-6 branch, because "the current working folder is behind."
