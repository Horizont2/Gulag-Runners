"""The compiler checks this project cannot run.

There is no Unity and no .NET in this environment, so every C# edit here is a text edit and
the first thing that reads it for real is Unity, in front of Vlad. These are the two mistakes
a hand edit actually makes, and both are findable from the text:

  * brackets that do not balance, which is what a bad splice looks like;
  * a local declared twice in one scope, which is what inserting a block into the middle of a
    method looks like. CS0128 — it has happened, in LootMotor, where a `Fix centre` added near
    the top of a method shadowed the `FixVec2 centre` the end of it already used, and took the
    call that used it down with it;
  * a field added to a config struct and not assigned in the builder that returns one. CS0165
    — C# will not let you return a struct local with a field never written, and every config
    here is built that way, so adding a field is two edits and it is easy to make one. It has
    happened, in MoveConfig, with HomeDepth.
  * a string literal left open at the end of a line. CS1010 — it has happened, in HudBuilder,
    where a patch to a Debug.Log left a stray `+ "` and the file reached Unity broken. It
    slipped past the bracket check for a reason worth knowing: the regex that blanks string
    bodies has `[^"\\]` in it, which matches a NEWLINE as happily as anything else, so an
    unclosed quote simply pairs with the next quote two lines down and the braces still
    balance. A line-aware scanner is the only way to see it.

  * a call to something the file no longer defines. CS0103 — see the long note further down;
    it cost an afternoon because Unity answers a broken editor assembly by keeping the
    PREVIOUS one, so the tool on the menu is the old one and the change looks like it did
    nothing.
  * the same type declared in two files. CS0101, and the one a MERGE makes: a generated
    `InputSystem_Actions.cs` sat in Assets/ while the .inputactions it came from had wrapper
    generation switched back on, so Unity wrote a second copy of the same class beside the
    asset. One class, two files, and every member in it reported twice — which is what
    "висипало багато помилок" after a merge looks like.

Neither is a type check: this cannot tell you a method does not exist. It tells you the edit
is at least well-formed, which is the part a careful patch can get wrong without noticing.
"""
import io
import os
import re
import sys

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                      "Gulag runners", "Assets")
ROOT = ASSETS

# Code that arrived as somebody else's zip: it compiled before it got here, and a rule
# written against this project's habits has no business failing a vendor's file. _Leftovers
# is the template project Unity made, kept only for reference.
#
# Everything else under Assets IS checked -- not just _Game. A merge brings code in beside
# it (Core/NomadUI did), and the half of the project a check does not walk is the half that
# arrives broken.
SKIP = {"ThirdParty", "_Leftovers", "Plugins", "StreamingAssets", "TextMesh Pro",
        "Samples", "Resources.meta"}

DECL = re.compile(r'^\s*(?:(?:readonly|const)\s+)?'
                  r'(?:(?:in|ref|out)\s+)?'
                  r'([A-Za-z_][\w<>,\[\]\.]*)\s+([a-z_]\w*)\s*(?:=[^=]|;)')

# Words that start a statement rather than a declaration, and so look like a type to the
# pattern above without being one.
NOT_A_TYPE = {"return", "if", "else", "for", "foreach", "while", "switch", "case", "do",
              "using", "new", "throw", "break", "continue", "lock", "yield", "public",
              "private", "static", "internal", "protected", "var", "get", "set", "await"}


def strip(src):
    """Comments and string bodies out, so a brace in a string is not a brace."""
    src = re.sub(r'//[^\n]*', '', src)
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    src = re.sub(r'@"(?:[^"]|"")*"', '""', src)
    src = re.sub(r'"(?:\\.|[^"\\])*"', '""', src)
    src = re.sub(r"'(?:\\.|[^'\\])'", "''", src)
    return src


