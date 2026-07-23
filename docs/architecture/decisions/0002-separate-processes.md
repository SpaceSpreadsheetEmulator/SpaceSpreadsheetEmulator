# ADR 0002: Separate Gateway, Coordinator, and Worker processes

Status: accepted

Gateway owns client TCP, Coordinator owns control-plane placement and directory
state, and Worker owns simulation. The boundaries use versioned gRPC contracts even
for local development. Coordinator never proxies client traffic.
