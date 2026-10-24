namespace Gamex.GameCatalog.Api.Endpoints;

/// <summary>RFC 7807 responses with Gamex error type URIs.</summary>
internal static class Problems
{
    private const string TypeBase = "https://gamex.rs/errors/";

    public static IResult NotFound(string title, string detail) =>
        Results.Problem(type: TypeBase + "not-found", title: title, detail: detail, statusCode: StatusCodes.Status404NotFound);

    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, type: TypeBase + "validation", title: "One or more query parameters are invalid.");
}
