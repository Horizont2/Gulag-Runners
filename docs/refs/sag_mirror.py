"""The sagging cage, in Python, so its arithmetic can be argued with.

Mirrors the vertex stage of Assets/_Game/Shaders/SaggingSpan.shader and the spring in
Assets/_Game/Presentation/SaggingSpan.cs.

Two things in that pair cannot be judged by looking at a frame, and both will ship
broken if nobody writes them down twice.

The first is the SIZE of the lie. The fight is deterministic fixed-point and the
collision under these props was baked out of their colliders once, at load. The sag is
drawn and never simulated, so the fighters go on standing exactly where the bake put
them: past about ten centimetres of droop they are visibly hovering over the cage they
are standing on. That is a number, it is checkable, and it is not visible in the editor
with nobody in the scene.

The second is the SPRING. It is integrated explicitly, which is fine at sixty frames a
second and is the classic way to make something that detonates the first time the frame
rate drops -- on the exact phone that could least afford it, during the exact fight that
made it drop. Here it can simply be run at every frame time that matters and watched.
"""

# What the component clamps a frame to, so a stall cannot integrate a huge step.
MAX_STEP = 1.0 / 20.0

# Past this much droop the fighters visibly float above collision that never moved.
HOVER_BUDGET = 0.11


def saturate(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def smoothstep(edge0, edge1, x):
    if edge1 <= edge0:
        return 0.0 if x < edge0 else 1.0
    t = saturate((x - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


# --------------------------------------------------------------------- the shape

def point_load(t, at, spread=0.12):
    """Deflection of a taut line under a point load, 1 directly beneath it.

    Two straight segments meeting under the foot, which is what a loaded rope actually
    does -- a smooth parabola under a standing fighter is the tell that nobody looked.
    The rounding is the BOOT, not the rope: a fighter is a boot's width of span, not a
    point, and without it the cage creases to a mathematical V and the crease slides as
    they walk.
    """
    denom = max(at * (1.0 - at), 0.02)
    tent = min(t * (1.0 - at), at * (1.0 - t)) / denom
    near = 1.0 - smoothstep(0.0, max(spread, 1e-3), abs(t - at))
    return saturate(tent) * (1.0 - near * 0.35) + 1.0 * (near * 0.35)


def span_drop(t, rest, load, amount, at, end_stiffness, spread=0.12):
    """How far a point on a slung span drops, in metres. Mirrors the SPAN branch.

    end_stiffness 1 means the anchors do not move at all, which is a plank bolted down;
    lower lets the whole thing sink, which is a cage whose chains are stretching.

    It is a MIX between sinking and bending, not a multiplier over the bend. The first
    version multiplied the point load by an end-pinning envelope, which drags the crease
    away from the foot: with the weight at 0.15 along the span the deepest point came
    out at 0.31. The tent already goes to zero at both ends; pinning them twice only
    bends the shape.
    """
    shaped = rest * 4.0 * t * (1.0 - t) + load * amount * point_load(t, at, spread)
    sunk = rest + load * amount
    return sunk + (shaped - sunk) * end_stiffness


def hanger_drop(t, rest, load, amount):
    """How far a point down a chain drops. Mirrors the HANGER branch: it stretches."""
    return (rest + load * amount) * t


# -------------------------------------------------------------------- the spring

class Settle:
    """The load easing towards what is actually on the span. Mirrors LateUpdate.

    Explicit Euler with the damping applied as a multiplier, exactly as the component
    does it, because the point of this class is to catch the frame time where that stops
    being stable rather than to be a better integrator than the one that ships.
    """

    def __init__(self, stiffness=55.0, damping=9.0):
        self.stiffness = stiffness
        self.damping = damping
        self.load = 0.0
        self.velocity = 0.0

    def frame(self, target, dt):
        dt = min(dt, MAX_STEP)
        self.velocity += (target - self.load) * self.stiffness * dt
        self.velocity -= self.velocity * min(self.damping * dt, 1.0)
        self.load = max(-0.25, min(1.5, self.load + self.velocity * dt))
        return self.load

    def run(self, target, dt, seconds=2.0):
        """Every value it passes through. The shape of the settle, not just its end."""
        out = []
        for _ in range(int(seconds / dt)):
            out.append(self.frame(target, dt))
        return out


def overshoot(trace, target=1.0):
    """How far past the target it goes, as a share. Some is the point; lots is a trampoline."""
    return (max(trace) - target) / max(abs(target), 1e-6) if trace else 0.0


def settled_after(trace, dt, target=1.0, tolerance=0.02):
    """Seconds until it stays within tolerance of the target, or None if it never does."""
    for i in range(len(trace)):
        if all(abs(v - target) <= tolerance for v in trace[i:]):
            return i * dt
    return None


# ------------------------------------------------- what the setup tool writes

# The two have to agree about one number. A chain's full stretch is how far the cage
# hanging off it drops AT ITS ENDS, which for a span is (1 - end_stiffness) of its total
# droop. Get that wrong and the cage visibly parts company with the chains holding it:
#   span sinks at its ends by (1 - 0.85) * (0.025 + 0.055) = 0.012 m
#   chain stretches by                      0.004 + 0.008  = 0.012 m
SPAN = dict(rest=0.025, load=0.055, end_stiffness=0.85, spread=0.12,
            stiffness=70.0, damping=7.0, sway=0.0)

HANGER = dict(rest=0.004, load=0.008, end_stiffness=1.0, spread=0.12,
              stiffness=70.0, damping=7.0, sway=0.06)

RIGS = {"span": SPAN, "hanger": HANGER}
