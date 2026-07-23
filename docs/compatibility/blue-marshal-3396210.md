# Build 3396210 selected wire vocabulary

This is an independently authored behavioral specification for the first supported
profile. It records only behavior exercised by hand-authored fixtures and private
black-box observations. No reference implementation is a source or dependency.

## Stream envelope and shared encodings

A marshal document begins with byte `7e`, followed by a little-endian `uint32`
declaring the number of saved-value slots, one root value, and a tail table with
exactly that many little-endian `uint32` slot numbers. Bytes between the end of the
root and the start of the tail table are malformed.

The tail table is read in save-flag encounter order. Value byte bit `40` consumes
the next table entry and saves that value in the entry's one-based slot; it does not
implicitly use the next sequential slot. The observed table is a permutation of
`1` through the declared count. Bits `00` through `3f` select the form. A saved
reference contains a size-ex slot number and may resolve only a slot populated by
an earlier value. String-table references contain one byte and are one-based; index
zero is reserved.

The root decoder is bounded at `document length - 4 * saved count`, so a malformed
root length cannot consume the tail. A truncated table, duplicate, zero, or
out-of-range slot, too many or too few save flags, or a reference to an unpopulated
slot is malformed. A zero saved count has no tail table.

Size-ex values use one byte for values `0` through `254`. Marker `ff` is followed by
a little-endian `uint32`. Length and count limits are checked before allocation.

## Values

| Opcode | Form | Payload |
| --- | --- | --- |
| `01` | null | none |
| `02` | token | size-ex ASCII byte length, bytes |
| `03`, `04`, `05`, `06` | signed integer | little-endian 64, 32, 16, or 8-bit |
| `07`, `08`, `09` | integer constants | `-1`, `0`, `1` |
| `0a`, `0b` | floating point | binary64, or constant zero |
| `0d`, `10`, `13` | byte buffers | size-ex byte length, bytes |
| `0e`, `0f` | empty or one-byte buffer | no payload, or one byte |
| `11` | string-table reference | one-byte, one-based index |
| `12` | UTF-16LE text | size-ex code-unit count, code units |
| `14`, `15`, `16` | tuple, list, ordered dictionary | size-ex count, children |
| `17` | ordinary object | type value, state value |
| `19` | substructure | one nested value |
| `1b` | saved-value reference | size-ex, one-based slot |
| `1c` | checksummed wrapper | little-endian `uint32` metadata, one nested value |
| `1f`, `20` | Boolean | true, false |
| `21` | opaque picked data | size-ex byte length, bytes |
| `22`, `23` | extended object variants 1 and 2 | header, list members, `2d`, dictionary members, `2d` |
| `24`, `25` | empty tuple, one-item tuple | no payload, or one child |
| `26`, `27` | empty list, one-item list | no payload, or one child |
| `28`, `29` | empty text, one-character text | no payload, or one UTF-16LE code unit |
| `2a` | packed row | descriptor, size-ex packed-data length, packed bytes, variable values |
| `2b` | opaque substream | size-ex byte length, bytes |
| `2c` | two-item tuple | two children |
| `2d` | extended-object terminator | valid only inside an extended object |
| `2e` | UTF-8 text | size-ex byte length, bytes |
| `2f` | arbitrary integer | size-ex byte length, signed little-endian bytes |

Dictionary entries are visible and ordered, including duplicates. Their wire order
is value then key. Extended-object dictionary members use the same order.

Checksums are preserved metadata. The observed decoder accepts both matching and
non-matching values, so this profile does not invent a validation rule.

## Canonical and preserve modes

Preserve mode reproduces the entire original marshal document byte-for-byte.
Canonical mode emits a zero-slot document, expands saved references, uses compact
integer and collection forms, UTF-8 text, and deterministic ordered traversal.
Semantic comparison ignores integer width and saved-reference indirection while
wire comparison encodes in preserve mode.

## Packed rows

The descriptor is ObjectEx1 with token `blue.DBRowDescriptor` and an ordered tuple
of `(name, numeric encoding)` columns. The selected observation set contains:

| Encoding | Observed semantic family | Wire location |
| --- | --- | --- |
| `2` | signed 16-bit integer | packed fixed section |
| `3` | signed 32-bit integer | packed fixed section |
| `5` | binary64 | packed fixed section |
| `6` | signed 64-bit integer | packed fixed section |
| `11` | Boolean | packed fixed section |
| `17` | unsigned byte | packed fixed section |
| `20` | signed 64-bit integer | packed fixed section |
| `64` | signed 64-bit timestamp | packed fixed section |
| `129` | bytes | following marshal value |
| `130` | Unicode text | following marshal value |

The general codec validates the descriptor, column bound, packed byte bound, and one
following marshal value for each variable-width encoding. It preserves the fixed
packed section as bytes because row layout is descriptor-dependent; no inferred
field projection is presented as a general protocol fact. Canonical encoding accepts
those same explicit packed bytes and variable values, so every observed row can be
decoded and reproduced without loss.

Gateway's build-3396210 character-selection adapter has a deliberately narrower
authored writer. Fixed fields are stably ordered by descending width, written
little-endian, and followed by Boolean bits and descriptor-order null bits. The bit
field uses `((boolean count + column count) >> 3) + 1` bytes. The combined fixed and
bit sections use the observed zero-run encoding: each control nibble represents up
to eight literal bytes or up to eight zero bytes. Variable-width values follow in
descriptor order. The writer rejects unsupported encodings, CLR/wire type mismatches,
and out-of-range fixed values.

This construction rule is scoped to the selected build and tested through the
Gateway TCP path with a fabricated starter character. It does not turn the
descriptor-preserving protocol decoder into a generic database-row projector.

## Limits and framing

The defaults are 16 MiB for a frame and individual value, 64 MiB after zlib
decompression, nesting depth 128, 262,144 collection entries, 65,536 saved slots,
and 4,096 packed-row columns. Network framing is a four-byte little-endian payload
length. Fragmented input is incomplete; coalesced frames are consumed in order.
