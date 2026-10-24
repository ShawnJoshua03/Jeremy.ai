# Laparoscopic cholecystectomy (contract)

Third exercise, modeled on the opening of a standard laparoscopic cholecystectomy as shown in Teleflex's Weck Hem-o-lok / Endo5 procedure animation: patient positioning, Veress needle and insufflation, umbilical port, three working ports, gallbladder retraction, dissection of Calot's triangle to the critical view of safety, clipping the cystic duct and cystic artery, and dividing them between clips. Exercise id: `lap_chole`. Training simulation only; no clinical claims.

## Frame

Board-local metres, as for the other exercises (the board transform is the abdomen's frame): x toward the feet, -x toward the head, y up, **+z is the patient's right**. The right upper quadrant (liver and gallbladder) is `x < 0, z > 0`. Exact anatomy positions are Unity's choice; the backend never needs them because events carry both the actual and the ideal position.

## Steps (in order; the HUD guides each one)

| # | step id | what the trainee does | done when |
|---|---|---|---|
| 1 | `veress` | insert the Veress needle at the umbilicus | needle tip through the wall at the site |
| 2 | `insufflate` | insufflate to target pressure | pressure held in 12–15 mmHg |
| 3 | `port_umbilical` | place the 10–12 mm umbilical trocar, insert the scope | trocar seated |
| 4 | `port_epigastric` | place the 10 mm epigastric trocar under vision | trocar seated |
| 5 | `port_rsc_medial` | 5 mm right subcostal (midclavicular) trocar | trocar seated |
| 6 | `port_rsc_lateral` | 5 mm right subcostal (anterior axillary) trocar | trocar seated |
| 7 | `retract` | grasp the gallbladder fundus and retract it over the liver | fundus held cephalad |
| 8 | `dissect` | clear the peritoneum over Calot's triangle | critical view of safety reached (cystic duct and artery both fully exposed) |
| 9 | `clip_duct` | Hem-o-lok clips on the cystic duct: 2 proximal (CBD side), 1 distal | three clips on the duct |
| 10 | `clip_artery` | clips on the cystic artery: 2 proximal, 1 distal | three clips on the artery |
| 11 | `divide` | cut the duct and the artery between the proximal and distal clips | both divided |

## Events (`POST /events`)

Two new optional fields on every event: `value` (float) and `detail` (string, up to 64 chars). Existing kinds ignore them.

| kind | object_id | source | target | value | detail |
|---|---|---|---|---|---|
| `step_completed` | step id | – | – | seconds since attempt start | – |
| `port_placed` | `veress`, `umbilical`, `epigastric`, `rsc_medial`, `rsc_lateral` | actual skin entry | ideal site | insertion angle error, degrees | – |
| `insufflation` | – | – | – | final pressure, mmHg | – |
| `critical_view` | – | – | – | percent of Calot's triangle peritoneum cleared (0–100) | – |
| `clip_applied` | `cystic_duct`, `cystic_artery`, `cbd` | clip position | – | distance along the structure from its proximal end (the CBD / hepatic artery junction), mm | `ok` or `crossing` (clip not fully across the structure) |
| `structure_cut` | `cystic_duct`, `cystic_artery`, `cbd` | cut position | – | distance along the structure from its proximal end, mm | – |
| `injury` | `bowel`, `liver`, `cbd`, `artery_bleed`, `gallbladder_perforation` | position | – | – | short reason |

The backend decides safety from the sequence: a cut is safe when the same structure has at least two clips with a smaller distance (proximal) and at least one with a larger distance (distal), each at least 2 mm from the cut. A clip or cut on `cbd` is a major error. Cutting a structure without enough clips is a major error. Dividing before `critical_view` was reached is a major error.

## Metrics

`MetricsOut` keeps every field. For `lap_chole`:
- `successful_transfers` = steps completed (of 11), `completion_pct` = completed / 11 × 100
- `drops` = major errors (injuries, CBD clip or cut, unsafe division, division before the critical view)
- `invalid_releases` = minor errors (port more than 15 mm from its site or more than 20 degrees off, pressure outside 12–15 mmHg, clip `crossing`, clips closer than 2 mm to a cut)
- path length and efficiency from samples as today

New optional field `details` (object of string → number, string or bool, null for other exercises), for example `ports_mean_error_mm`, `insufflation_mmhg`, `critical_view_pct`, `safe_divisions`, `major_errors`. The dashboard shows it as a table.

## Unity

`PegTransferRig.SetExercise("lap_chole")`, key `3` in the desktop preview. Steps 1–6 happen from the OR view on the patient's abdomen; from step 3 on, the laparoscope view is the main view (as in the video), with the OR view as an inset.
