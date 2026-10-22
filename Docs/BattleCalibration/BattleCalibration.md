# Battle calibration

Combat battle-1; 344 notes; 87 BPM; player HP 3.
Analysis only intervals: [0,100), [100,148), [148,216), [216,344). Actual runs use HP and pickup gates.
Calibration seeds 0..999; independent holdout 100000..100999; three deterministic observation-only strategies.
CollectFirst alone supplies P10 and the 90% target; all three strategies are reported. P10 uses nearest rank ceil(0.10*N), including deaths and unfinished phases. HP = floor(P10*0.875); a candidate below one stops calibration. No second miss deduction. The 5% input omission experiment is separate.
Final HP: **62, 12, 24, 94**. Calibration target (CollectFirst actual clear >=90%): **met**.
Executed 12000 battles in 1022.95 seconds including compressed log export; invariant failures: **0**.

| Set | Strategy | Runs | Clears | Deaths | Timeouts | Analysis goals unfinished | Leftover P10 / P50 / P90 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| analysis | CollectFirst | 1000 | 0.0 % | 0 | 1000 | 0 | 0 / 0 / 0 |
| analysis | AttackHold | 1000 | 0.0 % | 0 | 1000 | 1000 | 0 / 0 / 0 |
| analysis | MovementUse | 1000 | 0.0 % | 0 | 1000 | 999 | 0 / 0 / 0 |
| calibration | CollectFirst | 1000 | 100.0 % | 0 | 0 | 0 | 44 / 57 / 69 |
| calibration | AttackHold | 1000 | 100.0 % | 0 | 0 | 0 | 52 / 63 / 74 |
| calibration | MovementUse | 1000 | 100.0 % | 0 | 0 | 0 | 44 / 58 / 71 |
| holdout | CollectFirst | 1000 | 100.0 % | 0 | 0 | 0 | 44 / 55 / 69 |
| holdout | AttackHold | 1000 | 100.0 % | 0 | 0 | 0 | 51 / 63 / 75 |
| holdout | MovementUse | 1000 | 100.0 % | 0 | 0 | 0 | 44 / 57 / 70 |
| miss5 | CollectFirst | 1000 | 96.2 % | 38 | 0 | 0 | 27 / 42 / 59 |
| miss5 | AttackHold | 1000 | 97.2 % | 28 | 0 | 1 | 37 / 50 / 63 |
| miss5 | MovementUse | 1000 | 96.4 % | 36 | 0 | 1 | 27 / 43 / 60 |

## Iterations

- Initial phase 1: P10=71, HP=62 (floor(P10 * .875)).
- Initial phase 2: P10=14, HP=12 (floor(P10 * .875)).
- Initial phase 3: P10=28, HP=24 (floor(P10 * .875)).
- Initial phase 4: P10=108, HP=94 (floor(P10 * .875)).
- Iteration 1: HP=[62,12,24,94], actual clear=100.00 %.

## Data and interpretation

phase-samples.csv retains every seed and phase, including failures and unvisited phases (zero opportunities; transition -1). phase-distributions.csv gives min/P10/P50/P90/max/mean for move, guard, effective attack, other, attack opportunity, transition note and leftover notes. Transition notes are zero-based; -1 means no transition.
Every execution, including each tuning iteration, has a compressed JSONL file in logs/. Its set label, seed and strategy identify the run. run-diagnostics.csv preserves event reason counts and invariant failures for every execution; phase-samples.csv also carries capacity/defer/fallback counts and simulation time. Holdout results never tune HP.
CollectFirst prioritizes a disclosed pickup trip. AttackHold guards in place against imminent damage and attacks until the remaining attack/collection/spawn budget becomes urgent. MovementUse takes cheap disclosed pickup trips during ordinary play, uses safe evasive movement to collect, and uses the remaining budget for longer trips. All choices use copied current observations and fixed tie ordering; no strategy reads the future random stream.
HP-only reductions apply only to timeout runs with the current pickup goal complete and current HP remaining, at most 10% of that phase per iteration, at most eight actual runs. Death, route and pickup failures are retained and cannot be cured by arbitrary HP cuts. Integer HP below ten is not reduced when one HP would exceed ten percent.
