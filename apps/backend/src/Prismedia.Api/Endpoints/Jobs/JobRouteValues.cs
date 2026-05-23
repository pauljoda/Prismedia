using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class JobRouteValues {
    internal static bool TryDecodeJobType(string? value, out JobType? type) {
        if (string.IsNullOrWhiteSpace(value)) {
            type = null;
            return true;
        }

        if (value.TryDecodeAs<JobType>(out var decoded)) {
            type = decoded;
            return true;
        }

        type = null;
        return false;
    }
}
