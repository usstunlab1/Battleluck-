# Sequence UUID catalog

`config/BattleLuck/sequences/uuid_catalog.json` is the executable catalog for
native `SequenceGUID` values. It is intentionally separate from the
KindredExtract allowlists.

- KindredExtract allowlists are reference candidates. They are not verified by
  merely appearing in a source export.
- `entries` starts empty and may contain only values confirmed on the target
  V Rising server with an in-game dump or an approved runtime capture.
- Keep unverified names and hashes in the source/reference notes until they are
  promoted with `verificationStatus: "in_game_verified"`, a UTC timestamp, and
  the dump source.
- Do not use a pending or reference-only value in a production sequence.

This separation prevents a large source catalog from being mistaken for a
verified UUID catalog when the game build changes.
