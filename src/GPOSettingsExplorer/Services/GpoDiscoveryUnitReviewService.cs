using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit, snapshot-bound unit review. No unit is inferred from a policy
/// title, ADMX min/max, localized label or matching numeric value.
/// All filesystem access is read-only and restricted to named ADMX/ADML files.
/// </summary>
public static class GpoDiscoveryUnitReviewService
{
    private const int MaxDefinitionBytes = 8 * 1024 * 1024;

    public sealed record ReviewCandidate(
        GpoDiscoveryEvidenceService.Evidence Evidence,
        string Status, string Reason,
        AdmxPolicyDefinition? Policy = null,
        AdmxElementDefinition? Element = null)
    {
        public bool CanReview => Status == "READY_FOR_MANUAL_REVIEW" &&
                                 Policy is not null && Element is not null;
    }

    public sealed record DefinitionFingerprint(
        string PolicyIdentitySha256,
        string SourceFileSha256,
        string AdmxSha256,
        string AdmlSha256,
        string DefinitionSha256,
        string DefinitionLocation);

    /// <summary>
    /// A candidate is only an invitation for a human to check the units.
    /// It is NOT evidence that the stored unit is minutes.
    /// </summary>
    public static ReviewCandidate Inspect(
        GpoDiscoveryEvidenceService.Evidence item,
        IReadOnlyList<AdmxPolicyDefinition>? catalog)
    {
        ArgumentNullException.ThrowIfNull(item);
        ReviewCandidate Deny(string status, string reason) =>
            new(item, status, reason);

        if (item.NavigationStatus != "MMC_ROW_OBSERVED" ||
            item.MmcRowNavigation != "Exact MMC row candidate" ||
            string.IsNullOrWhiteSpace(item.MmcObservedSection))
            return Deny("MMC_NOT_EXACT", "A unique native MMC row and its full section path have not both been verified.");

        if (item.SourceStatus != "REGISTRY_SOURCE_LOCATED" ||
            !item.SourceStoredType.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase) ||
            !IsHash(item.SourceFileHash) || !IsHash(item.IdentityHash) ||
            string.IsNullOrWhiteSpace(item.RegistryTarget) ||
            item.RegistryTarget.Contains(" / **", StringComparison.Ordinal))
            return Deny("SOURCE_NOT_EXACT", "Requires one hashed Registry.pol DWORD target in the same GPO and scope.");

        if (item.MmcObservedState.Contains("Not Configured", StringComparison.OrdinalIgnoreCase))
            return Deny("MMC_NOT_CONFIGURED", "The native MMC row reports Not Configured; no stored value comparison can be established.");

        if (!TryCanonicalUInt(item.SourceStoredValue, out _) ||
            !TryHours(item.MmcObservedValue, out _))
            return Deny("UNSUPPORTED_FORMAT", "Currently supports only a canonical DWORD and an exact English 'N hours' MMC display.");

        if (catalog is null)
            return Deny("ADMX_NOT_LOADED", "Load ADMX/ADML definitions before checking the unit provenance.");

