namespace VolumePadLink.Agent.Core;

public sealed class ProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
