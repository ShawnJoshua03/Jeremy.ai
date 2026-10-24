# Laparoscopic cholecystectomy: visual spec

Reference: Teleflex "Laparoscopic Cholecystectomy with the Weck Hem-o-lok / Endo5" animation (YouTube TrE_6W_US94). The desktop preview should read like that video: clean, smooth, stylised medical CG, not a cluttered game level. Times below are seconds in the video.

## Room and patient (0:08–0:20)
- Minimal room: blue-grey walls with a darker baseboard band, dark blue-grey floor tiles, cool soft light. No equipment, no surgical lights in frame. (The existing busy OR can stay for the other exercises; lap chole uses the clean look.)
- Light blue-grey OR table with a head pillow. Patient supine, face up, eyes closed, arms at the sides under the drape.
- One teal drape (#5fa3b5 to #3f8296, soft folds) from the neck to past the feet, covering the chest and arms. The skin window is the whole anterior abdomen: from just below the xiphoid to the pubis, flank to flank, with a soft rolled drape edge.
- Skin: smooth, pale peach (#e8b9a0), soft subsurface look, visible navel and a gentle six-pack contour like the video.

## Ports and instruments (0:14–1:20)
Board-local, metres, origin at the navel, x toward the feet, +z = patient's right, y up from the skin.

| Port | Size | Site (x, z) | Angle | Look |
|---|---|---|---|---|
| Veress needle | thin | navel (0, 0) | vertical | steel needle, spring-loaded blunt tip |
| Umbilical (camera) | 10–12 mm | navel (0, 0) | vertical | Teleflex style: blue cap housing, metal stopcock on the side, white threaded collar on the skin, white balloon inside |
| Epigastric | 10 mm | (-0.15, +0.01): midline, about 3 cm below the xiphoid, just right of midline | tilted about 20° toward the right upper quadrant | blue cap, clear cannula |
| Right subcostal, medial | 5 mm | (-0.11, +0.08): midclavicular line, 2 cm below the costal margin | toward the gallbladder | smaller blue cap |
| Right subcostal, lateral | 5 mm | (-0.05, +0.13): anterior axillary line, lower | toward the gallbladder | smaller blue cap |

- Abdomen visibly domes during insufflation (about +3 cm at the centre), before the working ports go in.
- When the umbilical port goes in, the skin can turn see-through to show bowel loops and the balloon inflating (0:24–0:40). Optional but it is the video's signature shot.
- Instruments: long black shafts, black ratcheted handles outside; the clip applier is white with a green trigger and grey jaws; Hem-o-lok clips are white polymer.

## Inside view (1:20–4:20)
- Seen from the umbilical scope, looking up toward the right upper quadrant.
- Background: pale lavender-grey haze (#c9c6d6 to #b8b4c8), soft and bright, with gentle depth fog.
- Liver: large, glossy dark red (#7a1a14 to #a02a20) underside filling the top of the frame.
- Gallbladder: green (#2f6b3a to #3f8a4a), pear shaped, hanging from the liver; the fundus is retracted up and over.
- Calot's triangle below it: cystic duct (green tube) joining the common bile duct (blue, #2a4f9a); cystic artery (salmon, #e08a6a) beside the duct. Smooth tubes with soft speculars. A thin translucent peritoneum layer covers the triangle until it is dissected.

## Shot list for verification
Each shot needs a camera preset in the preview (bridge command `camera <name>`). The screenshot should be compared side by side with the reference frame.

| Preset | Video time | What must match |
|---|---|---|
| `room` | 0:08 | patient on table, drape covers neck to feet, abdomen window, minimal room |
| `veress` | 0:14 | low side view across the abdomen from the patient's left, needle at the navel |
| `umbilical` | 0:22 | the umbilical port seated at the navel |
| `ports` | 1:12 | view from the feet: epigastric and two right subcostal ports in the right places, umbilical port in the foreground |
| `inside` | 1:40 | scope view: liver above, gallbladder, ducts, lavender haze |
| `clip` | 2:10 | clip applier on the cystic duct |

Pass criteria: no limb, hand or instrument handle intersects the body or drape from any preset; every port within about 1 cm of its site; colours within the ranges above; nothing primitive-looking (no visible cube or cylinder edges on the body, drape or organs).