        var matchingPolicies = catalog.Where(policy =>
            policy.Scope.Equals(item.Scope, StringComparison.OrdinalIgnoreCase) ||
            policy.Scope.Equals("Both", StringComparison.OrdinalIgnoreCase))
            .Where(policy =>
                policy.DisplayName.Equals(item.SettingName, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(policy.Name) &&
                !string.IsNullOrWhiteSpace(policy.AdmxFile) &&
                MmcInventoryReconciliation.SectionMatches(
                    item.MmcObservedSection, policy.Category))
            .ToArray();

        if (matchingPolicies.Length != 1)
            return Deny(matchingPolicies.Length > 1 ? "AMBIGUOUS_ADMX" : "ADMX_NOT_MAPPED",
                "Expected exactly one matching ADMX policy by scope, full MMC category and literal name; found " +
                matchingPolicies.Length.ToString(CultureInfo.InvariantCulture) + ".");

        var policyMatch = matchingPolicies[0];
        var matchedElements = policyMatch.Elements.Where(element =>
            element.Type == AdmxElementType.Decimal &&
            !string.IsNullOrWhiteSpace(element.Id) &&
            !string.IsNullOrWhiteSpace(element.Key) &&
            !string.IsNullOrWhiteSpace(element.ValueName) &&
            (element.Key + " / " + element.ValueName)
                .Equals(item.RegistryTarget, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchedElements.Length != 1)
            return Deny(matchedElements.Length > 1 ? "AMBIGUOUS_ELEMENT" : "DECIMAL_ELEMENT_NOT_MAPPED",
                "Expected one exact ADMX decimal element for the Registry.pol key/value; found " +
                matchedElements.Length.ToString(CultureInfo.InvariantCulture) + ".");

        return new ReviewCandidate(item, "READY_FOR_MANUAL_REVIEW",
            "Candidate only: independently inspect ADMX/ADML semantics and native MMC units before approval.",
            policyMatch, matchedElements[0]);
    }

    /// <summary>
    /// Capture and verify actual on-disk ADMX + ADML contents (not a cache
    /// timestamp/fingerprint). Refuse missing, oversized, malformed or DTD
    /// source files and stale cached policy/element metadata.
    /// </summary>
    public static DefinitionFingerprint CaptureDefinition(
        ReviewCandidate candidate, string definitionsDirectory, string language)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.CanReview || candidate.Policy is null || candidate.Element is null)
            throw new InvalidOperationException("This setting is not eligible for reviewed unit mapping.");
        if (string.IsNullOrWhiteSpace(definitionsDirectory) ||
            string.IsNullOrWhiteSpace(language) ||
            language != Path.GetFileName(language) ||
            language is "." or "..")
            throw new InvalidOperationException("A pinned ADMX store and exact ADML language are required.");

        var policy = candidate.Policy;
        var element = candidate.Element;
        if (policy.AdmxFile != Path.GetFileName(policy.AdmxFile) ||
            !policy.AdmxFile.EndsWith(".admx", StringComparison.OrdinalIgnoreCase) ||
            policy.AdmxFile is "." or "..")
            throw new InvalidOperationException("Invalid ADMX file name in the cached definition.");

        var admxPath = Path.Combine(definitionsDirectory, policy.AdmxFile);
        var admlPath = Path.Combine(definitionsDirectory, language,
            Path.GetFileNameWithoutExtension(policy.AdmxFile) + ".adml");
        var admxBytes = ReadBoundedDefinition(admxPath);
        var admlBytes = ReadBoundedDefinition(admlPath);
        var admx = ParseXml(admxBytes);
        var adml = ParseXml(admlBytes);

        var policies = admx.Descendants().Where(x => x.Name.LocalName == "policy" &&
            (string?)x.Attribute("name") == policy.Name).ToArray();
        if (policies.Length != 1)
            throw new InvalidOperationException("The current ADMX does not contain exactly one named policy.");

