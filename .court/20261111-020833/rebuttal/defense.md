# Rebuttal: Defense

**1. Rejected attacks get silently rewritten. Weakened.** The method says the validator "reports problems by note ('note 212: no dodge route')", so a rejection shows up as an error while the designer writes the chart, before anything runs. The record does not say what happens at runtime if a bad attack ships anyway. Making the build fail on a rejection is a small fix, not a flaw in the method.

**2. Note numbers are blind. Partly survives.** The song is a fixed, authored chart, so note numbers stay put unless the chart itself changes, and the record mentions no plan to change it. The record also has no map from notes to sections. That is a real gap, but a visual editor or a Notion import would need the same map.

**3. Bots can't judge a fixed chart. Partly survives.** The record shows no bot that memorizes. But "no human playtest" is true under every option, and the bot simulation is the only balance tool this project has today. The method reuses that tool. It does not invent a new kind of evidence.

**4. It throws away the balance work. Does not survive as a reason to reject.** Re-measuring comes with any option that replaces the random scheduler. The only option that avoids it, "keep the random scheduler," cannot place a specific attack at a specific point in the song, which is what the user asked for. A text format can hold a fixed timeline or section pools, so the open choice doesn't block it.

**5. The code is stale. Does not survive.** Every option has to be built on the newest branch. This is about project state, not the method.

**The jury should decide on point 1:** does the validator stop bad attacks while the designer writes the chart, or do they surface only at runtime?
