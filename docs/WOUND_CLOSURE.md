# Wound closure scenario (contract)

Second exercise next to peg transfer. Exercise id: `wound_closure`. Peg transfer stays `peg_transfer` and must keep working unchanged.

## Board-local geometry (metres, same frame as PegBoard: x right, y up, z away from the trainee)

- Skin pad: centred at the board origin, top surface at `y = 0.012`. Size 0.20 (x) by 0.18 (z).
- Incision: straight line along z at `x = 0`, from `z = -0.065` to `z = +0.065`. Gap width 0.004.
- Five suture sites, index `i = 0..4`, `site_{i+1}`:
  - `z_i = -0.05 + 0.025 * i`
  - entry point `(-0.008, 0.012, z_i)` (left of the incision)
  - exit point `(+0.008, 0.012, z_i)` (right of the incision)
- Wound-edge exclusion zone: `|x| < 0.003` within the incision z range. Tip contact there is tissue damage.
- Tolerances: a bite within **4 mm** of its point is good; up to **8 mm** still counts as placed; beyond 8 mm is a miss.

These constants live once in Unity (`WoundPad`) and once in the backend scoring. Keep them identical.

## Events (`POST /events`, same fields as today)

| kind | object_id | source | target | when |
|---|---|---|---|---|
| `stitch_placed` | `site_N` | actual entry point hit | actual exit point hit | needle driver closed near the entry point, then closed near the exit point, both within 8 mm |
| `stitch_missed` | `site_N` (nearest) | entry hit | exit hit or null | a bite beyond 8 mm, or exit not reached within 10 s |
| `tissue_contact` | null | contact position | null | either tip enters the exclusion zone below the pad top |

`instrument` is `left` or `right` as today. Peg-transfer kinds are unchanged. A site scores once.

## Metrics

`MetricsOut` keeps every existing field so old clients still parse it. For wound closure:

- `successful_transfers` = stitches placed
- `drops` = tissue contacts
- `invalid_releases` = missed stitches
- `completion_pct` = placed / 5 * 100

Extra optional fields (null for peg transfer): `exercise` (string), `mean_bite_error_mm`, `spacing_sd_mm` (standard deviation of the distance between consecutive placed stitches, in mm). The backend computes bite error from `source`/`target` against the site points above; Unity does not send its own error number.

## Unity

- `PegTransferRig.ExerciseId` (string property, `"peg_transfer"` or `"wound_closure"`) and `PegTransferRig.SetExercise(string)` which resets the attempt and swaps the board content. Also a public C# event `ExerciseChanged`.
- The session create body sends `exercise` = the active id.
- Desktop preview: keys `1` = peg transfer, `2` = wound closure. Right hand = needle driver, left hand = forceps in wound closure.
- The OR environment reads `ExerciseId` to decide what to show around the board (abdomen dome with trocar ports for peg transfer; open surgical window on the abdomen for wound closure).
