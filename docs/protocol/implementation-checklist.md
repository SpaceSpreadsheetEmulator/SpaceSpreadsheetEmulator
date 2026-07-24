# Protocol implementation and capture checklist

This is the working order for turning a discovered client behavior into a
clean-room, testable server feature.

## Current compatibility map

| Slice | Current evidence | Remaining real-client proof |
| --- | --- | --- |
| Framing, marshal, optional crypto, login | 3396210 wire-confirmed and server-covered | Preserve regression coverage |
| Character selection and station session | 3396210 wire-confirmed and server-covered | Preserve session-before-response order |
| Station rendering baseline | Fresh graphical 3396210 checkpoint passes with the local auxiliary replay bundle | Replace remaining replayed auxiliary calls with authored contracts |
| Inventory open/list | 3396210 wire-confirmed and server-covered for starter state | Expand only from exact typed consumers |
| Inventory mutation | Consumer-required | Exact request, response, event, and persistence proof |
| Undock and initial space bind | Server-covered | Graphical 3396210 session, `SetState`, ego ball/slim, state-bag, and liveness proof |
| Authored nearby space objects | Server-covered as neutral snapshot state; graphical encoding capture-gated | Matching ball/slim and overview/navigation proof |
| Direction, stop, follow, orbit, point | Server-covered | Graphical 3396210 Destiny reaction proof |
| Dock | Server-covered with exact documented order | Graphical round trip from a real ballpark |
| Warp | Consumer-required | Entire 3396210 call/Destiny/FX contract |
| Target lock/unlock | Consumer-required | Entire 3396210 response/event/error contract |
| Module activation/deactivation | Consumer-required | Entire 3396210 Dogma event/error contract |
| Stargate jump | Consumer-required | Entire cross-system 3396210 transition contract |

“Server-covered” means the codecs and local topology agree. It must not be
reported as graphical proof.

## Capture one action

Use a lawfully controlled account and the exact target client build. Keep all raw
material below ignored `_local/` paths.

Before the action, record:

- client build, protocol profile, and static-data build/hash;
- initial session attributes;
- relevant bound-object leases;
- ballpark/Dogma/inventory state needed as a precondition;
- exact action performed and UI option values.

For the smallest possible action window, identify:

1. the client call request or one-way notification;
2. service or bound-object route;
3. positional and keyword argument types;
4. call identifier;
5. correlated response or error;
6. every session or state event causally required for success;
7. the first visible or subsequent-RPC proof that the client accepted it.

Then repeat a deliberate failure such as invalid range or missing target. A
success-only trace does not establish the error contract or client lock cleanup.

Never copy raw packets, session identifiers, account data, proprietary objects, or
decoded dumps into the repository. Reduce the observation to an independently
written behavior statement and a minimal fabricated fixture.

## Specify before implementing

For each action, add or update a build-specific compatibility specification with:

- preconditions and authoritative owner;
- request route and typed semantic arguments;
- which fields are build-specific Gateway concerns;
- correlation and minimum response value;
- required event names and semantic payload fields;
- hard ordering edges, with unrelated calls left unordered;
- success observation;
- typed failure behavior;
- reconnect, retry, and stale-epoch behavior;
- evidence label and known uncertainty.

If the observation comes from a different build, use it only to formulate the
next exact-build experiment.

## Implement through the ownership boundaries

1. Define a narrow protocol-neutral request and result/event contract.
2. Make Worker validate and commit the authoritative mutation in its single-writer
   partition.
3. Add idempotency, epoch fencing, and durable state where the action requires it.
4. Publish a snapshot or ordered neutral event from Worker.
5. Decode the exact client call in Gateway and map it to that neutral request.
6. Map the result, error, session change, and events back to the build profile.
7. Atomically enqueue every ordering-sensitive outbound batch.
8. Keep Coordinator out of the gameplay request path except for placement,
   lease/epoch control, and cross-partition coordination.

Do not let a convenient Macho tuple leak into a `.proto` contract.

## Test layers

Every promoted behavior should have the narrowest useful coverage:

- codec tests for the fabricated request, response, error, and event values;
- profile tests for exact argument and result shapes;
- Gateway integration tests for call correlation and outbound ordering;
- Worker tests for legality, state mutation, idempotency, fencing, and event order;
- topology tests through real Gateway, Coordinator, Worker, and storage boundaries;
- an ignored local exact-build capture comparison;
- a fresh graphical checkpoint with a visible success assertion and later ping.

For snapshot/delta features, test snapshot-first behavior, a sequence gap, a stale
epoch, cancellation, reconnect, and duplicate request receipt.

## Recommended next vertical slices

1. Finish the graphical build-3396210 undock checkpoint: session transition,
   solar bind response, minimal usable `SetState`, ego ball/slim, and stable pings.
2. Add one authored nearby object with matching ball/slim and prove it in the
   overview before expanding the whole environment.
3. Prove one graphical movement action end to end, including the authoritative
   Destiny update rather than only the bound call.
4. Capture and implement target lock/unlock with typed failure.
5. Capture one simple module activation/deactivation and its Dogma effect event.
6. Add warp only after ordinary movement and Destiny state are reliable.
7. Add inventory mutation with committed item-change events.
8. Design durable cross-partition transfer before implementing stargate jump.

This order maximizes protocol reuse while keeping each new gameplay invariant
small enough to prove.

## Handoff gate

Before declaring an action implemented:

- every blocking call terminates in its correlated response or error;
- every required authoritative event is present;
- session and event ordering is encoded as tests;
- no unknown route is being hidden by a successful `null`;
- the client reaches the visible success state and survives later ping cycles;
- no raw research input, credential, identity, capture, key, or personal path is
  present in the diff;
- `dotnet test SpaceSpreadsheetEmulator.slnx` and `git diff --check` pass.
