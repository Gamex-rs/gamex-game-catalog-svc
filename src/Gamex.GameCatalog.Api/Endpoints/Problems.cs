using Gamex.GameCatalog.Application.Common;

namespace Gamex.GameCatalog.Api.Endpoints;

/// <summary>RFC 7807 responses with Gamex error type URIs.</summary>
internal static class Problems
{
    private const string TypeBase = "https://gamex.rs/errors/";

    public static IResult NotFound(string title, string detail) =>
        Results.Problem(type: TypeBase + "not-found", title: title, detail: detail, statusCode: StatusCodes.Status404NotFound);

    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, type: TypeBase + "validation", title: "One or more parameters are invalid.");

    public static IResult From<T>(OperationResult<T> result, string notFoundTitle, Func<T, IResult> onOk) => result.Status switch
    {
        OperationStatus.Ok => onOk(result.Value!),
        OperationStatus.NotFound => NotFound(notFoundTitle, result.Detail!),
        OperationStatus.Invalid => Validation(result.Errors!.ToDictionary()),
        OperationStatus.DependencyFailed => Results.Problem(
            type: TypeBase + "upstream-failed", title: "Upstream service failed", detail: result.Detail, statusCode: StatusCodes.Status502BadGateway),
        OperationStatus.DependencyDisabled => Results.Problem(
            type: TypeBase + "upstream-disabled", title: "Upstream service not configured", detail: result.Detail, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null),
    };
}
