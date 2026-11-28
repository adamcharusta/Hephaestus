using System.Globalization;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Api;

/// <summary>
/// The status of a Signal (Story 3.14, AD-17): what became of a request the Owner made. Everything that affects a Task or a Project's work is a Signal, so the answer to such a request is only its id, and this is where its
/// outcome is read: pending until the loop has applied it in one transaction, then applied, or rejected with the reason.
/// </summary>
internal static class SignalApi
{
    public static void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/signals/{id}", ShowAsync);

    private static async Task<IResult> ShowAsync(string id, ISignalInbox inbox, CancellationToken cancellationToken)
    {
        if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && await inbox.FindAsync(sequence, cancellationToken) is { } found)
        {
            return Results.Json(Documents.Signal(found), ApiJson.Default.SignalDocument);
        }

        return new ProblemResult(StatusCodes.Status404NotFound, "signal-not-found", MessageIds.ProblemSignalNotFoundTitle, new Message(MessageIds.ProblemSignalNotFoundDetail, id));
    }
}
