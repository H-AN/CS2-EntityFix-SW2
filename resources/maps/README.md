# Map Gravity Config

Place per-map gravity overrides in this folder with filename `<mapname>.json`.

Example (`de_dust2.json`):

```json
{
  "123456": 0.2,
  "654321": 0.5
}
```

- Key: `UniqueHammerID` of a `trigger_gravity` entity
- Value: gravity scale applied when a player touches that trigger
