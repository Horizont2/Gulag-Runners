#!/usr/bin/env python3
"""
stale_members.py — `thing.Member` where the type has no such member any more. CS1061.

This is the single most repeated way a change breaks this project, and it broke it three
times in one afternoon:

    recorder.rateAgainstBots   renamed to practiceCountsOnTheLadder
    pool.SurfaceY              a property removed in favour of the method already there
    view.sparksOnBlock         removed with the block mechanic

Every one compiled fine as text -- brackets balanced, strings closed, nothing duplicated --
and every one was found by Unity, minutes later, in front of the person waiting to build.
There is no compiler here, so a rename across two hundred files is a grep and a hope.

What this does instead: for each file, find the variables and fields whose type is one this
project declares, then check every `name.Member` against what that type actually has,
inherited project base types included.

Where it stays quiet, on purpose:

  * anything reached through a type this project does not declare -- Unity's own, the
    standard library, a package. It cannot know those, so it says nothing about them.
  * any project type that DERIVES from one of those. A MonoBehaviour carries Unity's whole
    surface, and no list kept here can stay level with it: a first version kept one and was
    still wrong about EditorWindow.minSize, which is as ordinary a property as exists. So
    those types are skipped entirely.

    That costs the check a real case -- MatchRecorder.rateAgainstBots was a MonoBehaviour
    field and this would not have caught it -- and it is still the right trade. What is left
    is the simulation: structs and plain classes whose every member is declared in this
    repository, where the answer is not a guess. Ten false alarms in code nobody had touched
    is how a check gets switched off; being right about less is how it stays on.
  * `var`, lambda parameters, and anything whose type cannot be read from the declaration.
    A guess here is a false alarm, and a check that cries wolf is a check people stop
    reading. A name that wears two types in one file -- `feet` is an Aabb in one method of
    PlayerMotor and an `in FixVec2` parameter in another -- is dropped for the same reason.
  * a member used by its BARE name inside the type that declares it. This only follows
    `something.Member`, so removing `Puzzle.Ordered` while `Puzzle` itself still says
    `if (Ordered)` goes unseen here. That one needs no help: it is a plain undefined name
    and the first thing any compiler says.

So it is not a compiler and does not pretend to be. It catches the one mistake that keeps
happening, which is worth more than completeness.

Run: python3 stale_members.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SKIP = {"ThirdParty", "_Leftovers", "Plugins", "Packages", "TextMesh Pro"}

# What a MonoBehaviour, ScriptableObject or Object already carries. A project type deriving
# from one of these has all of it, and none of it is declared in this repository.
UNITY = {
    "transform", "gameObject", "name", "tag", "enabled", "isActiveAndEnabled", "hideFlags",
    "useGUILayout", "runInEditMode", "destroyCancellationToken", "didStart", "didAwake",
    "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren",
    "GetComponentInParent", "GetComponentsInParent", "TryGetComponent", "CompareTag",
    "SendMessage", "SendMessageUpwards", "BroadcastMessage", "Invoke", "InvokeRepeating",
    "CancelInvoke", "IsInvoking", "StartCoroutine", "StopCoroutine", "StopAllCoroutines",
    "Equals", "GetHashCode", "ToString", "GetType", "GetInstanceID", "SetDirty",
    "Reset", "OnValidate", "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
    # UnityEvent, which half the views hold
    "AddListener", "RemoveListener", "RemoveAllListeners", "Invoke", "GetPersistentEventCount",
    # the common collection surface, since fields are often List<T> of a project type
    "Count", "Length", "Add", "Remove", "Clear", "Contains", "IndexOf", "Insert", "Sort",
    "ToArray", "ToList", "Item",
}

# Any `Type name` at all -- declaration, field or parameter, this project's types and
# everyone else's. Used only to notice that one name wears two types in a file.
ANY_DECL = re.compile(r'\b(?:in|ref|out|params)?\s*\b([A-Z]\w*(?:<[^<>()]*>)?)\s+([a-z_]\w*)\s*(?=[,;=){])')

DECL_TYPE = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|private|protected|internal|static|readonly|const|new|sealed|override|'
    r'virtual|abstract|partial|unsafe|async|\s)*'
    r'\b([A-Z]\w*)\s+([a-z_]\w*)\s*(?:=|;|,|\))')

MEMBER_DECL = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|private|protected|internal)\s+'
    r'(?:static\s+|readonly\s+|const\s+|sealed\s+|override\s+|virtual\s+|abstract\s+|new\s+|'
    r'extern\s+|partial\s+|unsafe\s+|async\s+|event\s+)*'
    # The type. A generic may hold SPACES -- `Action<byte[], int>` is one type, and a
    # character class with no room for the space after the comma stopped matching there,
    # so every event declared with one looked like no member at all and its users looked
    # like they were reaching for something removed.
    r'(?:[A-Za-z_][\w\[\]\.\?]*(?:<[^<>]*(?:<[^<>]*>)?[^<>]*>)?)\s+'
    r'([A-Za-z_]\w*)\s*(?:\(|=>|[;={])')

# The rest of `public Fix MinX, MinY, MaxX, MaxY;` after the first name.
MULTI = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|private|protected|internal)\s+'
    r'(?:static\s+|readonly\s+|const\s+|new\s+|volatile\s+|\s)*'
    r'(?:[A-Za-z_][\w<>,\[\]\.\?]*)\s+'
    r'([A-Za-z_]\w*)\s*(?:=[^,;]*)?\s*,\s*([^;]+);')

# A property whose BRACE IS ON THE NEXT LINE. MEMBER_DECL wants a `(`, `=>`, `=`, `;` or
# `{` straight after the name, and this style ends the line there instead:
#
#     public float EffectiveViewHeight
#     {
#         get => ...
#
# Six of them went uncollected -- EffectiveViewHeight, Flooded, SuggestedDelay,
# ExternallyClocked and two more -- and every file that read one looked like it was
# reaching for something removed.
OPEN_NEXT_LINE = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|private|protected|internal)\s+'
    r'(?:static\s+|readonly\s+|sealed\s+|override\s+|virtual\s+|abstract\s+|new\s+|'
    r'extern\s+|partial\s+|unsafe\s+)*'
    r'(?:[A-Za-z_][\w\[\]\.\?]*(?:<[^<>]*(?:<[^<>]*>)?[^<>]*>)?)\s+'
    r'([A-Za-z_]\w*)\s*$')

# And a field with no access modifier at all, which C# makes private and this project
# writes a lot of: `Image _black;`, `float _hold;`. Only ever ADDS a name to a type's
# surface, so a loose match here makes this check quieter, never noisier.
IMPLICIT_FIELD = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:static\s+|readonly\s+|const\s+|volatile\s+)*'
    r'(?:[A-Za-z_][\w\[\]\.\?]*(?:<[^<>]*(?:<[^<>]*>)?[^<>]*>)?)\s+'
    r'([A-Za-z_]\w*)\s*(?:=[^;]*)?;\s*$')

TYPE_DECL = re.compile(
    r'^\s*(?:public|internal|private|protected|static|sealed|abstract|partial|\s)*'
    r'(class|struct|interface|enum)\s+([A-Za-z_]\w*)\s*(?::\s*([^{\n]+))?')


def strip(src):
    """Comments and string bodies out, so neither can look like code."""
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    src = re.sub(r'//[^\n]*', '', src)
    src = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', src)
    src = re.sub(r"'(?:\\.|[^'\\\n])'", "''", src)
    return src


def sources():
    out = {}
    base = os.path.join(ASSETS, "..", "..")
    for root, dirs, names in os.walk(ASSETS):
        if set(os.path.relpath(root, base).replace("\\", "/").split("/")) & SKIP:
            continue
        for f in sorted(names):
            if not f.endswith(".cs"):
                continue
            path = os.path.relpath(os.path.join(root, f), base)
            with open(os.path.join(root, f), encoding="utf-8", errors="replace") as h:
                out[path] = h.read()
    return out


def types_of(files):
    """
    Every type this project declares, with its members and its base types.

    The body's depth is learnt from the brace that OPENS it rather than guessed when the
    declaration is read. The first version guessed depth+1 and pushed it, and the very same
    line's "pop anything deeper than we are" then threw it straight back off -- because the
    opening brace is usually on the NEXT line, so depth had not risen yet. Every type was
    found and not one member was collected, and the check reported ok on nothing at all.
    """
    members, bases, kinds = {}, {}, {}

    for path, raw in files.items():
        src = strip(raw)
        depth, stack, waiting = 0, [], None

        for line in src.split("\n"):
            t = TYPE_DECL.match(line)
            if t:
                kind, name, base = t.group(1), t.group(2), t.group(3) or ""
                kinds[name] = kind
                members.setdefault(name, set())
                bases[name] = [b.strip().split("<")[0]
                               for b in base.split(",") if b.strip()]
                waiting = name

            # A member belongs to the innermost type whose body we are directly inside.
            if stack and depth == stack[-1][1]:
                owner = stack[-1][0]
                m = MEMBER_DECL.match(line)
                if m:
                    members[owner].add(m.group(1))
                for other in (OPEN_NEXT_LINE, IMPLICIT_FIELD):
                    hit = other.match(line)
                    if hit:
                        members[owner].add(hit.group(1))

                # `public Fix MinX, MinY, MaxX, MaxY;` is four fields, and this is its own
                # test rather than a continuation of the one above -- MEMBER_DECL wants a
                # `;`, `=`, `(` or `=>` straight after the name and finds a COMMA, so it
                # does not match at all. Nesting this inside its success meant it never ran,
                # every name but the first went uncollected, and the check raised a hundred
                # and seventy false alarms about code nobody had touched.
                tail = MULTI.match(line)
                if tail:
                    for extra in (tail.group(1) + "," + tail.group(2)).split(","):
                        extra = extra.strip().split("=")[0].strip()
                        if re.match(r"^[A-Za-z_]\w*$", extra):
                            members[owner].add(extra)
                if kinds.get(owner) == "enum":
                    for e in re.findall(r"^\s*([A-Za-z_]\w*)\s*(?:=|,|$)", line):
                        members[owner].add(e)

            for ch in line:
                if ch == "{":
                    depth += 1
                    if waiting is not None:
                        stack.append((waiting, depth))
                        waiting = None
                elif ch == "}":
                    if stack and depth == stack[-1][1]:
                        stack.pop()
                    depth -= 1

    return members, bases, kinds


def surface(name, members, bases, seen=None):
    """Everything a type has, its project base types included."""
    seen = seen or set()
    if name in seen or name not in members:
        return set()
    seen.add(name)
    out = set(members[name])
    for b in bases.get(name, []):
        out |= surface(b, members, bases, seen)
    return out


# The one foreign base this check will reason about. A MonoBehaviour's own surface is
# small, fixed and already written out in UNITY above, and nearly every view and component
# in this project is one -- so skipping them cost the check the commonest case there is.
# It cost it twice in a week: `view.sparksOnBlock` when the block went, and
# `speaks.waiting` when the disconnect notice went. Both were a field removed from a
# MonoBehaviour while a writer stayed behind, and both were found by Unity in front of
# somebody waiting to build.
#
# Nothing else is let through. EditorWindow, Editor, ScriptableObject, Graphic and the UI
# behaviours each carry a surface no list here can stay level with -- the first version of
# this check kept one and was still wrong about EditorWindow.minSize.
KNOWN_BASES = {"MonoBehaviour"}


def foreign_bases(name, bases, members, seen=None):
    """The base types this project does not declare, anywhere up the chain."""
    seen = seen or set()
    if name in seen:
        return set()
    seen.add(name)
    out = set()
    for b in bases.get(name, []):
        if b not in members:
            out.add(b)
        else:
            out |= foreign_bases(b, bases, members, seen)
    return out


def unity_backed(name, bases, members, seen=None):
    """True when the type descends from a foreign type this check cannot account for."""
    return bool(foreign_bases(name, bases, members) - KNOWN_BASES)


def main():
    files = sources()
    members, bases, kinds = types_of(files)
    problems = []
    checked = 0

    for path, raw in sorted(files.items()):
        src = strip(raw)

        # Which local or field holds which project type, in this file.
        #
        # A name declared with TWO different types somewhere in one file is dropped rather
        # than resolved. `feet` is an Aabb in one method of PlayerMotor and a FixVec2 in
        # another; `m` is a MatchSimState here and a Material there. Scoping by method
        # would settle it, but a check that says nothing where it cannot be sure is worth
        # more than one that picks the first declaration and reports twenty faults in code
        # nobody has touched -- which is what the first version did.
        # Ambiguity is judged against EVERY type, not only this project's. `feet` is an
        # Aabb in one method of PlayerMotor and an `in FixVec2` parameter in another; `m` is
        # a MatchSimState here and a Material there. Looking only at project types missed
        # both, because the other declaration was a parameter or a Unity type -- so the name
        # looked unambiguous and twenty faults were reported in code nobody had touched.
        seen_as = {}
        for m_ in ANY_DECL.finditer(src):
            seen_as.setdefault(m_.group(2), set()).add(m_.group(1))

        # A lambda's parameters carry no type at all -- `_gates.Sort((a, b) => a.order...)`
        # -- so `a` there is whatever the list holds and nothing in the line says what. Any
        # name used as one is marked unresolvable, which is how this stops reporting that an
        # Aabb has no 'order'.
        for lam in re.finditer(r"\(([^()]*)\)\s*=>", src):
            for name in lam.group(1).split(","):
                name = name.strip().split()[-1] if name.strip() else ""
                if re.match(r"^[a-z_]\w*$", name):
                    seen_as.setdefault(name, set()).add("<lambda>")

        holds = {v: next(iter(t)) for v, t in seen_as.items()
                 if len(t) == 1
                 and next(iter(t)) in members
                 and kinds.get(next(iter(t))) != "enum"
                 and not unity_backed(next(iter(t)), bases, members)}

        if not holds:
            continue

        for n, line in enumerate(src.split("\n"), 1):
            for var, type_ in holds.items():
                for m in re.finditer(r'\b%s\s*\.\s*([A-Za-z_]\w*)' % re.escape(var), line):
                    used = m.group(1)
                    if used in UNITY:
                        continue
                    have = surface(type_, members, bases)
                    if not have:
                        continue
                    checked += 1
                    if used in have:
                        continue

                    problems.append(
                        "%s:%d: %s is a %s and has no '%s'. Renamed or removed?"
                        % (path, n, var, type_, used))

    if problems:
        seen = set()
        for line in problems:
            if line in seen:
                continue
            seen.add(line)
            print("stale_members: " + line)
        print("stale_members: %d problem(s)" % len(seen))
        return 1

    print("stale_members: ok (%d member access(es) checked against the type that owns them)"
          % checked)
    return 0


if __name__ == "__main__":
    sys.exit(main())
