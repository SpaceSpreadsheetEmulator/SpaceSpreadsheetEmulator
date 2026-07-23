# ADR 0001: Clean-room, specification-first protocol development

Status: accepted

Protocol behavior is written as an independent specification and demonstrated with
hand-authored fixtures before original .NET code is added. EVESharp is historical
context only. EvEJS and private captures may be black-box oracles, but never source,
build, test, or runtime dependencies. Wire-visible distinctions remain explicit.
