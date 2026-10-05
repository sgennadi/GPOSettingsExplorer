namespace GPOSettingsExplorer.Models;

public sealed class GppPrinterItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    // Shared, TCP/IP, or Local.
    public string PrinterKind { get; set; } = "Shared";

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";

    public string PrinterName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    public bool DefaultPrinter { get; set; }
    public bool SkipLocal { get; set; }
    public bool DeleteAll { get; set; }

    // Shared-printer fields.
    public bool Persistent { get; set; }
    public bool DeleteMaps { get; set; }
    public string UserName { get; set; } = string.Empty;

    // Legacy GPP cpassword data is kept opaque. It is never decrypted,
    // displayed, created, or copied to a cloned preference.
    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    // TCP/IP printer fields.
    public string IpAddress { get; set; } = string.Empty;
    public bool UseDns { get; set; }
    public string LocalName { get; set; } = string.Empty;
    public string LprQueue { get; set; } = string.Empty;
    public string SnmpCommunity { get; set; } = string.Empty;
    public string Protocol { get; set; } = "PROTOCOL_RAWTCP_TYPE";
    public string PortNumber { get; set; } = "9100";
    public bool DoubleSpool { get; set; }
    public bool SnmpEnabled { get; set; }
    public string SnmpDevIndex { get; set; } = "1";

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasStoredCredential =>
        !string.IsNullOrWhiteSpace(OpaqueCredential);

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FiltersXml);

    public bool IsNew =>
        Ordinal == 0;

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string PrinterKindDisplay => NormalizeKind(PrinterKind) switch
    {
        "PORT" => "TCP/IP",
        "LOCAL" => "Local",
        _ => "Shared"
    };

    public string TargetDisplay => NormalizeKind(PrinterKind) switch
    {
        "PORT" => string.IsNullOrWhiteSpace(LocalName)
            ? IpAddress
            : $"{LocalName} ({IpAddress})",
        "LOCAL" => PrinterName,
        _ => Path
    };

    public string SearchText =>
        $"{GpoName} {Scope} {PrinterKindDisplay} {DisplayName} {Description} " +
        $"{ActionDisplay} {PrinterName} {Path} {Port} {Location} {Comment} " +
        $"{IpAddress} {LocalName} {UserName}";

    private static string NormalizeKind(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "TCP/IP" or "TCPIP" or "PORT" => "PORT",
            "LOCAL" => "LOCAL",
            _ => "SHARED"
        };
}
