#!/usr/bin/env sh
# BattleLuck offline event validator. The server performs the same minimal
# contract checks in EventSchemaValidator, so jq/AJV are optional conveniences.
set -eu

ROOT="${1:-.}"
SCHEMA_DIR="${2:-config/BattleLuck/events/schemas}"
FILES="flow.json zones.json kits.json prompt.txt"

fail() { echo "ERROR[$1] $2" >&2; exit 1; }

[ -d "$ROOT" ] || fail EMISSINGDIR "event directory not found: $ROOT"
for file in $FILES; do
  [ -f "$ROOT/$file" ] || fail EMISSINGFILE "$file is missing"
done
[ -s "$ROOT/prompt.txt" ] || fail ESCHEMA "prompt.txt is empty"

if command -v jq >/dev/null 2>&1; then
  for file in flow.json zones.json kits.json; do
    jq empty "$ROOT/$file" >/dev/null 2>&1 || fail EJSONPARSE "$file is invalid JSON"
  done
else
  echo "WARN ETOOL jq is not installed; JSON syntax was not checked by the shell wrapper." >&2
fi

if command -v ajv >/dev/null 2>&1; then
  ajv validate -s "$SCHEMA_DIR/flow.schema.json" -d "$ROOT/flow.json" --strict=false
  ajv validate -s "$SCHEMA_DIR/zones.schema.json" -d "$ROOT/zones.json" --strict=false
  ajv validate -s "$SCHEMA_DIR/kits.schema.json" -d "$ROOT/kits.json" --strict=false
else
  echo "WARN ETOOL ajv is not installed; schema checks will run when the plugin stages the event." >&2
fi

echo "OK BattleLuck event files are present and ready for server-side validation."
