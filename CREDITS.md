# Credits — third-party asset attribution

Start filling this in with the very first pack you download. Later on nobody remembers
where anything came from.

## Entry format

```
### <Pack name> — <Author>
Source:      <URL>
Licence:     CC0 / CC BY 4.0 / Unity Asset Store EULA
Location:    Assets/_ThirdParty/<folder>
In repo:     yes / no (why)
Used for:    <short description>
```

## Rules

- **CC0** — attribution is not required, but record it anyway so the origin is traceable.
  Safe to commit to the repository.
- **CC BY** — attribution **is required**: the author and a link must appear in the game
  credits.
- **Unity Asset Store (including free packs)** — you may ship the pack inside the game but
  you may not redistribute it. Those packs are **not committed**: they are covered by
  `.gitignore`, and this file records what each person has to install by hand when setting
  the project up.

See `docs/10-free-assets.md` for the shortlist of packs chosen per biome.

## Assets

<!-- Add an entry here for every pack you download. -->

### Free Pack - Stick Man — PolyOne
Source:      Unity Asset Store (publisher PolyOne)
Licence:     Unity Asset Store EULA
Location:    Assets/PolyOne/Free Stickman
In repo:     yes — see the note below
Used for:    player character model, rig and the eight animation clips
             (Idle, Walk, Run, Run Fast, Jumping Up, Sitting, Swimming, Yelling)

> ⚠️ The Asset Store EULA lets you ship a pack inside your game but does not let you
> redistribute it, and committing it to a repository is redistribution. It is currently
> committed. Two ways to settle this: keep it only if this repository stays private and
> every person with access holds their own Asset Store licence for the pack, or remove it
> from git (`git rm -r --cached "Gulag runners/Assets/PolyOne"`, add it to `.gitignore`)
> and leave this entry so everyone installs it themselves. A CC0 character pack —
> KayKit Adventurers or Quaternius — would avoid the question entirely; see
> docs/10-free-assets.md.
