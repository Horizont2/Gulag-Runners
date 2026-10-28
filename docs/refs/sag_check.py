"""Does the sagging cage give the right amount, and does its spring stay put?

Three things here would ship broken and look fine in the editor, because the editor is
a still frame with nobody standing on anything.

  THE HOVER. The sag is drawn, never simulated: the collision under these props was
  baked out of their colliders once at load and does not move. So every centimetre of
  droop is a centimetre the fighters stand above the cage they are on. There is a budget
  and it is small, and nothing on screen says when it has been spent.

  THE ANCHORS. A span pinned at both ends must not move AT its ends, or the plank pulls
  out of the posts holding it. One number controls that and it is easy to mistune.

  THE SPRING. It is integrated explicitly. That is stable at sixty frames a second and
  is the standard way to build something that detonates the first time the frame rate
  drops -- on the worst phone, during the busiest fight. Here it is simply run at every
  frame time that matters, including the long ones.

Run it with no arguments. Exits non-zero on a fault.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sag_mirror as sm

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                      "..", "..", "Gulag runners", "Assets")
SHADER = os.path.join(ASSETS, "_Game", "Shaders", "SaggingSpan.shader")
SETUP = os.path.join(ASSETS, "_Game", "Editor", "SaggingSpanSetup.cs")
COMPONENT = os.path.join(ASSETS, "_Game", "Presentation", "SaggingSpan.cs")

# Frame times worth surviving: a good phone, a tired one, and the stall the component
# clamps to. A spring that is only stable at 60 is not stable.
FRAME_TIMES = (1.0 / 120.0, 1.0 / 60.0, 1.0 / 30.0, 1.0 / 20.0, 1.0 / 5.0)


def tool_values():
    """What SaggingSpanSetup writes, read back out of the C#."""
    with open(SETUP, encoding="utf-8") as fh:
        source = fh.read()

    def pick(prop):
        m = re.search(re.escape(prop) + r'",\s*hanger \? (-?[\d.]+)f : (-?[\d.]+)f', source)
        if m:
            return float(m.group(1)), float(m.group(2))
        m = re.search(re.escape(prop) + r'",\s*(-?[\d.]+)f', source)
        return (float(m.group(1)), float(m.group(1))) if m else (None, None)

    out = {"hanger": {}, "span": {}}
    for prop, key in (("_RestSag", "rest"), ("_LoadSag", "load"),
                      ("_EndStiffness", "end_stiffness"), ("_LoadSpread", "spread")):
        h, s = pick(prop)
        if h is None:
            continue
        out["hanger"][key] = h
        out["span"][key] = s

    m = re.search(r"swayPerLoad = hanger \? (-?[\d.]+)f : (-?[\d.]+)f", source)
    if m:
        out["hanger"]["sway"] = float(m.group(1))
        out["span"]["sway"] = float(m.group(2))
    return out


def component_defaults():
    """The spring's serialised defaults, read back out of the component."""
    with open(COMPONENT, encoding="utf-8") as fh:
        source = fh.read()
    out = {}
    for field, key in (("stiffness", "stiffness"), ("damping", "damping")):
        m = re.search(r"public float " + field + r" = (-?[\d.]+)f;", source)
        if m:
            out[key] = float(m.group(1))
    return out


