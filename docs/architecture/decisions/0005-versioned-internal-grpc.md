# ADR 0005: Versioned internal gRPC contracts

Status: accepted

Process boundaries use protobuf packages with explicit version namespaces. Additive
changes preserve field numbers; incompatible changes receive a new package version.
Client-facing Blue marshal values never cross this internal boundary.
