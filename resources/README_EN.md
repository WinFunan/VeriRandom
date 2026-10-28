<div align="center">

<img src="secrandom-icon-paper.png" width="128" height="128" alt="VeriRandom" />

# VeriRandom

**A random-selection tool for classrooms and teams, with configurable workflows, managed history, and verifiable draw records.**

[![GitHub Issues](https://img.shields.io/github/issues-search/WinFunan/VeriRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=Issues)](https://github.com/WinFunan/VeriRandom/issues)
[![Latest Release](https://img.shields.io/github/v/release/WinFunan/VeriRandom?style=for-the-badge&color=00b4ab&label=Latest%20Release)](https://github.com/WinFunan/VeriRandom/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/WinFunan/VeriRandom?include_prereleases&style=for-the-badge&label=Pre-release)](https://github.com/WinFunan/VeriRandom/releases)
[![Last Update](https://img.shields.io/github/last-commit/WinFunan/VeriRandom?style=for-the-badge&color=00b4ab&label=Last%20Update)](https://github.com/WinFunan/VeriRandom/commits/master)
[![Downloads](https://img.shields.io/github/downloads/WinFunan/VeriRandom/total?style=for-the-badge&color=00b4ab&label=Downloads)](https://github.com/WinFunan/VeriRandom/releases)

[![QQ Group](https://img.shields.io/badge/-QQ%20Group%20%7C%20768421833-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/EvhyCJWqCA)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](../LICENSE)

**Language** [ [简体中文](../README.md) | **English** | [日本語](README_JA.md) ]

</div>

> [!IMPORTANT]
> This product is an open-source fork of SecRandom and must not be regarded as SecRandom itself

> [!NOTE]
> VeriRandom is released under GNU GPLv3. You may modify and redistribute the source, but derivative redistributions must also use GNU GPLv3.

## VeriRandom

VeriRandom is a fair random-selection application for classrooms, teams, events, decision-making, and other scenarios.

## Fork-specific features

These are the capabilities this fork adds on top of upstream SecRandom. The sections after this one describe behaviour both projects share.

1. **NIST Beacon entropy and Up/Own dual-chain proofs** — an authoritative external beacon can seed a draw, so the operator cannot inject entropy of their own; Up-chain nodes stay readable after a migration to upstream.
2. **Proof trust scoring** — a built-in evidence grader takes the sources the operator trusts (chain integrity, timestamp, server receipt, human time attestation) and produces an explainable report instead of leaving trust to judgement.
3. **TSA timestamping can be switched off** — turning it off raises a dialog explaining how the beacon period anchor depends on it; the privacy page discloses that a time-stamp request sends de-identified data to WoTrus.
4. **Cross-border transfer gate, nothing uploaded by default** — every path that could send data to SECTL goes through one gate, and uploading a draw to the upstream witness service is off unless it is turned on.
5. **More conservative privacy defaults** — telemetry and online status are both off on a fresh install.
6. **Detached configuration signing** — the private key stays on the operator's trusted device while the draw machine holds only the public key, so a tampered policy is refused at startup; the public key fingerprint is shown for comparison.
7. **TOTP alone is disabled** — a TOTP code can no longer be the only factor; it must pair with a password or a USB key, and leftover standalone seeds are cleaned up on upgrade.
8. **Independent product identity** — its own program name, install directory, `verirandom://` scheme, single-instance name and installer id, so it coexists with SecRandom on one machine; the legacy `secrandom://` scheme is opt-in.
9. **Two quick-draw refinements** — focus returns to the previous window after the quick-draw closes, and beacon pulses can be prefetched to remove the network pause.
10. **Proof replay** — replays a proof locally against a roster you supply and compares the recomputed names with what was displayed, which upstream verification does not do.
11. **Raw random number page** — generates a raw number inside a range and immediately writes its own proof record, and warns on the spot when a source that cannot be re-checked later (random.org, the local CSPRNG) is mixed in.
12. **Courseware markers** — a QR code pasted onto a slide starts the draw with the parameters it carries when that slide is shown, so a presentation clicker only has to turn pages.
13. **Proof details** — shows the same public parameters a server-side verification page shows, under a notice that they are what the file claims rather than a conclusion checked locally.
14. **Resizable result card** — the floating result card can be dragged and resized from any edge or corner, remembers its size, and works with touch.

## Features

### Draw workflows

- **Roll call**: Supports standard random, history-balanced, and repeat-control draws.
- **Quick draw**: Quickly draws students through a standalone floating window.
- **Lottery**: Supports prize-wheel and inventory draws, with students and prizes managed independently.
- **Rich presentation**: Provides unified settings for animation, results, speech, music, and notifications, with fallback when a notification fails.
- **Courseware markers**: Paste an application-generated QR code onto a slide and the draw starts by itself when that slide is shown, so a presentation clicker only has to turn the page. The list, count, timing (a delay, or waiting for the next button press), animation and speech settings all travel inside the code, which is why the same deck works on another computer; one click on "start a new lesson" re-arms everything, and one slide fires once per lesson. The screen is read once after a key press, never while idle, and no frame is stored or sent anywhere.
- **Resizable result card**: The floating result card can be dragged and resized from any edge or corner, remembers the size you left it at, and accepts touch as well as a mouse.

### Fairness and list management

- Dynamically adjusts weights using history count, draw interval, group, gender, and other factors to reduce repeats and distribution imbalance.
- Uses stable internal identifiers to preserve history; student numbers, IDs, and names are display information only.
- Supports multiple student lists, prize pools, and `.xlsx`, `.xls`, `.csv` import, mapping, and preview.
- Saves history for every draw round for convenient review.

### Reviewable draw results

- Every draw automatically saves a proof record file.
- The random-number page generates a raw number inside the chosen range and immediately writes its own proof record; the value can be recomputed later from the source material that record stores.
- Selecting a proof in the replay window shows the same public parameters a server-side verification page shows: input and audit commitments, random material, pool rule, sampling, average-gap protection, and the share-debt parameters. The dialog says plainly at the top that these are what the proof file itself claims, and that trust has to come from the evidence grader and the replay result.
- You can choose to involve the server in and witness the draw process.
- Draw results can be checked again through official channels.

### Data, privacy, and security

- Settings, lists, and history can all be imported, exported, backed up, and restored.
- Backups may include lists, history, draw proofs, images, and audio, but never passwords or other security information.
- Password, TOTP, or USB-drive protection can secure important operations, and you can choose which operations require verification.

### Verification boundaries

| Mode | What it can do | What it cannot prove |
|---|---|---|
| Offline proof | Review a completed draw process; later edits, deletions, and a wiped-and-rebuilt chain are all detectable | It cannot prove that the local program, random seed, or roster was unchanged **before** the draw, nor rule out repeated trials and selection before it |
| Online witnessing | Protect the draw flow after the server locks it | It cannot prove that the roster is authentic, complete, or unfiltered before submission |

Notes:

- Every draw saves a locally replayable proof immediately and submits that same proof for server replay signing; its digest also goes to a third-party time-stamp authority (the digest only — never the roster or the draw content)
- Local proofs form an append-only hash chain, so deleting or rewriting one leaves a gap; the service remembers the chain head it has seen for each device, so wiping local proofs and rebuilding the chain is reported as a backwards chain position
- A proof commits a digest of the "record id to name/number/group" mapping of the roster it used, so editing the list **after** the draw to point a winning record at someone else is detectable
- Filtering the roster **before** the draw (deleting or disabling a student first) cannot be proven: the digest only covers the roster as it stood at draw time. Covering that would need a "confirm the roster with a third party before drawing" flow, which is not implemented

## Technical evolution

| Version | Stack | Stage |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | First desktop implementation |
| v2 | Python + PySide6 + qfluentwidgets | Qt stack evolution |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET desktop rewrite for continued draw, verification, and desktop-integration development |

## Download and updates

- [GitHub Releases](https://github.com/WinFunan/VeriRandom/releases) provides release packages and change notes for this fork.

## Upstream download and updates

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) provides upstream release packages and change notes.
- The [upstream official download page](https://stk.sectl.cn/SecRandom) provides the latest upstream download entry point.
- Automatic updates validate a signed release manifest and artifact length/hash before deployment. Refer to the package and notes supplied with each release for installation details.

## License and third-party notices

- VeriRandom is released under [GNU GPLv3](../LICENSE).
- See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for third-party components, copyright information, and distribution-review notes.
- History-balanced weights and candidate filters help reduce repeat selections and improve long-term distribution. They do not replace management of real-world rosters, rules, or processes, and VeriRandom does not claim to verify those conditions.

## Contributors and special thanks

<a href="https://github.com/WinFunan/VeriRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=WinFunan/VeriRandom" alt="VeriRandom contributors" />
</a>

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

Thank you to everyone who contributes code, reports issues, improves documentation, or provides feedback to VeriRandom and SecRandom. The avatars are generated from GitHub contributor data; select them to open the [this repository's GitHub contributors page](https://github.com/WinFunan/VeriRandom/graphs/contributors) or the [upstream GitHub contributors page](https://github.com/SECTL/SecRandom/graphs/contributors) for complete statistics.

## Support and community for this repository

- [QQ Group 768421833](https://qm.qq.com/q/EvhyCJWqCA)
- [Email](mailto:love-code-yeyixiao@outlook.com)
- [Bilibili](https://space.bilibili.com/510993086)
- [Report an issue](https://github.com/WinFunan/VeriRandom/issues)
- [English contributing guide](CONTRIBUTING_EN.md)

## Support and community for upstream

- [Support upstream on Afdian](https://afdian.com/a/lzy0983)
- [Email](mailto:lzy.12@foxmail.com)
- [QQ Group 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ Channel](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili](https://space.bilibili.com/520571577)
- [Report an issue](https://github.com/SECTL/SecRandom/issues)
- [SecRandom documentation](https://secrandom.sectl.cn/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [Simplified Chinese contributing guide](https://github.com/SECTL/SecRandom/CONTRIBUTING.md)


**Copyright © 2025-2026 WinFunan**

**Copyright © 2025-2026 SECTL**
