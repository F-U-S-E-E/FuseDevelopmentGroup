# Security Policy

## Supported Versions

Security fixes land on the latest release. FUSE targets the Railroader `2025.1.x`
line; older FUSE builds and other game versions are not patched.

## Reporting a Vulnerability

**Do not open a public issue for a security problem.**

Report it privately through GitHub's
[private vulnerability reporting](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/security/advisories/new)
on this repository. That creates a draft advisory only the maintainers can see.

Please include:

- What the issue is and what an attacker could do with it
- Steps to reproduce, or a minimal package that triggers it
- The FUSE version, Railroader version, and your mod list
- `FUSE.log` if it is relevant

You can expect an acknowledgement within a week. We will let you know whether the
report is accepted, and tell you when a fix ships. Credit is offered unless you
would rather stay anonymous.

## Scope

FUSE is a game mod. It loads third-party package content — JSON data, asset packs,
audio, and in some cases component assemblies — into the game process. The
interesting security questions are about what a **malicious or malformed package**
can do to someone who installs it.

In scope:

- A package that escapes its own folder when read, written, or installed — path
  traversal through package files, the converter, or the installer
- A package that causes FUSE to execute code it should not
- Crashes or hangs triggered by malformed package data that a validator should
  have caught
- Anything that lets one package tamper with another package's data or with the
  player's saves outside normal ownership rules

Out of scope:

- **A package assembly running code.** FUSE loads custom `IndustryComponent` types
  from installed assemblies by design. Installing a package that ships a `.dll`
  means running that code — that is the model, not a vulnerability. Install
  packages from sources you trust.
- Cheating, save editing, or achievement manipulation in a single-player game.
- Multiplayer desync from mismatched mod lists. FUSE does not sync package
  contents over the network and does not claim to; every player applies their own
  local stack. See [docs/FAQ.md](docs/FAQ.md#multiplayer).
- Bugs in Railroader, Unity Mod Manager, or third-party mods. Report those to
  their own maintainers.
- Vulnerabilities in the OpenStreetMap or Mapbox services the external editor
  fetches data from.

## For Package Authors And Players

A FUSE package is untrusted input to your game. Treat downloading one the way you
would treat downloading any other mod: get it from a source you trust, and be
aware that a package containing a component assembly runs code on your machine
when the game loads it.
