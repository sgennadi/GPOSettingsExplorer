using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public sealed partial class GpoAdvancedAnalysisWindow
{
    private GpoGitOpsReviewRequest? _lastGitOpsReview;

    private TabItem SetupGitOpsTab()
    {
        var tab = Tab("GitOps & approvals", out var commands);
        AddAction(commands, "Save redacted manifest...", SaveGitOpsManifestAsync);
        AddAction(commands, "Compare saved baseline...", CreateGitOpsReviewAsync);
        AddAction(commands, "Open review request...", OpenGitOpsRequestAsync);
        AddAction(commands, "List signing certificates", ListReviewCertificatesAsync);
        AddAction(commands, "Approve / reject...", SignGitOpsReviewAsync);
        AddAction(commands, "Verify signed decision...", VerifyGitOpsReviewAsync);
        AddAction(commands, "Export protected source...", ExportDpapiSourceAsync);
        AddAction(commands, "Check protected source...", InspectDpapiSourceAsync);
        commands.Children.Add(new TextBlock
        {
            Text = "Redacted fingerprint JSON and review requests may be committed to a " +
                "private, access-controlled Git repository after human review. " +
                "The DPAPI archive contains confidential source evidence and must " +
                "NEVER be committed or uploaded. Certificates sign a specific request; " +
                "nothing applies or changes GPOs.",
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 250,
            MaxWidth = 860,
            Margin = new Thickness(7, 5, 7, 5),
            Foreground = UiStyle.WarningBrush
        });
        return tab;
    }

    private Task<string> SaveGitOpsManifestAsync()
    {
        var manifest = GpoGitOpsExportService.Capture(Source());
        var save = new SaveFileDialog
        {
            Title = "Save redacted, Git-ready HMAC fingerprint manifest",
            Filter = "Redacted GitOps JSON (*.json)|*.json",
            FileName = "GPO-GitOps-redacted.json",
            AddExtension = true
        };
        if (save.ShowDialog(this) != true)
            return Task.FromResult("Redacted GitOps export canceled.");
        GpoGitOpsReviewService.SaveJson(save.FileName, manifest);
        return Task.FromResult("GITOPS FINGERPRINT SAVED\n" +
            "Local file: " + save.FileName +
            "\nKey ID: " + manifest.KeyId +
            "\nRows: " + manifest.SourceRecords +
            "\nCoverage: " + (manifest.PartialCoverage ? "PARTIAL (approval blocked)" : "bounded scan") +
            "\n\nNo raw policy names, domains, paths or values are included. " +
            "The same Windows user's DPAPI-protected key is required for comparable captures. " +
            "Git publishing, approvals and any domain changes are always manual.");
    }

    private Task<string> CreateGitOpsReviewAsync()
    {
        var baselineFile = new OpenFileDialog
        {
            Title = "Select an earlier GitOps fingerprint manifest for this exact GPO",
            Filter = "Redacted GitOps JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (baselineFile.ShowDialog(this) != true)
            return Task.FromResult("Baseline selection canceled.");
        var before = GpoGitOpsReviewService.LoadManifest(baselineFile.FileName);
        var current = GpoGitOpsExportService.Capture(Source());
        _lastGitOpsReview = GpoGitOpsReviewService.CreateRequest(before, current);

        var save = new SaveFileDialog
        {
            Title = "Save exact redacted review request for separate approval",
            Filter = "GitOps review JSON (*.json)|*.json",
            FileName = "GPO-GitOps-review-request.json",
            AddExtension = true
        };
        var saved = save.ShowDialog(this) == true;
        if (saved)
            GpoGitOpsReviewService.SaveJson(save.FileName, _lastGitOpsReview);
        return Task.FromResult(GpoGitOpsReviewService.ReviewSummary(_lastGitOpsReview) +
            (saved ? "\n\nSaved locally: " + save.FileName :
                "\n\nThe request was not saved; no artifact was published."));
    }

    private Task<string> OpenGitOpsRequestAsync()
    {
        var file = new OpenFileDialog
        {
            Title = "Open untrusted GitOps review request (bounded validation)",
            Filter = "GitOps review JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (file.ShowDialog(this) != true)
            return Task.FromResult("Review selection canceled.");
        _lastGitOpsReview = GpoGitOpsReviewService.LoadRequest(file.FileName);
        return Task.FromResult(GpoGitOpsReviewService.ReviewSummary(_lastGitOpsReview));
    }

    private Task<string> ListReviewCertificatesAsync()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var lines = store.Certificates.OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey &&
                        DateTime.UtcNow >= c.NotBefore.ToUniversalTime() &&
                        DateTime.UtcNow <= c.NotAfter.ToUniversalTime())
            .Take(100)
            .Select(c => "SHA-256: " +
                Convert.ToHexString(SHA256.HashData(c.RawData)) +
                "\nSubject: " + c.Subject +
                "\nExpires: " + c.NotAfter.ToUniversalTime().ToString("O"));
        return Task.FromResult("LOCAL REVIEWER CERTIFICATES (CurrentUser/My)\n" +
            "Use the full SHA-256 certificate fingerprint, NOT Windows' SHA-1 thumbprint. " +
            "Use independently distributed trusted pins when VERIFYING a receipt.\n\n" +
            string.Join("\n\n", lines) +
            "\n\nNo certificate private key is exported.");
    }

    private Task<string> SignGitOpsReviewAsync()
    {
        var request = _lastGitOpsReview ??
            throw new InvalidOperationException(
                "Create or open a GitOps review request first.");
        var digest = GpoGitOpsReviewService.RequestSha256(request);
        var answer = MessageBox.Show(this,
            "Choose YES to APPROVE or NO to REJECT the exact review below.\n\n" +
            "Request SHA-256:\n" + digest + "\n" +
            "Changed opaque identities: " + request.Changes.Count + "\n" +
            "All writes to GPO/AD/SYSVOL remain forbidden by this action. " +
            "An approval never performs deployment.\n\n" +
            "YES = Approve   NO = Reject   CANCEL = Stop",
            "Signed human decision", MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel)
            return Task.FromResult("Signing canceled.");
        var approve = answer == MessageBoxResult.Yes;
        if (approve && !GpoGitOpsReviewService.CanApprove(request))
            throw new InvalidOperationException(
                "Approval blocked: incomplete capture or no observed change.");

        var pin = new InputDialog("Certificate for reviewer signature",
            "Enter full SHA-256 of a local signing certificate in CurrentUser/My " +
            "(use 'List signing certificates' to obtain it):", "")
        { Owner = this };
        if (pin.ShowDialog() != true)
            return Task.FromResult("Signing canceled; no certificate key accessed.");
        var receipt = GpoGitOpsReviewService.Sign(request, pin.Value.Trim(), approve);
        var file = new SaveFileDialog
        {
            Title = "Save reviewer signature (public certificate, no private key)",
            Filter = "Signed review receipt (*.json)|*.json",
            FileName = "GPO-GitOps-signed-decision.json",
            AddExtension = true
        };
        if (file.ShowDialog(this) != true)
            return Task.FromResult("Signed receipt was not saved. GPO remains unchanged.");
        GpoGitOpsReviewService.SaveJson(file.FileName, receipt);
        return Task.FromResult("SIGNED GITOPS DECISION\n" +
            "Decision: " + receipt.Decision +
            "\nRequest digest: " + receipt.RequestSha256 +
            "\nCertificate SHA-256: " + receipt.CertificateSha256 +
            "\nLocal file: " + file.FileName +
            "\n\nNo GPO write, automatic deployment, Git push or certification " +
            "of reviewer permissions has occurred.");
    }

    private Task<string> VerifyGitOpsReviewAsync()
    {
        var request = _lastGitOpsReview ??
            throw new InvalidOperationException(
                "Create or open the original exact GitOps review request first.");
        var file = new OpenFileDialog
        {
            Title = "Select the signed reviewer decision for this exact request",
            Filter = "Signed review receipt (*.json)|*.json",
            CheckFileExists = true
        };
        if (file.ShowDialog(this) != true)
            return Task.FromResult("Signature verification canceled.");
        var receipt = GpoGitOpsReviewService.LoadDecision(file.FileName);
        var pin = new InputDialog("Verify independent certificate trust",
            "Enter the reviewer's full SHA-256 certificate pin obtained " +
            "from a trusted independent source (not the receipt itself):", "")
        { Owner = this };
        if (pin.ShowDialog() != true)
            return Task.FromResult("Signature verification canceled.");
        return Task.FromResult(GpoGitOpsReviewService.Verify(
            request, receipt, pin.Value.Trim()));
    }

    private Task<string> ExportDpapiSourceAsync()
    {
        var source = Source();
        if (MessageBox.Show(this,
                "Export confidential, potentially sensitive GPO source records " +
                "encrypted for this Windows user with DPAPI CurrentUser?\n\n" +
                "It cannot normally be decrypted by another user/computer. " +
                "Do NOT put it in Git or upload it. Anonymized GitOps JSON is " +
                "the correct format for code review.\n\nProceed?",
                "Protected source export", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return Task.FromResult("Protected source export canceled.");
        var file = new SaveFileDialog
        {
            Title = "Encrypted DPAPI source - NEVER COMMIT TO GIT",
            Filter = "DPAPI protected GPO source (*.gposesdpapi)|*.gposesdpapi",
            FileName = "GPO-CONFIDENTIAL-source.gposesdpapi",
            AddExtension = true
        };
        if (file.ShowDialog(this) != true)
            return Task.FromResult("Protected source export canceled.");
        GpoProtectedExportService.Export(file.FileName, source);
        return Task.FromResult("ENCRYPTED LOCAL SOURCE EXPORTED\n" +
            file.FileName +
            "\nDPAPI CurrentUser. Raw policy data is inside encrypted payload. " +
            "No plaintext staging file, Git push or GPO write occurred. " +
            "The file is confidential, not a portable backup.");
    }

    private Task<string> InspectDpapiSourceAsync()
    {
        var open = new OpenFileDialog
        {
            Title = "Inspect DPAPI source using current Windows user",
            Filter = "DPAPI protected GPO source (*.gposesdpapi)|*.gposesdpapi",
            CheckFileExists = true
        };
        if (open.ShowDialog(this) != true)
            return Task.FromResult("Protected source inspection canceled.");
        var restored = GpoProtectedExportService.Import(open.FileName);
        // Intentionally do not set _active: an encrypted standalone source is
        // not a GPMC backup and must not become a live-GPO editing context.
        return Task.FromResult("DPAPI SOURCE DECRYPTION VERIFIED (READ ONLY)\n" +
            "GPO source records: " + restored.Rows.Count +
            "\nFiles tracked: " + restored.Files.Count +
            "\nPartial: " + restored.IsPartial +
            "\n\nThe decrypted record values remain in memory only for this check. " +
            "They were not copied to the report, synced, restored or applied.");
    }
}
