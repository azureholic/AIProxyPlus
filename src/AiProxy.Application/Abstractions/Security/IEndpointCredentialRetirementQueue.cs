namespace AiProxy.Application.Abstractions;

public interface IEndpointCredentialRetirementQueue
{
    void ScheduleRetirement(Guid endpointId, string secretRef);

    void ScheduleOrphanCleanup(Guid endpointId, string secretRef);
}