using System.Text.RegularExpressions;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Internal;

internal static class DescriptorValidator
{
    private static readonly Regex CapabilityIdPattern =
        new(@"^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9\-]*)+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProviderIdPattern =
        new(@"^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9\-]*)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(ApplicationContributorDescriptor descriptor)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(descriptor.ProviderId))
        {
            errors.Add("ProviderId must not be blank.");
        }
        else if (!ProviderIdPattern.IsMatch(descriptor.ProviderId))
        {
            errors.Add($"ProviderId '{descriptor.ProviderId}' is malformed.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            errors.Add("DisplayName must not be blank.");
        }

        ValidateCapabilityList(descriptor.Produces, "Produces", errors, allowEmpty: true);
        ValidateCapabilityList(descriptor.Requires, "Requires", errors, allowEmpty: true);
        ValidateCapabilityList(descriptor.Optional, "Optional", errors, allowEmpty: true);

        var requires = new HashSet<string>(descriptor.Requires, StringComparer.Ordinal);
        foreach (var optional in descriptor.Optional)
        {
            if (requires.Contains(optional))
            {
                errors.Add($"Capability '{optional}' appears in both Requires and Optional.");
            }
        }

        foreach (var produced in descriptor.Produces)
        {
            if (requires.Contains(produced))
            {
                errors.Add($"Provider '{descriptor.ProviderId}' requires capability '{produced}' that it also produces.");
            }
        }

        if (descriptor.MaxAge is { } maxAge && maxAge <= TimeSpan.Zero)
        {
            errors.Add("MaxAge must be null or positive.");
        }

        if (descriptor.PullTimeout is { } pullTimeout && pullTimeout <= TimeSpan.Zero)
        {
            errors.Add("PullTimeout must be null or positive.");
        }

        if (descriptor.SchemaVersion < 1)
        {
            errors.Add("SchemaVersion must be at least 1.");
        }

        return errors;
    }

    public static bool IsValidCapabilityId(string capabilityId) =>
        !string.IsNullOrWhiteSpace(capabilityId) && CapabilityIdPattern.IsMatch(capabilityId);

    public static bool FactKeyBelongsToContributor(
        string factKey,
        ApplicationContributorDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(factKey))
        {
            return false;
        }

        if (factKey.StartsWith(descriptor.ProviderId + ".", StringComparison.Ordinal)
            || string.Equals(factKey, descriptor.ProviderId, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var capability in descriptor.Produces)
        {
            var prefix = capability.Contains('.', StringComparison.Ordinal)
                ? capability.Split('.')[0]
                : capability;

            if (factKey.StartsWith(prefix + ".", StringComparison.Ordinal)
                || factKey.StartsWith(capability + ".", StringComparison.Ordinal)
                || string.Equals(factKey, capability, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateCapabilityList(
        IReadOnlyCollection<string> capabilities,
        string listName,
        List<string> errors,
        bool allowEmpty)
    {
        if (!allowEmpty && capabilities.Count == 0)
        {
            errors.Add($"{listName} must not be empty.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability) || !CapabilityIdPattern.IsMatch(capability))
            {
                errors.Add($"{listName} contains malformed capability ID '{capability}'.");
                continue;
            }

            if (!seen.Add(capability))
            {
                errors.Add($"{listName} contains duplicate capability '{capability}'.");
            }
        }
    }
}
