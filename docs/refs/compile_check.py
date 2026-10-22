"""Does this project actually compile?

Every other check here reasons about the code. This one builds it, with Roslyn, against the
real Unity reference assemblies — because the one failure none of the others can see is the
one that stops Unity dead, and three rounds of a blocked editor were spent arguing about a
compile error from the outside.

Unity is not installed here and never will be, but it does not have to be:

  * UnityEngine's reference assemblies are on NuGet (UnityEngine.Modules), which is the same
    API surface the editor compiles against;
  * two PACKAGES ship no reference assemblies — the UI event interfaces and the Input System —
    so unity_stubs/ stands in for exactly the surface this project touches, and nothing more;
  * the simulation assembly is engine-free by design (GulagRunners.Sim.asmdef sets
    noEngineReferences), so it compiles as plain C# with no stand-ins at all.

Assets/_Game/Editor is covered too, and the sentence that used to sit here -- "those scripts
are editor tooling and cannot break a build" -- was wrong. It cannot break a PLAYER build; it
breaks the EDITOR, which stops Unity compiling anything at all, which stops play mode, which
stops the game. One stale field in HudViewEditor.cs did exactly that. UnityEditor ships with
the editor rather than on NuGet, so unity_stubs/UnityEditorStubs.cs stands in for the seventy
or so members this project's tooling actually calls, extracted from the sources.

    cd docs/refs && python3 compile_check.py

Needs the .NET SDK and, the first time, network for one NuGet restore. Without them it says so
and stops rather than pretending the project is fine.
"""
import io, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
GAME = os.path.join(ROOT, "Gulag runners", "Assets", "_Game")

# The folders that make up the two real assemblies: the engine-free simulation, and everything
# in the default assembly that the game needs at runtime.
FOLDERS = ("Sim", "Presentation", "Audio", "Input", "Meta", "Menu", "Editor")

UNITY_PACKAGE = "UnityEngine.Modules"
UNITY_VERSION = "2021.3.33"      # reference assemblies only: the API this project uses is old
                                 # enough that the exact version is not the point, and a newer
                                 # one is not published


def dotnet():
    exe = shutil.which("dotnet")
    if exe is None:
        print("compile_check: no .NET SDK on this machine, so nothing was compiled.")
        print("  install one (apt-get install -y dotnet-sdk-8.0) and run this again.")
        return None
    return exe


def project(tmp, refs):
    """A class library made of this project's own sources plus the Unity references."""
    includes = "\n".join(
        f'    <Compile Include="{os.path.join(GAME, f)}/**/*.cs" />' for f in FOLDERS)
    stubs = f'    <Compile Include="{os.path.join(HERE, "unity_stubs")}/*.cs" />'
    references = "\n".join(
        f'    <Reference Include="{os.path.splitext(os.path.basename(d))[0]}">\n'
        f'      <HintPath>{d}</HintPath>\n    </Reference>' for d in refs)

    io.open(os.path.join(tmp, "check.csproj"), "w", encoding="utf-8").write(f"""\
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AssemblyName>GulagRunners.CompileCheck</AssemblyName>
    <NoWarn>CS0649;CS0414;CS0169;CS1701;CS1702;NU1701;CS0108;CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
{stubs}
{includes}
  </ItemGroup>
  <ItemGroup>
{references}
  </ItemGroup>
</Project>
""")


def unity_refs(exe, tmp):
    """The Unity reference assemblies, restored once and cached in ~/.nuget afterwards."""
    cache = os.path.expanduser(
        f"~/.nuget/packages/{UNITY_PACKAGE.lower()}/{UNITY_VERSION}/lib/net45")
    if not os.path.isdir(cache):
        print(f"compile_check: fetching {UNITY_PACKAGE} {UNITY_VERSION} (once)...")
        io.open(os.path.join(tmp, "get.csproj"), "w").write(
            f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            f'<TargetFramework>netstandard2.0</TargetFramework></PropertyGroup><ItemGroup>'
            f'<PackageReference Include="{UNITY_PACKAGE}" Version="{UNITY_VERSION}" />'
            f'</ItemGroup></Project>')
        r = subprocess.run([exe, "restore", os.path.join(tmp, "get.csproj")],
                           capture_output=True, text=True, timeout=600)
        if not os.path.isdir(cache):
            print("compile_check: could not fetch the Unity reference assemblies.")
            print((r.stderr or r.stdout)[-600:])
            return None
    return sorted(os.path.join(cache, f) for f in os.listdir(cache) if f.endswith(".dll"))


def main():
    exe = dotnet()
    if exe is None:
        return 2

    tmp = tempfile.mkdtemp(prefix="gr-compile-")
    try:
        refs = unity_refs(exe, tmp)
        if refs is None:
            return 2
        project(tmp, refs)

        env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        r = subprocess.run([exe, "build", os.path.join(tmp, "check.csproj"),
                            "-v", "q", "--nologo"],
                           capture_output=True, text=True, env=env, timeout=900)

        errors, seen = [], set()
        for line in (r.stdout + r.stderr).split("\n"):
            m = re.search(r'(.+?)\((\d+),(\d+)\): error (CS\d+): (.+?)( \[|$)', line)
            if not m:
                continue
            where = os.path.relpath(m.group(1).strip(), ROOT) \
                if os.path.isabs(m.group(1).strip()) else m.group(1).strip()
            key = (where, m.group(2), m.group(4))
            if key in seen:
                continue
            seen.add(key)
            errors.append(f"  {where}({m.group(2)},{m.group(3)}): {m.group(4)}: {m.group(5)}")

        for e in errors[:40]:
            print(e)
        if len(errors) > 40:
            print(f"  ... and {len(errors) - 40} more")

        files = sum(len([f for f in fs if f.endswith(".cs")])
                    for folder in FOLDERS
                    for _, _, fs in os.walk(os.path.join(GAME, folder)))
        print(f"compile_check: {len(errors)} error(s) in {files} file(s) "
              f"across {', '.join(FOLDERS)}")
        return 1 if errors else 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
