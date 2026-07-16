# BattleLuck

BattleLuck is a server-side BepInEx plugin for V Rising. It adds event-driven arena modes, controlled NPC encounters, player loadouts, progression, teleporting, schematics, and optional local AI tools.

## Install

1. Install BepInEx for V Rising on the dedicated server.
2. Install the package with a Thunderstore-compatible mod manager, or copy the package files into the server's `BepInEx` folder.
3. Start the server once, then edit `BepInEx/config/BattleLuck/*.json`.
4. Use `.bl.help` in game to see the commands available to your permission level.

AI is optional and local-first. It is disabled until a server owner configures a provider and explicitly enables the requested features.

## Included features

- Match-ready event flow for arena and custom V Rising events.
- NPC control, boss commands, generic actions, and safe action reachability checks.
- Player event sessions, loadouts, progression, death-prevention charges, and rollback snapshots.
- Teleport services, spatial points, borders, schematics, and verified data catalogs.
- Optional local LLM prompts for event and mod authoring with approval gates.

## Support

Maintainer: **coyoteq1 — Ahmadtllal**  
Discord support: <https://discord.gg/uJ2ehWv4gR>

## Documentation

- [User guide](docs/user/README.md)
- [Developer guide](docs/developer/README.md)
- [AI and prompt guide](docs/LLM_GUIDE.md)
- [Publishing checklist](docs/PUBLISHING_CHECKLIST.md)
- [V Rising Mod Wiki](https://wiki.vrisingmods.com/)
- [V Rising Mod Wiki: Thunderstore upload](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html)
- [V Rising Mod Wiki: licensing](https://wiki.vrisingmods.com/dev/licensing.html)

## License

BattleLuck is licensed under the GNU Affero General Public License, version 3 or any later version. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