def main():
    problems, notes = [], []

    for path, what in ((SHADER, "the shader"), (SETUP, "the setup tool"),
                       (COMPONENT, "the component")):
        if not os.path.exists(path):
            print(f"sag_check: {what} is missing ({os.path.relpath(path, ASSETS)})")
            return 1

    # --- does the mirror still describe the tool and the component?

    written = tool_values()
    spring = component_defaults()
    for kind, settings in sorted(written.items()):
        described = sm.RIGS[kind]
        for key, value in sorted(settings.items()):
            if abs(value - described[key]) > 1e-4:
                problems.append(
                    f"{kind}: the tool writes {key} = {value} but sag_mirror says "
                    f"{described[key]} -- this check is measuring something the game "
                    f"will not draw")
    for key, value in sorted(spring.items()):
        if abs(value - sm.SPAN[key]) > 1e-4:
            problems.append(
                f"the component's default {key} is {value} but sag_mirror says "
                f"{sm.SPAN[key]}")

    # --- the hover budget

    for kind, rig in sorted(sm.RIGS.items()):
        deepest = rig["rest"] + rig["load"]
        if deepest > sm.HOVER_BUDGET:
            problems.append(
                f"{kind}: droops {deepest * 100:.1f} cm at full load, over the "
                f"{sm.HOVER_BUDGET * 100:.0f} cm budget -- the collision under it never "
                f"moves, so the fighters stand that far above what they are standing on")
        else:
            notes.append(f"{kind}: {deepest * 100:.1f} cm at full load, "
                         f"{(sm.HOVER_BUDGET - deepest) * 100:.1f} cm of budget left")

    # --- the anchors of a pinned span

    rig = sm.SPAN
    sinks = (1.0 - rig["end_stiffness"]) * (rig["rest"] + rig["load"])
    for t in (0.0, 1.0):
        drop = sm.span_drop(t, rig["rest"], rig["load"], 1.0, 0.5,
                            rig["end_stiffness"], rig["spread"])
        if drop > sinks + 1e-6:
            problems.append(
                f"a span pinned at {rig['end_stiffness']:.2f} drops {drop * 1000:.0f} mm "
                f"at t={t:g}, more than the {sinks * 1000:.0f} mm its anchors are "
                f"allowed to give -- the plank pulls out of its posts")

    # --- do the chains reach the cage they are holding?
    #
    # A cage hung on chains sinks at its ends by exactly as much as those chains
    # stretch. These are two separate numbers in two separate materials and nothing
    # else ties them together, so when they drift the cage parts company with its own
    # chains -- which reads as the cage having come loose, the one thing the whole
    # effect is meant to avoid.
    stretch = sm.HANGER["rest"] + sm.HANGER["load"]
    if abs(stretch - sinks) > 0.004:
        problems.append(
            f"the chains stretch {stretch * 1000:.0f} mm but the cage they carry sinks "
            f"{sinks * 1000:.0f} mm at its ends -- a {abs(stretch - sinks) * 1000:.0f} mm "
            f"gap opens between the cage and the chains holding it")
    else:
        notes.append(f"chains stretch {stretch * 1000:.0f} mm, cage ends sink "
                     f"{sinks * 1000:.0f} mm -- they stay attached")

    # --- the load lands under the foot, wherever the foot is

    for at in (0.15, 0.35, 0.5, 0.72, 0.9):
        samples = [(t / 200.0,
                    sm.span_drop(t / 200.0, 0.0, rig["load"], 1.0, at,
                                 rig["end_stiffness"], rig["spread"]))
                   for t in range(201)]
        peak = max(samples, key=lambda s: s[1])[0]
        if abs(peak - at) > 0.12:
            problems.append(
                f"with the weight at {at:g} along the span the deepest point is at "
                f"{peak:.2f} -- the crease is not under the foot")

    # --- the spring, at every frame time that matters

    for kind, rig in sorted(sm.RIGS.items()):
        for dt in FRAME_TIMES:
            # STABILITY, at every frame time. Run long enough that a spring which is
            # going to diverge has done it. The clamp in the component is a last
            # resort, not a design: reaching it means the maths went somewhere the
            # clamp then hid, and the cage snapped instead of settling.
            long_run = sm.Settle(rig["stiffness"], rig["damping"]).run(1.0, dt, seconds=12.0)
            if not long_run:
                continue
            if max(long_run) > 1.5 - 1e-6 or min(long_run) < -0.25 + 1e-6:
                problems.append(
                    f"{kind}: at {1 / dt:.0f} fps the spring reaches its clamp -- without "
                    f"it the cage would have turned inside out")
            if abs(long_run[-1] - 1.0) > 0.02:
                problems.append(
                    f"{kind}: at {1 / dt:.0f} fps the spring does not converge -- it ends "
                    f"at {long_run[-1]:.2f} instead of 1.00")

            over = sm.overshoot(long_run)
            if over > 0.6:
                problems.append(
                    f"{kind}: at {1 / dt:.0f} fps it overshoots by {over:.0%} -- that is "
                    f"a trampoline, not a cage")

            # RESPONSIVENESS, only where the game is actually playable. Below 30 fps
            # the component clamps the step, so the spring advances in slow motion --
            # correct, deliberate, and not something to measure against a stopwatch.
            if dt > 1.0 / 30.0:
                continue
            trace = sm.Settle(rig["stiffness"], rig["damping"]).run(1.0, dt, seconds=3.0)
            when = sm.settled_after(trace, dt)
            if when is None or when > 1.6:
                problems.append(
                    f"{kind}: at {1 / dt:.0f} fps the spring takes "
                    f"{'over three' if when is None else f'{when:.2f}'} s to settle -- "
                    f"the cage is still moving long after the step")
            if sm.overshoot(trace) < 0.08:
                problems.append(
                    f"{kind}: at {1 / dt:.0f} fps it overshoots by only "
                    f"{sm.overshoot(trace):.1%}, so it eases in rather than taking "
                    f"weight -- the overshoot IS the feeling")

        # What the settle actually looks like at the rate the game targets. A spring
        # that never settles has already been reported above, so this only has to avoid
        # crashing on it -- which the first version did not, and a check that dies on
        # the fault it is hunting reports nothing at all.
        trace = sm.Settle(rig["stiffness"], rig["damping"]).run(1.0, 1 / 60.0, seconds=3.0)
        when = sm.settled_after(trace, 1 / 60.0)
        notes.append(
            f"{kind} at 60 fps: overshoots {sm.overshoot(trace):.0%}, "
            + (f"settles in {when:.2f} s" if when is not None else "never settles"))

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"sag_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
