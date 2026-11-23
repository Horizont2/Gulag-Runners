"""Mirror of GulagRunners.Sim fixed-point math, to check the tuning actually produces
the numbers the design docs ask for. Same truncation, same order of operations."""
ONE = 1 << 16
def milli(t): return (t << 16) // 1000
def mul(a, b):
    r = (a * b)
    return r >> 16 if r >= 0 else -((-r) >> 16)   # C# >> on negative int is arithmetic;
def mul_csharp(a, b): return (a * b) >> 16        # this is the real C# behaviour
def to_f(x): return x / ONE

DT = ONE // 60                     # 1092
RUN, CROUCH = milli(3600), milli(1600)
GACC, GDEC = milli(40000), milli(50000)
AACC, ADEC = milli(22000), milli(10000)
GRAV, MAXFALL = milli(28000), milli(18000)
JUMP, CUT = milli(9500), milli(450)
CLIMB_UP, CLIMB_DN = milli(2000), milli(2600)
DODGE_SPEED, DODGE_FRAMES = milli(7000), 21
FLOOR_H, ROOM_W = 2.5, 7.2

def move_towards(cur, tgt, maxd):
    d = tgt - cur
    if abs(d) <= maxd: return tgt
    return cur + (maxd if d > 0 else -maxd)

print(f"dt raw={DT}  = {to_f(DT):.6f}s  (ideal 0.016667)  drift/s = {(to_f(DT)*60-1)*1000:.2f} ms")

# --- jump apex, held to the top -------------------------------------------------
y, vy, t = 0, JUMP, 0
apex = 0
while True:
    vy = vy - mul_csharp(GRAV, DT)
    if vy < -MAXFALL: vy = -MAXFALL
    y = y + mul_csharp(vy, DT)
    t += 1
    apex = max(apex, y)
    if vy <= 0 and y <= 0: break
print(f"jump apex      = {to_f(apex):.3f} m   (analytic 1.611)   floor height {FLOOR_H} m")
print(f"  -> clears a floor? {'YES - BUG' if to_f(apex) >= FLOOR_H else 'no, correct'}")
print(f"airtime        = {t/60:.3f} s")

# --- short hop (button released on frame 4) -------------------------------------
y, vy, apex2 = 0, JUMP, 0
for f in range(600):
    if f == 4: vy = mul_csharp(vy, CUT)
    vy = vy - mul_csharp(GRAV, DT)
    if vy < -MAXFALL: vy = -MAXFALL
    y = y + mul_csharp(vy, DT)
    apex2 = max(apex2, y)
    if vy <= 0 and y <= 0: break
print(f"short hop apex = {to_f(apex2):.3f} m")

# --- run: time to cross one room from standstill --------------------------------
x, vx, frames = 0, 0, 0
while to_f(x) < ROOM_W:
    vx = move_towards(vx, RUN, mul_csharp(GACC, DT))
    x = x + mul_csharp(vx, DT)
    frames += 1
print(f"cross {ROOM_W} m room = {frames/60:.2f} s   (docs/03 budget ~2.0 s)")
print(f"time to top speed    = {next(i for i,_ in enumerate(iter([0])) ) if False else ''}", end="")
vx, f2 = 0, 0
while vx < RUN:
    vx = move_towards(vx, RUN, mul_csharp(GACC, DT)); f2 += 1
print(f"{f2/60:.3f} s")

# --- climb one floor -------------------------------------------------------------
print(f"climb {FLOOR_H} m   = {FLOOR_H/to_f(CLIMB_UP):.2f} s up, {FLOOR_H/to_f(CLIMB_DN):.2f} s down"
      f"   (docs/03 budget ~1.5 s incl. mounting)")

# --- dodge distance --------------------------------------------------------------
x = 0
for _ in range(DODGE_FRAMES): x = x + mul_csharp(DODGE_SPEED, DT)
print(f"dodge distance = {to_f(x):.2f} m over {DODGE_FRAMES/60:.2f} s")

# --- overflow safety: worst-case multiply in the motor ----------------------------
worst = max(abs(GRAV), abs(MAXFALL), abs(GDEC), abs(JUMP), abs(RUN))
prod = worst * max(DT, ONE)
print(f"\nworst-case mul operand = {worst} ({to_f(worst):.1f}), product = {prod} "
      f"({'fits in int64, ok' if abs(prod) < 2**63 else 'OVERFLOW'})")
print(f"largest position representable = {to_f(2**31-1):.0f} m  (arena is ~22 m wide)")
