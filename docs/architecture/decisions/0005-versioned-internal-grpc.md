# ADR 0005: Versioned internal gRPC contracts

Status: accepted

Process boundaries use protobuf packages with explicit version namespaces. Additive
changes preserve field numbers; incompatible changes receive a new package version.
Client-facing Blue marshal values never cross this internal boundary.

The first incompatible solar-system gameplay cleanup moved that service directly
from `backplane.v1` to `backplane.v2` while the project was still pre-release and
all callers could migrate together. Login remains in `backplane.v1`; package versions
apply to each contract surface rather than forcing unrelated services to rev together.
