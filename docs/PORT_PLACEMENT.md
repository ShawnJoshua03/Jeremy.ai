# Port placement (contract)

Exercise id `port_placement`. The trainee places the four ports of a standard laparoscopic cholecystectomy on the abdomen, and nothing after that. Dissection, clipping and division are not modelled. Training simulation only; no clinical claims.

Scoring code: `backend/scoring.py` (port placement section) and `backend/assessment.py` (`PORT_PLACEMENT_THRESHOLDS`). Tests: `backend/tests/test_port_placement.py` and `test_port_placement_api.py`.

## Frame

Board-local metres, as in [LAP_CHOLE.md](LAP_CHOLE.md): x toward the feet, y up, **+z is the patient's right**. The umbilicus is a convenient origin but not required. Use real adult scale, because the vessel-zone rule is in absolute millimetres.

## Ports

Standard four-port layout with the surgeon on the patient's left (source: standard technique, not verified against a guideline):

| object_id | Role | Typical site |
|---|---|---|
| `umbilical` | camera | umbilicus |
| `epigastric` | surgeon's right hand | just below the xiphoid, right of the falciform ligament |
| `rsc_medial` | surgeon's left hand | right subcostal, midclavicular line |
| `rsc_lateral` | assistant (fundus retraction) | right subcostal, anterior axillary line |

## Events (`POST /events`)

Only `port_placed` and `landmark` are accepted for this exercise.

| kind | object_id | source | target | value | detail |
|---|---|---|---|---|---|
| `landmark` | `target` | Calot's triangle (inside the abdomen) | – | – | – |
| `landmark` | `midline` | any point on the midline (only z is used) | – | – | – |
| `port_placed` | a port id above | actual skin entry | ideal site | insertion angle error, degrees | `under_view` or `blind` |

- Send both landmarks once, at the start. Without `target`, the angles are not measured. Without `midline`, the vessel zone is not checked.
- A retry sends another `port_placed` for the same port. The last one counts for geometry, and the first one counts for sequence.
- Send `detail: "blind"` when a working port was inserted with the camera not looking at its entry point.
- Unity's ideal sites must sit outside 40–80 mm from the midline, or a perfect placement will be flagged.

## What is scored

| Check | Rule | Severity | Source |
|---|---|---|---|
| Site accuracy | distance from ideal site and insertion-angle error | score; minor finding over 15 mm or 20° | prototype |
| Triangulation | angle at the target between the two working ports (`epigastric`, `rsc_medial`) is 45–75° | score | Hanna 1997 |
| Azimuth symmetry | camera-to-left and camera-to-right angles at the target are equal | score | Hanna 1997 (equal azimuth); 30° tolerance is prototype |
| Epigastric vessel zone | skin entry 40–80 mm from the midline | major | Saber 2004 |
| Camera first | a working port before the umbilical port | major | standard technique, **not verified** |
| Under view | a working port with `detail: "blind"` | major | standard technique, **not verified** |
| Spacing | two ports closer than 50 mm | minor | prototype |

```
score = 100 * (0.35*site_accuracy + 0.20*completion + 0.20*triangulation + 0.10*azimuth_symmetry
               + completion*(0.10*minor_factor + 0.05*time_factor))
```

Any major error caps the score at 40. Formula details are in the comment block in `scoring.py`.

`MetricsOut` mapping: `successful_transfers` = ports placed (of 4), `drops` = major errors, `invalid_releases` = minor errors. `details` holds the per-port site errors (`<port>_error_mm`), `manipulation_angle_deg`, `azimuth_left_deg`, `azimuth_right_deg`, `azimuth_difference_deg`, `elevation_left_deg`, `elevation_right_deg` (reported only), `min_port_spacing_mm` and the error counts. Cost prices only major errors.

## Sources

| Source | What it supports | Checked |
|---|---|---|
| Hanna GB, Shimi S, Cuschieri A. Optimal port locations for endoscopic intracorporeal knotting. *Surg Endosc* 1997;11(4):397–401 | manipulation angle 45–75° with equal azimuth angles; 60° best; elevation should rise with manipulation angle | abstract only ([Dundee record](https://discovery.dundee.ac.uk/en/publications/optimal-port-locations-for-endoscopic-intracorporeal-knotting/)) |
| Hanna GB, Shimi S, Cuschieri A. Influence of direction of view, target-to-endoscope distance and manipulation angle on endoscopic knot tying. *Br J Surg* 1997;84(10):1460–1464 | 60° manipulation angle beat 30° and 90°; 75–150 mm lens-to-target distance (not used: the scope depth is not simulated) | abstract only |
| Saber AA et al. Safety zones for anterior abdominal wall entry during laparoscopy: a CT scan mapping of epigastric vessels. *Ann Surg* 2004;239(2):182–185 | epigastric vessels usually 4–8 cm from the midline (means 4.4–7.5 cm by level) | full text ([PMC1356210](https://pmc.ncbi.nlm.nih.gov/articles/PMC1356210/)) |
| Supe AN, Kulkarni GV, Supe PA. Ergonomics in laparoscopic surgery. *J Minim Access Surg* 2010;6(2):31–36 | background on triangulation and port placement | not opened (paywalled) |

## Limits

- Hanna's numbers come from knot tying in a box trainer, not from cholecystectomy. Real lap chole layouts rarely reach the "baseball diamond" ideal of about 30° each side. That is why only azimuth *symmetry* is scored, measured as 3D angles at the target.
- Saber's band varies with age and BMI (wider in older, heavier patients). One fixed band is used.
- The camera-first and under-view rules are standard teaching, but no guideline text has been opened for them yet.
- Every other number is a prototype choice and is not clinically validated.
- The API tests have not been run (no fastapi or sqlalchemy in the environment where this was written).