def unclosed_strings(path, src):
    """A regular string or char literal that does not close on its own line.

    Walks the file as a character stream rather than with a regex, because the thing being
    looked for is exactly the case a regex gets wrong: `"` ... newline ... `"` is a valid
    match for every pattern that treats the body as "anything but a quote", and an unclosed
    literal is indistinguishable from a long one unless newlines are tracked.

    Verbatim (@-quoted) and raw (triple-quoted) strings are allowed to span lines and are
    skipped whole; everything else must close before its line ends.
    """
    out = []
    i, line, n = 0, 1, len(src)
    while i < n:
        c = src[i]

        if c == "\n":
            line += 1; i += 1; continue

        if src.startswith("//", i):
            i = src.find("\n", i)
            if i < 0: break
            continue

        if src.startswith("/*", i):
            end = src.find("*/", i + 2)
            if end < 0:
                out.append(f"{path}:{line}: block comment is never closed")
                break
            line += src.count("\n", i, end)
            i = end + 2; continue

        if src.startswith('"""', i):                      # raw string literal
            end = src.find('"""', i + 3)
            if end < 0:
                out.append(f"{path}:{line}: raw string literal is never closed")
                break
            line += src.count("\n", i, end)
            i = end + 3; continue

        if c == "@" and i + 1 < n and src[i + 1] == '"':    # verbatim, may span lines
            j = i + 2
            while j < n:
                if src[j] == '"':
                    if j + 1 < n and src[j + 1] == '"': j += 2; continue
                    break
                j += 1
            if j >= n:
                out.append(f"{path}:{line}: verbatim string is never closed")
                break
            line += src.count("\n", i, j)
            i = j + 1; continue

        if c in '"\'':
            quote, j = c, i + 1
            while j < n and src[j] != quote and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            if j >= n or src[j] == "\n":
                kind = "string" if quote == '"' else "character"
                out.append(f"{path}:{line}: {kind} literal is not closed before the end of "
                           f"the line (CS1010)")
                # Resume on the next line: one stray quote should not make every line after
                # it look broken too.
                i = src.find("\n", i)
                if i < 0: break
                continue
            i = j + 1; continue

        i += 1
    return out


def brackets(path, src):
    out = []
    s = strip(src)
    for open_c, close_c in (("{", "}"), ("(", ")"), ("[", "]")):
        if s.count(open_c) != s.count(close_c):
            out.append(f"{path}: {open_c}{close_c} do not balance "
                       f"({s.count(open_c)} vs {s.count(close_c)})")
    return out


def duplicate_locals(path, src):
    """A local that shadows one already live in the same method.

    Brace depth is tracked rather than parsed: depth 1 is the namespace, 2 the type, so a
    method body starts at 3. Good enough to find a name declared twice, which is all this is
    for.
    """
    out = []
    depth, scopes, method_depth = 0, [set()], None

    for n, raw in enumerate(src.split("\n"), 1):
        line = re.sub(r'//.*', '', raw)

        if method_depth is not None and depth >= method_depth:
            m = DECL.match(line)
            if m and m.group(1) not in NOT_A_TYPE and m.group(2) not in NOT_A_TYPE:
                name = m.group(2)
                if any(name in s for s in scopes[method_depth:]):
                    out.append(f"{path}:{n}: '{name}' is already a local in this method")
                else:
                    scopes[-1].add(name)

        for _ in range(line.count("{")):
            depth += 1
            scopes.append(set())
            if method_depth is None and depth >= 3:
                method_depth = depth
        for _ in range(line.count("}")):
            depth -= 1
            if len(scopes) > 1:
                scopes.pop()
            if method_depth is not None and depth < method_depth:
                method_depth = None
    return out


FIELD = re.compile(r'^\s*public\s+(?!static|const|abstract|override|virtual|readonly\s+static)'
                   r'(?:readonly\s+)?([A-Za-z_][\w<>,\[\]\.]*)\s+([A-Z]\w*)\s*;', re.M)
LOCAL_STRUCT = re.compile(r'^\s*([A-Z][\w]*)\s+([a-z_]\w*)\s*;\s*$')  # one line at a time


