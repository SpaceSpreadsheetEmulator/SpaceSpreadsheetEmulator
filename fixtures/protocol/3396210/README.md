# Protocol fixtures for build 3396210

These minimal fixtures were written by hand from the independent specification in
`docs/compatibility/blue-marshal-3396210.md`. Hex files allow whitespace and `#`
comments. Each has diagnostic JSON describing the codec, expected outcome, and
semantic root kind or error code. They contain no captured client packets.

Every selected opcode form has its own fixture. The MachoNet example uses fabricated
names and identifiers. Private parser exports are exercised only by the conditional,
gitignored local-corpus test documented in `docs/compatibility/README.md`.
