#!/usr/bin/env bash
set -euo pipefail
shopt -s nocasematch

EVENT_ROOT="${1:-config/BattleLuck/events}"
SCHEMA_ROOT="${2:-config/BattleLuck/events/schemas}"
ALLOWLIST_ROOT="${3:-docs/audit/systems/allowlists}"

command -v jq >/dev/null 2>&1 || { echo "E_TOOL jq is required" >&2; exit 2; }
command -v ajv >/dev/null 2>&1 || { echo "E_TOOL ajv-cli is required" >&2; exit 2; }

printf '| event | file/path | code | detail |\n|---|---|---|---|\n'
failures=0
report() {
  local event="$1" file="$2" code="$3" detail="$4"
  printf '| %s | %s | %s | %s |\n' "$event" "$file" "$code" "${detail//|/\\|}"
  failures=$((failures + 1))
}

systems="$ALLOWLIST_ROOT/systems.json"
components="$ALLOWLIST_ROOT/components.json"
prefabs="$ALLOWLIST_ROOT/prefabs.json"
for required in "$systems" "$components" "$prefabs" "$SCHEMA_ROOT/flow.schema.json" "$SCHEMA_ROOT/zones.schema.json" "$SCHEMA_ROOT/kits.schema.json"; do
  [ -f "$required" ] || { echo "E_TOOL missing validator input: $required" >&2; exit 2; }
done

for reference_allowlist in "$systems" "$components" "$prefabs"; do
  if ! jq -e '.verifiedInGame == true' "$reference_allowlist" >/dev/null 2>&1; then
    echo "Reference-only allowlist (not in-game verified): $reference_allowlist" >&2
  fi
done

UUID_CATALOG="config/BattleLuck/sequences/uuid_catalog.json"
if [ -f "$UUID_CATALOG" ]; then
  if ! jq empty "$UUID_CATALOG" >/dev/null 2>&1; then
    echo "E_UUID_CATALOG invalid JSON: $UUID_CATALOG" >&2
    exit 1
  fi
  if jq -e '.entries[]? | select(.verificationStatus != "in_game_verified")' "$UUID_CATALOG" >/dev/null 2>&1; then
    echo "E_UUID_UNVERIFIED sequence UUID catalog contains an entry without in_game_verified status" >&2
    exit 1
  fi
fi

mapfile -t system_names < <(jq -r '.entries[]' "$systems")
mapfile -t component_names < <(jq -r '.entries[]' "$components")
mapfile -t prefab_names < <(jq -r '.entries[]' "$prefabs")
in_list() {
  local needle="$1"; shift
  local candidate
  for candidate in "$@"; do
    [ "$candidate" = "$needle" ] && return 0
  done
  return 1
}

for directory in "$EVENT_ROOT"/*; do
  [ -d "$directory" ] || continue
  event="$(basename "$directory")"
  [ "$event" = "schemas" ] && continue

  for file in flow.json zones.json kits.json; do
    path="$directory/$file"
    if [ ! -s "$path" ]; then
      report "$event" "$file" "EMISSINGFILE" "file is missing or empty"
      continue
    fi
    if ! jq empty "$path" >/dev/null 2>&1; then
      report "$event" "$file" "EJSONPARSE" "invalid JSON"
      continue
    fi
  done
  if [ ! -s "$directory/prompt.txt" ]; then
    report "$event" "prompt.txt" "EMISSINGFILE" "file is missing or empty"
  fi

  [ -s "$directory/flow.json" ] && ajv validate -s "$SCHEMA_ROOT/flow.schema.json" -d "$directory/flow.json" --strict=false >/dev/null 2>&1 || {
    [ -s "$directory/flow.json" ] && report "$event" "flow.json" "ESCHEMA" "flow schema validation failed"
  }
  [ -s "$directory/zones.json" ] && ajv validate -s "$SCHEMA_ROOT/zones.schema.json" -d "$directory/zones.json" --strict=false >/dev/null 2>&1 || {
    [ -s "$directory/zones.json" ] && report "$event" "zones.json" "ESCHEMA" "zones schema validation failed"
  }
  [ -s "$directory/kits.json" ] && ajv validate -s "$SCHEMA_ROOT/kits.schema.json" -d "$directory/kits.json" --strict=false >/dev/null 2>&1 || {
    [ -s "$directory/kits.json" ] && report "$event" "kits.json" "ESCHEMA" "kits schema validation failed"
  }

  for file in flow.json zones.json kits.json; do
    path="$directory/$file"
    [ -s "$path" ] || continue
    while IFS=$'\t' read -r jsonpath value; do
      [ -n "$value" ] || continue
      if ! in_list "$value" "${system_names[@]}"; then
        report "$event" "$file $jsonpath" "E_IDS" "unknown system '$value'"
      fi
    done < <(jq -r '.. | objects | to_entries[] | select(.key == "systemType" or .key == "systemName") | [.key, (.value|strings)] | @tsv' "$path")

    while IFS=$'\t' read -r jsonpath value; do
      [ -n "$value" ] || continue
      if ! in_list "$value" "${component_names[@]}"; then
        report "$event" "$file $jsonpath" "E_IDS" "unknown component '$value'"
      fi
    done < <(jq -r '.. | objects | to_entries[] | select(.key == "componentType" or .key == "componentName") | [.key, (.value|strings)] | @tsv' "$path")

    while IFS=$'\t' read -r jsonpath value; do
      [ -n "$value" ] || continue
      if [ "${#prefab_names[@]}" -gt 0 ] && ! in_list "$value" "${prefab_names[@]}"; then
        report "$event" "$file $jsonpath" "E_IDS" "unknown prefab '$value'"
      fi
    done < <(jq -r '.. | objects | to_entries[] | select((.key|test("[Pp]refab$")) or .key == "prefab") | [.key, (.value|strings)] | @tsv' "$path")

    while IFS= read -r value; do
      [ -n "$value" ] || continue
      markers="$(printf '%s' "$value" | grep -oE '(tick|wait):[^|[:space:]]+' || true)"
      while IFS= read -r marker; do
        [ -n "$marker" ] || continue
        number="${marker#*:}"
        if ! [[ "$number" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
          report "$event" "$file \$" "E_TICK" "invalid timing marker '$marker'"
        fi
      done <<< "$markers"
    done < <(jq -r '.. | strings' "$path")
  done
done

if [ "$failures" -gt 0 ]; then
  echo "Validation failed: $failures finding(s)." >&2
  exit 1
fi
echo "Validation passed: JSON, AJV schemas, and KindredExtract reference candidate allowlists; UUID catalog entries are in-game-verified only." >&2