def struct_fields(sources):
    """Every public instance field of every struct in the project, by type name."""
    out = {}
    for src in sources.values():
        for m in re.finditer(r'\bstruct\s+([A-Z]\w*)', src):
            name = m.group(1)
            body = src[m.end():]
            depth, start = 0, None
            for i, ch in enumerate(body):
                if ch == "{":
                    depth += 1
                    if start is None:
                        start = i + 1
                elif ch == "}":
                    depth -= 1
                    if depth == 0:
                        break
            if start is None:
                continue
            out.setdefault(name, set()).update(
                f.group(2) for f in FIELD.finditer(body[start:i]))
    return out


def unassigned_struct_fields(path, src, fields):
    """A builder that declares a struct local, fills it in and returns it, but misses a field.

    This is the shape every config in the project is built with — `MoveConfig c; c.X = ...;
    return c;` — and C# requires every field to be written before the struct is used.
    """
    out = []
    lines = src.split("\n")
    for n, line in enumerate(lines):
        m = LOCAL_STRUCT.match(line)
        if not m or m.group(1) not in fields:
            continue
        type_name, local = m.group(1), m.group(2)

        # The rest of the method: up to the return of this local, or 400 lines, whichever first.
        body, end = [], min(len(lines), n + 400)
        for k in range(n + 1, end):
            body.append(lines[k])
            if re.match(r'^\s*return\s+' + local + r'\s*;', lines[k]):
                break
        else:
            continue

        written = set(re.findall(r'\b' + local + r'\.(\w+)\s*=[^=]', "\n".join(body)))

        # Nothing written at all is not a half-built config. One line of regex cannot tell
        # a local from a FIELD of the same shape, and a field is zero-initialised by C# and
        # perfectly legal -- while a local that really is never written does not compile,
        # which compile_check says with Roslyn rather than guessing. So this rule speaks
        # only about the case it can actually judge: a builder that fills some in and
        # forgets the rest.
        if not written:
            continue

        missing = sorted(fields[type_name] - written)
        if missing:
            out.append(f"{path}:{n + 1}: '{local}' is returned as a {type_name} with "
                       f"{', '.join(missing)} never assigned")
    return out



# ------------------------------------------------------------------ the fifth mistake
#
# A CALL TO SOMETHING THE FILE NO LONGER HAS. This one shipped the day it was written: a
# block of a tool was cut out -- the folder-scanning half of MenuBuilder -- and it took
# three methods with it that the other half still called. Unity answered with six CS0103s
# and, because the editor assembly then does not build, kept running the PREVIOUS
# compile: the tool on the menu was the old one, the change looked like it had not
# happened, and the real message was five screens up in the console.
#
# Which is the worst shape a mistake can have here -- it looks like the edit did nothing.
# So it is checked: every call with no receiver has to be a method THIS FILE defines, or
# something inherited from the handful of Unity base classes the project derives from.
#
# Per file, and not per project, on purpose. A bare call in C# resolves inside its own type
# and its bases -- a static on another class arrives as `Other.Thing()`, with a receiver the
# pattern below skips -- so the file is the right scope, and the project is the wrong one:
# the three builder windows each have their own `Import`, `Size` and `MeasureScale`, so
# asking "does ANY file define this name" would have answered yes for exactly the three
# methods that had just been deleted from MenuBuilder, and said ok. That is the whole bug,
# missed by the check written for it.
#
# What makes the narrow scope safe is one fact about this project, worth re-checking before
# widening it: no type here derives from another type here (every base is MonoBehaviour,
# EditorWindow, Editor, ScriptableObject or an interface), there is no `using static`, and
# there are no partial types. So nothing can legally call a bare method that lives in
# another file.

# Called bare and inherited rather than written here. Only the ones this project's own
# types really use; anything else is a name to add deliberately after reading why.
INHERITED = {
    # MonoBehaviour / Object
    "GetComponent", "GetComponentInChildren", "GetComponentInParent",
    "GetComponentsInChildren", "GetComponentsInParent", "GetComponents",
    "TryGetComponent", "Instantiate", "Destroy", "DestroyImmediate", "FindObjectsByType",
    "FindFirstObjectByType", "FindAnyObjectByType", "StartCoroutine", "StopCoroutine",
    "StopAllCoroutines", "Invoke", "CancelInvoke", "InvokeRepeating", "print",
    "DontDestroyOnLoad", "SendMessage", "BroadcastMessage", "CompareTag",
    # EditorWindow / Editor / ScriptableObject
    "GetWindow", "Close", "Repaint", "ShowUtility", "ShowModalUtility", "CreateInstance",
    "DrawDefaultInspector", "serializedObject", "ApplyModifiedProperties", "Update",
    "OnInspectorGUI", "SetDirty", "EditorGUILayout", "Focus",
    # C# itself
    "nameof", "typeof", "sizeof", "default", "checked", "unchecked", "base", "this",
    "value", "stackalloc", "with", "when", "and", "or", "not",
    # System.Object's own statics, called bare from anywhere
    "ReferenceEquals", "Equals", "GetHashCode", "ToString",
}

