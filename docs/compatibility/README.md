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

Private capture validation is intentionally local-only. Put parser-generated `frames*.jsonl`
exports below `_local/protocol-captures/`, or set `SSE_LOCAL_PROTOCOL_CAPTURE_DIRECTORY`
to another directory. The local-capture test skips when that directory is absent or contains
no exports. The directory is gitignored; captures, session material, keys, and login primitives
must never be committed.

The local gate validates bounded zlib decompression, marshal decoding, exact
preserve-mode re-encoding, canonical semantic round-trips, and non-sensitive Macho
packet/address summaries. Milestone 2 adds checked-in fabricated tests for the login
handshake, independent inbound/outbound AES-CBC chains, correlated calls, and the
legacy ping exchange. Its Gateway integration fixture also reconstructs the
build-3396210 account and character packed rows independently and verifies a complete
select/undock/dock loopback flow. Private credentials, hashes, session keys, and
captured packets remain local-only.

See `login-3396210.md` for the independently authored handshake and initial-session
behavioral specification.
