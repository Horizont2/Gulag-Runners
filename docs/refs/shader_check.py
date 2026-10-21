"""Does every shader property reach the GPU, and does every tool write a real one?

Two faults have already shipped from this corner of the project, and neither of them is
visible by reading the file that contains it.

The first: `_SpecColor` was listed in a shader's Properties block and nowhere else. A
Properties entry is an INSPECTOR declaration -- it makes a slider appear and it makes the
material serialise a value. It does not declare an HLSL variable. The shader failed to
compile with "undeclared identifier '_SpecColor'" at a line two hundred below the one that
was actually wrong, and every banner in the scene turned magenta.

The second is the same gap pointed the other way. `BannerClothSetup` measures a rig off the
mesh and writes it with `material.SetVector("_HangAxis", ...)`. A typo there, or a property
renamed in the shader, does not fail: `SetVector` on a name the shader does not have is
silently discarded, the material keeps the shader's default, and the banner looks wrong in
a way that reads as bad tuning rather than as a broken wire.

So this check reads both ends and fails if they do not meet:

  * every non-texture property is declared in CBUFFER_START(UnityPerMaterial);
  * every texture property sampled through TRANSFORM_TEX has its _ST in that buffer;
  * every name in the buffer is a real property, so a renamed property leaves no orphan
    uniform behind that quietly reads garbage from the next material in the batch;
  * every property under the "Rig" header is written by the setup tool, because a rig the
    tool does not fill is a rig that keeps a default the mesh has never agreed to;
  * and every material.SetX("_Name") in a C# file that names one of these shaders is a
    name that shader actually has.

It parses text rather than compiling, because the Roslyn pass in compile_check.py does not
see .shader files at all and Unity only reports a shader error once the asset is imported
on a machine with a GPU.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
GAME = os.path.join(ROOT, "Gulag runners", "Assets", "_Game")
SHADERS = os.path.join(GAME, "Shaders")

TYPES = ("2D", "3D", "Cube", "CubeArray", "2DArray", "Any",
         "Color", "Vector", "Float", "Int", "Integer", "Range")
TEXTURE_TYPES = ("2D", "3D", "Cube", "CubeArray", "2DArray")

PROP = re.compile(r'^[ \t]*(?:\[[^\]]*\][ \t]*)*([_A-Za-z]\w*)[ \t]*\([ \t]*"[^"]*"[ \t]*,'
                  r'[ \t]*(' + "|".join(TYPES) + r')\b')
UNIFORM = re.compile(r'^[ \t]*(?:float|half|real|int|uint|bool)[1-4]?(?:x[1-4])?[ \t]+'
                     r'(\w+)[ \t]*;')
SETTER = re.compile(r'\.Set(?:Vector|Float|Color|Texture|Int|Matrix|Buffer)\(\s*"([^"]+)"')


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def block(src, opener):
    """The text between opener's brace and its match."""
    start = src.find(opener)
    if start < 0:
        return ""
    brace = src.find("{", start)
    if brace < 0:
        return ""
    depth, i = 0, brace
    while i < len(src):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[brace + 1:i]
        i += 1
    return src[brace + 1:]


def properties(src):
    """[(name, type, header it sits under)], in declaration order."""
    found, header = [], ""
    for line in block(src, "Properties").splitlines():
        h = re.search(r'\[Header\(\s*([^)\]]*?)\s*\)\]', line)
        if h:
            header = h.group(1)
        m = PROP.match(line)
        if m:
            found.append((m.group(1), m.group(2), header))
    return found


def uniforms(src):
    names = []
    for chunk in re.findall(r'CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END', src, re.S):
        for line in chunk.splitlines():
            m = UNIFORM.match(line)
            if m:
                names.append(m.group(1))
    return names


def main():
    problems = []
    shaders = {}

    if not os.path.isdir(SHADERS):
        print("shader_check: no Shaders folder")
        return 0

    for name in sorted(os.listdir(SHADERS)):
        if not name.endswith(".shader"):
            continue
        src = read(os.path.join(SHADERS, name))
        props = properties(src)
        declared = uniforms(src)
        shaders[name] = props

        title = re.search(r'^\s*Shader\s+"([^"]+)"', src, re.M)
        if title:
            shaders[title.group(1)] = props

        if not declared:
            problems.append(f"{name}: no CBUFFER_START(UnityPerMaterial) -- every property "
                            f"in it would be an undeclared identifier")
            continue

        seen = set(declared)
        for prop, kind, _ in props:
            if kind in TEXTURE_TYPES:
                if re.search(r'TRANSFORM_TEX\s*\([^)]*\b' + re.escape(prop) + r'\s*\)', src) \
                        and prop + "_ST" not in seen:
                    problems.append(f"{name}: {prop} is sampled through TRANSFORM_TEX but "
                                    f"{prop}_ST is not in UnityPerMaterial")
                continue
            if prop not in seen:
                problems.append(f"{name}: property {prop} is not declared in "
                                f"UnityPerMaterial -- it will not compile the first time "
                                f"the HLSL names it")

        known = {p for p, _, _ in props}
        for u in declared:
            if u in known:
                continue
            if u.endswith("_ST") and u[:-3] in known:
                continue
            if u.endswith(("_TexelSize", "_HDR")) and u.rsplit("_", 1)[0] in known:
                continue
            problems.append(f"{name}: {u} is in UnityPerMaterial but is not a property -- "
                            f"it reads whatever the batch left there")

        for u in declared:
            if declared.count(u) > 1:
                problems.append(f"{name}: {u} is declared twice in UnityPerMaterial")
                break

    # The C# side: a file that names one of these shaders must write names it has.
    for dirpath, _, files in os.walk(GAME):
        for f in files:
            if not f.endswith(".cs"):
                continue
            src = read(os.path.join(dirpath, f))
            named = re.findall(r'"(Gulag Runners/[^"]+)"', src)
            targets = [n for n in named if n in shaders]
            if len(targets) != 1:
                continue
            shader = targets[0]
            have = {p for p, _, _ in shaders[shader]}
            for prop in sorted(set(SETTER.findall(src))):
                if prop not in have:
                    problems.append(f"{f}: writes {prop}, which \"{shader}\" does not "
                                    f"have -- the call is discarded in silence")

            rig = {p for p, _, h in shaders[shader] if h.startswith("Rig")}
            written = set(SETTER.findall(src))
            for prop in sorted(rig - written):
                problems.append(f"{f}: \"{shader}\" declares {prop} under the Rig header "
                                f"but nothing here writes it -- it keeps a default the "
                                f"mesh never agreed to")

    for p in problems:
        print(f"  {p}")
    count = len([k for k in shaders if k.endswith(".shader")])
    print(f"shader_check: {len(problems)} problem(s) across {count} shader(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
