using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Core;

public interface ICommandRouter
{
    Task<ProtocolEnvelope> HandleAsync(ProtocolEnvelope request, CancellationToken cancellationToken);
}
