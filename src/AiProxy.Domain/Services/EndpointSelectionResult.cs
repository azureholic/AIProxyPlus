using AiProxy.Domain.Entities;

namespace AiProxy.Domain.Services;

/// <summary>Outcome of <see cref="EndpointSelector.SelectEndpoint"/> — either a chosen endpoint or a reason it failed.</summary>
public sealed class EndpointSelectionResult
{
    public bool IsSuccess { get; }
    public ModelEndpoint? SelectedEndpoint { get; }
    public string? FailureReason { get; }

    private EndpointSelectionResult(bool isSuccess, ModelEndpoint? selectedEndpoint, string? failureReason)
    {
        IsSuccess = isSuccess;
        SelectedEndpoint = selectedEndpoint;
        FailureReason = failureReason;
    }

    public static EndpointSelectionResult Success(ModelEndpoint endpoint) => new(true, endpoint, null);

    public static EndpointSelectionResult Failure(string reason) => new(false, null, reason);
}