CONTROL = {"if", "else", "for", "foreach", "while", "switch", "case", "do", "using",
           "lock", "return", "throw", "catch", "fixed", "yield", "select", "where",
           "from", "in", "is", "as", "new", "get", "set", "add", "remove", "init",
           # A MODIFIER in front of a tuple return type reads as a call to itself:
           # "public static (int slow, int quick) Days(" has "static (" in it. These are
           # never method names, so excluding them costs nothing and stops the one false
           # alarm a tuple-returning method produces.
           "public", "private", "protected", "internal", "static", "readonly", "sealed",
           "override", "virtual", "async", "partial", "extern", "unsafe", "abstract",
           "implicit", "explicit", "operator", "delegate", "event", "const"}

# A declaration's return type may be a generic with spaces in it -- Dictionary<string,
# Sprite> -- so the type is matched as "a word, optionally followed by a bracketed lump",
# not as one token of word characters. Missing that read six real methods as undefined.
# A TUPLE return type is a type in brackets -- (int slow, int quick) Days(int price) --
# and the "a word, maybe with a bracketed lump" rule above cannot see one, so the method
# read as undefined everywhere it was called. It is matched as its own alternative rather
# than by loosening the word rule, because a loose rule here reads "if (ready) Draw(" as
# a definition of Draw.
DEFINE = re.compile(
    r"(?m)^[ \t]*(?:(?:public|private|protected|internal|static|readonly|sealed|override|"
    r"virtual|async|partial|new|extern|unsafe|abstract|implicit|explicit|operator)\s+)*"
    r"(?:\(\s*[\w\.\?<>,\[\]\s]+\)|[\w\.\?]+(?:<[^()=;]*>)?(?:\[\s*\])?)"
    r"\s+(\w+)\s*(?:<[^>()]*>)?\s*\(")

# And a delegate in a variable is called like a method: Action paint = ...; paint();
DELEGATE = re.compile(
    r"\b(?:Action|Func|Predicate|Comparison|Converter|EventHandler|UnityAction)"
    r"(?:<[^()=;]*>)?\s+(\w+)\s*[=;,)]")
LAMBDA_VAR = re.compile(r"\bvar\s+(\w+)\s*=\s*[^;]*=>")

CALL = re.compile(r"(?<![\w\.])(\w+)\s*(?:<[^<>()]*>)?\s*\(")


def without_attributes(src):
    """Attribute blocks out: [Tooltip("...")] is not a call, and this project is full of
    them -- the tooltips are the documentation."""
    out, i, n = [], 0, len(src)
    while i < n:
        # a line whose first non-space character opens an attribute
        start = src.find("[", i)
        if start < 0:
            out.append(src[i:])
            break

        head = src.rfind("\n", 0, start) + 1
        if src[head:start].strip() != "":
            out.append(src[i:start + 1])
            i = start + 1
            continue

        depth, j = 0, start
        while j < n:
            if src[j] == "[":
                depth += 1
            elif src[j] == "]":
                depth -= 1
                if depth == 0:
                    break
            j += 1

        out.append(src[i:start])
        # keep the newlines so reported line numbers stay right
        out.append("\n" * src.count("\n", start, j))
        i = j + 1

    return "".join(out)


