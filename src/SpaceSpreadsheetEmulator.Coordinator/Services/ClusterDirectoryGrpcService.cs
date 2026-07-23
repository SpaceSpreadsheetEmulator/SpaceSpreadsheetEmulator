using Grpc.Core;
using SpaceSpreadsheetEmulator.Cluster.Contracts.V1;
using SpaceSpreadsheetEmulator.Cluster.Directory;

namespace SpaceSpreadsheetEmulator.Coordinator.Services;

public sealed class ClusterDirectoryGrpcService(IPartitionDirectory directory)
    : ClusterDirectory.ClusterDirectoryBase
{
    public override async Task<ResolvePartitionResponse> ResolvePartition(
        ResolvePartitionRequest request,
        ServerCallContext context)
        => await ResolveCoreAsync(request, context.CancellationToken);

    public async Task<ResolvePartitionResponse> ResolveCoreAsync(
        ResolvePartitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(PartitionKind), request.Kind)
            || string.IsNullOrWhiteSpace(request.Key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid partition kind and key are required."));
        }

        PartitionAssignment? assignment = await directory.ResolveAsync(
            new PartitionKey((PartitionKind)request.Kind, request.Key),
            cancellationToken);

        return assignment is null
            ? new ResolvePartitionResponse { Found = false }
            : new ResolvePartitionResponse
            {
                Found = true,
                OwnerNodeId = assignment.OwnerNodeId.Value,
                Epoch = assignment.Epoch.Value,
                Endpoint = assignment.Endpoint.AbsoluteUri,
            };
    }
}
