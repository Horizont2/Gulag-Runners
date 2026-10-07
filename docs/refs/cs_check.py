"""The compiler checks this project cannot run.

There is no Unity and no .NET in this environment, so every C# edit here is a text edit and
the first thing that reads it for real is Unity, in front of Vlad. These are the two mistakes
a hand edit actually makes, and both are findable from the text:

  * brackets that do not balance, which is what a bad splice looks like;
  * a local declared twice in one scope, which is what inserting a block into the middle of a
    method looks like. CS0128 — it has happened, in LootMotor, where a `Fix centre` added near
    the top of a method shadowed the `FixVec2 centre` the end of it already used, and took the
    call that used it down with it.

Neither is a type check: this cannot tell you a method does not exist. It tells you the edit
is at least well-formed, which is the part a careful patch can get wrong without noticing.
"""
import io
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                    "Gulag runners", "Assets", "_Game")

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


def main():
    problems, files = [], 0
    for root, _, names in os.walk(ROOT):
        for f in sorted(names):
            if not f.endswith(".cs"):
                continue
            path = os.path.relpath(os.path.join(root, f), os.path.join(ROOT, "..", "..", ".."))
            src = io.open(os.path.join(root, f), encoding="utf-8").read()
            files += 1
            problems += brackets(path, src)
            problems += duplicate_locals(path, strip(src))

    for p in problems:
        print("  " + p)
    print(f"cs_check: {'FAIL' if problems else 'ok'} "
          f"({files} files, {len(problems)} problem(s))")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
