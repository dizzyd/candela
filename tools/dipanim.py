#!/usr/bin/env python3
"""
Writes candela/assets/candela/patches/player-dip.json: the "candela-dip" player
animation for the dipping rod, third and first person, and the player.json entries
that give it its blending.

Vanilla has nothing that looks like dipping. This bends over the pot the way its
"water" animation bends over the ground, and borrows its blending, then works the
right arm: rod held over the pot, a small lift, plunged so the wicks go into the
tallow, held while the coat takes, lifted out slowly. One second, repeating while
right-click is held; a dip takes 0.8 s (BlockBehaviorDipVat), so it lands on the way
out.

Every number here was read off pictures of a stand-in, not reasoned out - the
seraph's axes do not go the way one would guess. CandelaLooks.DipPosesOnAStandIn
photographs each keyframe from the side:

    python3 tools/dipanim.py --probe   patch with each keyframe also as a still pose
    (run CandelaLooks.DipPosesOnAStandIn and DipPosesFirstPerson; look at
    results/dip-pose-*.png)
    python3 tools/dipanim.py           the patch to ship, without them

What the pictures showed:
  - Arm rotationZ more negative lifts it forward: about -20 hangs at the side,
    -80 upper / -40 lower holds it out level.
  - The rod is translated nearly a block across the hand by its tpHandTransform, so
    turning the item anchor swings it away; offsetY -8 brings the stick back into
    the fist with the wicks hanging.
  - The anchor's rotationZ decides which way the wicks hang. Larger swings them back
    toward the body; each arm pose needs its own value to keep them plumb.
  - In first person the same turn swings the whole rod across the view, so there
    the rod is lowered by the anchor's offsetY instead: -8 above the tallow, -12 in.
"""
import json
import pathlib
import sys

FRAMES = 30

# Held through the dip, third person only: bent over the pot from the waist up, legs
# straight - bending at the hips as well tipped the whole body over.
STANCE = {
    "LowerTorso": {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": 6.0},
    "UpperTorso": {"rotationX": -5.0, "rotationY": -5.0, "rotationZ": 28.0},
    "Head": {"rotationX": 3.0, "rotationY": 0.0, "rotationZ": -10.0},
    "UpperArmL": {"rotationX": -6.0, "rotationY": 3.0, "rotationZ": -8.0},
    "LowerArmL": {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": -10.0},
    "UpperFootL": {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": -4.0},
    "UpperFootR": {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": -4.0},
}

# (frame, upper arm rotZ, lower arm rotZ, item anchor rotZ)
ARM = [
    (0, -85, -45, 76),    # over the pot, wicks hanging
    (5, -88, -48, 78),    # a little lift
    (12, -70, -20, 45),   # plunged: the wicks in the tallow
    (19, -70, -20, 45),   # held while the coat takes
    (24, -80, -30, 60),   # half out
    (30, -85, -45, 76),   # over the pot again
]

# Brings the stick back into the hand once the anchor turns; see above.
ITEM_OFFSET_Y = -8.0

# First person keeps the arm and the hand still - a bend there tips the camera, and
# turning the hand swings the rod across the view - and lowers the rod straight down
# into the pot by the anchor's offset. Takes water-fp's arm offsets, which bring the
# arm into view. (frame, item offsetY), on the third person's timing.
FP_ARM = (-85, -45, 76)
FP_DIP = [
    (0, -8),       # over the pot
    (5, -7),       # a little lift
    (12, -12),     # plunged
    (19, -12.5),   # held while the coat takes
    (24, -10),     # half out
    (30, -8),      # over the pot again
]

FP_OFFSETS = {
    "UpperArmR": {"offsetX": 0.0, "offsetY": 0.0, "offsetZ": -3.0},
    "LowerArmR": {"offsetX": 0.0, "offsetY": 1.0, "offsetZ": 0.0},
}


def pose(upper, lower, item, fp, item_offset_y=ITEM_OFFSET_Y):
    el = {} if fp else {k: dict(v) for k, v in STANCE.items()}
    el["UpperArmR"] = {"rotationX": 8.0, "rotationY": 10.0, "rotationZ": float(upper)}
    el["LowerArmR"] = {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": float(lower)}
    el["ItemAnchor"] = {"offsetX": 0.0, "offsetY": float(item_offset_y), "offsetZ": 0.0,
                        "rotationX": 0.0, "rotationY": 0.0, "rotationZ": float(item)}
    if fp:
        for name, offs in FP_OFFSETS.items():
            el[name] = {**offs, **el[name]}
    return el


def tp_keyframes():
    return [(f, pose(u, l, i, False)) for f, u, l, i in ARM]


def fp_keyframes():
    return [(f, pose(*FP_ARM, True, item_offset_y=oy)) for f, oy in FP_DIP]


def shape_anim(code, keyframes):
    return {
        "name": code,
        "code": code,
        "quantityframes": FRAMES + 1,
        "onActivityStopped": "EaseOut",
        "onAnimationEnd": "Repeat",
        "keyframes": keyframes,
    }


ARMS = ["UpperArmR", "LowerArmR", "UpperArmL", "LowerArmL", "ItemAnchor"]


def meta(code, weight):
    # water's: the arms and item weighted to win over whatever else is playing, the
    # body added on top.
    return {
        "code": code,
        "animation": code,
        "animationSpeed": 1,
        "easeInSpeed": 10,
        "easeOutSpeed": 6,
        "blendMode": "Add",
        "elementWeight": {name: weight for name in ARMS},
        "elementBlendMode": {name: "AddAverage" for name in ARMS},
    }


def add(patches, code, keyframes, weight):
    patches.append({"op": "add", "path": "/animations/-", "value": shape_anim(code, keyframes),
                    "file": "game:shapes/entity/humanoid/seraph-faceless.json"})
    patches.append({"op": "add", "path": "/client/animations/-", "value": meta(code, weight),
                    "file": "game:entities/humanoid/player.json"})


def still(elements):
    return [{"frame": 0, "elements": elements}, {"frame": FRAMES, "elements": elements}]


patches = []
add(patches, "candela-dip", [{"frame": f, "elements": el} for f, el in tp_keyframes()], 20)
add(patches, "candela-dip-fp", [{"frame": f, "elements": el} for f, el in fp_keyframes()], 60)

if "--probe" in sys.argv:
    # Each keyframe held still, as candela-probe-N and candela-probe-fp-N, to
    # photograph one pose at a time.
    for n, (_, el) in enumerate(tp_keyframes()):
        add(patches, "candela-probe-%d" % n, still(el), 20)
    for n, (_, el) in enumerate(fp_keyframes()):
        add(patches, "candela-probe-fp-%d" % n, still(el), 60)

out = pathlib.Path(__file__).resolve().parent.parent / "candela/assets/candela/patches/player-dip.json"
out.write_text(json.dumps(patches, indent="\t") + "\n")
print("wrote", out, "(with probe poses)" if "--probe" in sys.argv else "")
