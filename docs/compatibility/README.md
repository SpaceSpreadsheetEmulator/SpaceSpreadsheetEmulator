# Compatibility evidence

Wire behavior is specified before implementation. Checked-in fixtures are small,
hand-authored examples described by `fixtures/protocol/3396210/README.md`; private
captures and third-party implementation artifacts are not repository inputs.

Each future RPC must add an independent behavioral specification, a redistributable
fixture, and its expected semantic result before production dispatch is implemented.

Captured startup replay is restricted to an explicit build-profile allowlist of
auxiliary, non-authoritative bootstrap queries. Core identity, authentication,
character selection, session state, routing, and gameplay mutations are always
authored from current server state. Disallowed manifest entries are ignored before
their payload files are opened.

The graphical build-3396210 checkpoint currently requires that private replay
bundle. The transport and Blue-marshal values are decoded, validated, and emitted in
fresh correlated response packets, but the complete semantics of every replayed
auxiliary result are not yet understood well enough to replace them with authored
fallbacks. With replay disabled, authentication and the character-selection rowset
succeed, but startup stops at the population-cap bootstrap before the client sends
`SelectCharacterID`. Therefore a source-only checkout without the local replay
material is not yet a graphical-client-compatible server.

Private capture validation is intentionally local-only. Put parser-generated
`frames*.jsonl` exports below `_local/protocol-captures/`, or set
`ProtocolTests:LocalCaptureDirectory` in the ignored
`tests/SpaceSpreadsheetEmulator.Protocol.Tests/appsettings.UnitTest.local.json`.
The local-capture test skips when that directory is absent or contains no exports.
The directory and local settings file are gitignored; captures, session material,
keys, and login primitives must never be committed.

The default local gate validates bounded zlib decompression, outer marshal decoding,
exact preserve-mode re-encoding, canonical semantic round-trips, and non-sensitive
Macho packet/address summaries. Set `ProtocolTests:LocalCaptureDirectory` explicitly
to one build-3396210 capture directory in the local UnitTest appsettings file to
enable the stricter gate that descends into every client `CALL_REQ` and type-12
client notification present, decodes the nested substream, preserves its exact
bytes, and validates the typed envelope. This explicit pin prevents a mixed
directory of different client builds from being treated as one protocol profile.

Milestone 2 adds checked-in fabricated tests for the login handshake, independent
inbound/outbound AES-CBC chains, correlated calls, and the legacy ping exchange. Its
Gateway integration fixture also reconstructs the build-3396210 account and character
packed rows independently and verifies a complete select/undock/dock loopback flow.
Private credentials, hashes, session keys, and captured packets remain local-only.

See `login-3396210.md` for the independently authored handshake and initial-session
behavioral specification and `station-entry-3396210.md` for the post-selection
station-entry sequence and graphical compatibility gate.