def defined_names(src):
    """Every method this file declares, including local functions and constructors."""
    out = {m.group(1) for m in DEFINE.finditer(src)}
    out |= {m.group(1) for m in DELEGATE.finditer(src)}
    out |= {m.group(1) for m in LAMBDA_VAR.finditer(src)}
    # constructors and type names: `class Foo` / `struct Foo` / `Foo(` inside it. The
    # `@` is optional because generated code writes `class @InputSystem_Actions` --
    # escaping a name that needs no escaping -- and its own constructor then reads as
    # a call to something nothing defines.
    out |= {m.group(1) for m in re.finditer(r"\b(?:class|struct|interface|enum)\s+@?(\w+)", src)}
    return out


def missing_calls(path, src):
    """Calls with no receiver that this file does not define."""
    here = defined_names(src)
    out, said = [], set()

    for m in CALL.finditer(src):
        name = m.group(1)
        if name in said or name in CONTROL or name in INHERITED or name in here:
            continue

        # `new Thing(...)` is a constructor, not a call to something this project owes a
        # definition for -- every BCL and Unity type arrives that way.
        before = src[:m.start()].rstrip()
        if before.endswith("new"):
            continue
        # A receiver, including one left at the end of the line above.
        if before.endswith((".", "?", "?.")):
            continue
        said.add(name)
        line = src.count("\n", 0, m.start()) + 1
        out.append(f"{path}({line}): calls {name}(), and this file does not define "
                   f"it. A method deleted with the block around it leaves exactly this: "
                   f"the file no longer compiles, Unity keeps the PREVIOUS build of the "
                   f"editor assembly, and the tool on the menu is the old one -- which "
                   f"reads as \"my change did nothing\".")
    return out


# ------------------------------------------------------------------ the sixth mistake
#
# THE SAME TYPE DECLARED TWICE. This is the mistake a merge makes rather than an edit, and
# it reported itself as two hundred errors: a generated `InputSystem_Actions.cs` committed
# in Assets/, and the .inputactions it was generated FROM with "generate C# class" switched
# back on, so Unity wrote its own copy next to the asset. Two files, one class, every member
# in it duplicated -- and no single error message says "the merge did this".
#
# A partial class spread over several files is legal and normal -- CFXR is written that way
# -- so a second declaration alone proves nothing. What proves it is the MEMBERS: two halves
# of one partial type that both define `Dispose` are not two halves, they are two copies.
# And the class that started this is partial, so "non-partial only" would have missed it.

TYPE_DECL = re.compile(
    r"(?m)^[ \t]*(?:\[[^\]]*\]\s*)*"
    r"((?:(?:public|private|protected|internal|static|sealed|abstract|partial|new|"
    r"readonly|unsafe|ref)\s+)*)"
    r"(class|struct|interface|enum|record)\s+@?(\w+)")

NAMESPACE = re.compile(r"(?m)^\s*namespace\s+([\w\.]+)")


