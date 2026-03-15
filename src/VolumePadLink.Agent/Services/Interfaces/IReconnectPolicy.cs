namespace VolumePadLink.Agent.Services.Interfaces;

public interface IReconnectPolicy
{
    TimeSpan GetDelay(int attempt);
}