        var currentPolicy = policies[0];
        var policyScope = (string?)currentPolicy.Attribute("class") ?? "";
        if (!policyScope.Equals("Both", StringComparison.OrdinalIgnoreCase) &&
            !policyScope.Equals(candidate.Evidence.Scope == "Computer" ? "Machine" : "User",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The ADMX policy scope changed since catalog capture.");

        var parentKey = (string?)currentPolicy.Attribute("key") ?? "";
        var elements = currentPolicy.Elements()
            .Where(x => x.Name.LocalName == "elements")
            .SelectMany(x => x.Elements())
            .Where(x => x.Name.LocalName == "decimal" &&
                (string?)x.Attribute("id") == element.Id)
            .ToArray();

        if (elements.Length != 1 ||
            !((string?)elements[0].Attribute("valueName") ?? "")
                .Equals(element.ValueName, StringComparison.OrdinalIgnoreCase) ||
            !((string?)elements[0].Attribute("key") ?? parentKey)
                .Equals(element.Key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exact ADMX decimal element/registry target changed.");

        var displayRef = (string?)currentPolicy.Attribute("displayName") ?? "";
        if (!displayRef.StartsWith("$(string.", StringComparison.Ordinal) ||
            !displayRef.EndsWith(')'))
            throw new InvalidOperationException("The ADMX policy has no resolvable localized display name.");

        var stringId = displayRef["$(string.".Length..^1];
        var labels = adml.Descendants().Where(x => x.Name.LocalName == "string" &&
            (string?)x.Attribute("id") == stringId).ToArray();
        if (labels.Length != 1 ||
            !labels[0].Value.Trim().Equals(policy.DisplayName, StringComparison.Ordinal))
            throw new InvalidOperationException("ADML display name differs from the observed policy name.");

        var admxHash = Sha256(admxBytes);
        var admlHash = Sha256(admlBytes);
        var evidence = candidate.Evidence;
        var digestInput = string.Join("\u001f",
            admxHash, admlHash, language,
            policy.Name, policy.DisplayName, policy.Category, policyScope,
            element.Id, element.Key, element.ValueName,
            evidence.GpoId.ToString("D"), evidence.Scope, evidence.IdentityHash,
            evidence.RegistryTarget, evidence.MmcObservedSection);
        var digest = Sha256(Encoding.UTF8.GetBytes(digestInput));

        return new DefinitionFingerprint(evidence.IdentityHash, evidence.SourceFileHash,
            admxHash, admlHash, digest,
            policy.AdmxFile + " + " + language + "/" +
            Path.GetFileNameWithoutExtension(policy.AdmxFile) + ".adml");
    }

    public static GpoDiscoveryEvidenceService.VerifiedUnitMapping Approve(
        ReviewCandidate candidate, DefinitionFingerprint fingerprint, string reviewReference)
    {
        if (!candidate.CanReview ||
            !IsHash(fingerprint.DefinitionSha256) ||
            !string.Equals(candidate.Evidence.IdentityHash, fingerprint.PolicyIdentitySha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.Evidence.SourceFileHash, fingerprint.SourceFileSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Review evidence identity or source hash changed.");
        var review = reviewReference.Trim();
        if (review.Length is < 6 or > 128 ||
            review.Any(char.IsControl))
            throw new ArgumentException("Enter a review reference of 6-128 printable characters.",
                nameof(reviewReference));

        var source = candidate.Evidence;
        return new GpoDiscoveryEvidenceService.VerifiedUnitMapping(
            source.GpoId, source.Scope, source.RegistryTarget, "minutes",
            "Operator-reviewed " + fingerprint.DefinitionLocation +
            "; ADMX SHA-256 " + fingerprint.AdmxSha256 +
            "; ADML SHA-256 " + fingerprint.AdmlSha256)
        {
            IdentitySha256 = fingerprint.PolicyIdentitySha256,
            SourceFileSha256 = fingerprint.SourceFileSha256,
            DefinitionEvidenceSha256 = fingerprint.DefinitionSha256,
            ReviewedAgainstMmc = true,
            ReviewReference = review
        };
    }

    private static byte[] ReadBoundedDefinition(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaxDefinitionBytes)
            throw new InvalidOperationException("Required ADMX/ADML file is missing or exceeds the read limit: " +
                file.Name);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        var bytes = new byte[65536];
        int n;
        while ((n = input.Read(bytes, 0, bytes.Length)) != 0)
        {
            if (buffer.Length + n > MaxDefinitionBytes)
                throw new InvalidOperationException("ADMX/ADML file exceeded the read limit.");
            buffer.Write(bytes, 0, n);
        }
        return buffer.ToArray();
    }

    private static XDocument ParseXml(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDefinitionBytes,
            MaxCharactersFromEntities = 0
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static bool TryCanonicalUInt(string text, out uint value)
    {
        value = 0;
        return text.Length > 0 &&
            text.All(c => c is >= '0' and <= '9') &&
            (text.Length == 1 || text[0] != '0') &&
            uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryHours(string text, out uint hours)
    {
        hours = 0;
        return text.EndsWith(" hours", StringComparison.Ordinal) &&
            TryCanonicalUInt(text[..^" hours".Length], out hours);
    }
    private static bool IsHash(string? text) =>
        text is { Length: 64 } && text.All(Uri.IsHexDigit);
    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