def body_of(src, at):
    """The text between the braces of the declaration starting at `at`."""
    start = src.find("{", at)
    if start < 0:
        return ""
    depth = 0
    for i in range(start, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[start + 1:i]
    return src[start + 1:]


def members_of(body):
    """The methods declared DIRECTLY in a type body.

    Depth matters: run the pattern over a whole class and it reads every `if (` and every
    local function inside every method as a member of the class. Both of CFXR's halves have
    an `if` in them, which is not a redeclaration of anything.
    """
    flat, depth = [], 0
    for line in body.split("\n"):
        if depth == 0:
            flat.append(line)
        depth += line.count("{") - line.count("}")
        if depth < 0:
            depth = 0
    return {m.group(1) for m in DEFINE.finditer("\n".join(flat))} - CONTROL - INHERITED


def declared_types(src):
    """The TOP-LEVEL types a file declares: name -> (line, partial?, member names).

    Top level only, by brace depth. A nested type is deliberately left out: the three
    builder windows each hold their own `Asset` struct, which is three legal types with one
    name, and a rule that cannot tell nesting apart fails the project for its own habits.
    A nested collision can only happen if the outer types collide too, and that is the line
    worth printing anyway.
    """
    ns = NAMESPACE.search(src)
    prefix = (ns.group(1) + ".") if ns else ""
    floor = 1 if ns else 0        # the depth a top-level type sits at

    out = {}
    for m in TYPE_DECL.finditer(src):
        head = src[:m.start()]
        if head.count("{") - head.count("}") != floor:
            continue
        name = prefix + m.group(3)
        if name in out:
            continue
        body = body_of(src, m.end())
        members = members_of(body)
        out[name] = (src.count("\n", 0, m.start()) + 1, "partial" in m.group(1), members)
    return out


def assembly_of(path, asmdef_dirs):
    """Which compilation a file lands in.

    Two types of one name are only an error inside one assembly, and this project has
    several: every folder with an .asmdef is its own, and Unity splits the rest into the
    runtime assembly and the Editor one by whether the path has an Editor folder in it.
    """
    best = ""
    for d in asmdef_dirs:
        if path.startswith(d) and len(d) > len(best):
            best = d
    if best:
        return best
    parts = path.replace("\\", "/").split("/")
    return "Assembly-CSharp-Editor" if "Editor" in parts else "Assembly-CSharp"


def duplicate_types(sources, asmdef_dirs):
    """One type, two files, one assembly -- and, for partials, the same members twice."""
    where = {}
    for path, src in sorted(sources.items()):
        asm = assembly_of(path, asmdef_dirs)
        for name, (line, is_partial, members) in \
                declared_types(without_attributes(strip(src))).items():
            where.setdefault((asm, name), []).append((path, line, is_partial, members))

    out = []
    for (asm, name), places in sorted(where.items()):
        if len(places) < 2:
            continue

        said = ", ".join(f"{p}({n})" for p, n, _pa, _me in places)

        if any(not is_partial for _p, _n, is_partial, _me in places):
            out.append(f"{name} is declared in {len(places)} files of the same assembly "
                       f"({asm}) and is not partial -- {said}. CS0101, and C# then reports "
                       f"it once per MEMBER as well, which is how a two-file mistake "
                       f"arrives as a screen of errors.")
            continue

        # Every declaration is partial, which is legal -- unless the halves overlap.
        shared = set.intersection(*[me for _p, _n, _pa, me in places])
        if len(shared) >= 2:
            few = ", ".join(sorted(shared)[:4])
            out.append(f"{name} is partial across {len(places)} files of the same assembly "
                       f"({asm}) and {len(shared)} member(s) are defined in more than one "
                       f"of them ({few}...) -- {said}. Two halves of a partial type do not "
                       f"both define the same method: this is one file copied, usually "
                       f"generated code committed next to the asset that generates it.")
    return out


def main():
    # Two sets, on purpose. `sources` is the code this project is answerable for, and the
    # per-file rules speak only about it. `every` is EVERY .cs under Assets, vendor code
    # included, because a duplicate type does not care who wrote the other copy: the one
    # that started this rule was generated into _Leftovers, a folder the per-file rules
    # skip, and a check that skipped it too would have found nothing.
    sources, every, asmdefs = {}, {}, []
    base = os.path.join(ASSETS, "..", "..")

    for root, dirs, names in os.walk(ASSETS):
        rel_root = os.path.relpath(root, base)
        for f in sorted(names):
            if f.endswith(".asmdef"):
                asmdefs.append(rel_root + os.sep)
            if not f.endswith(".cs"):
                continue
            path = os.path.relpath(os.path.join(root, f), base)
            text = io.open(os.path.join(root, f), encoding="utf-8",
                           errors="replace").read()
            every[path] = text
            if not (set(path.replace("\\", "/").split("/")) & SKIP):
                sources[path] = text

    fields = struct_fields(sources)

    problems, files = duplicate_types(every, asmdefs), len(sources)
    for path, src in sorted(sources.items()):
        problems += unclosed_strings(path, src)
        problems += brackets(path, src)
        problems += duplicate_locals(path, strip(src))
        problems += unassigned_struct_fields(path, strip(src), fields)
        problems += missing_calls(path, without_attributes(strip(src)))

    for p in problems:
        print("  " + p)
    print(f"cs_check: {'FAIL' if problems else 'ok'} "
          f"({files} files, {len(problems)} problem(s))")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
