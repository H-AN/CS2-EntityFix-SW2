# GameData

This migration primarily uses documented Swiftly2 event APIs and does not require custom signatures/offsets/patches by default.

If you need native detours in the future, add `signatures.jsonc`, `offsets.jsonc`, and `patches.jsonc` in this folder and consume them via `Core.GameData`.
