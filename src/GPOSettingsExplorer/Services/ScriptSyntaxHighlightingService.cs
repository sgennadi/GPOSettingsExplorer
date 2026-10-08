using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Runtime XSHD definitions. All colors originate in UiStyle / WindowsCompact.xaml.
/// No scripts are ever executed while highlighting.
/// </summary>
public static class ScriptSyntaxHighlightingService
{
    public static IHighlightingDefinition? ForFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (ScriptSyntaxService.IsPowerShell(fileName))
            return Load(PowerShellDefinition());
        if (ScriptSyntaxService.IsBatch(fileName))
            return Load(BatchDefinition());
        return extension.ToLowerInvariant() switch
        {
            ".js" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            ".vbs" => HighlightingManager.Instance.GetDefinition("VB"),
            ".wsf" or ".hta" => HighlightingManager.Instance.GetDefinition("HTML"),
            ".psd1" => Load(PowerShellDefinition()),
            _ => null
        };
    }

    private static IHighlightingDefinition Load(string definition)
    {
        using var reader = XmlReader.Create(new StringReader(definition));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static string CommonColors() => $"""
        <Color name="Comment" foreground="{UiStyle.ToRgbHex(UiStyle.ScriptCommentBrush)}"/>
        <Color name="String" foreground="{UiStyle.ToRgbHex(UiStyle.ScriptStringBrush)}"/>
        <Color name="Keyword" foreground="{UiStyle.ToRgbHex(UiStyle.ScriptKeywordBrush)}" fontWeight="bold"/>
        <Color name="Variable" foreground="{UiStyle.ToRgbHex(UiStyle.ScriptVariableBrush)}"/>
        <Color name="Number" foreground="{UiStyle.ToRgbHex(UiStyle.ScriptNumberBrush)}"/>
        """;

    private static string BatchDefinition() => $"""
        <SyntaxDefinition name="GPO Batch / CMD" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
        {CommonColors()}
          <RuleSet ignoreCase="true">
            <Span color="Comment" begin="^\s*(?:::|rem\b)" />
            <Span color="String" begin="&quot;" end="&quot;" />
            <Rule color="Keyword">\b(?:if|else|for|in|do|goto|call|exit|start|set|setlocal|endlocal|echo|choice|shift|pause|pushd|popd|cd|md|mkdir|rd|rmdir|copy|move|del|erase|type|title|cls|not|exist|defined|errorlevel|equ|neq|lss|leq|gtr|geq)\b</Rule>
            <Rule color="Variable">(?:%%?[A-Za-z0-9_]+|%[~*0-9A-Za-z_:-]+%|![A-Za-z_][A-Za-z0-9_]*!)</Rule>
            <Rule color="Number">\b\d+\b</Rule>
            <Rule color="Keyword">^\s*:[A-Za-z0-9_.-]+\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static string PowerShellDefinition() => $$"""
        <SyntaxDefinition name="GPO PowerShell" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
        {{CommonColors()}}
          <RuleSet ignoreCase="true">
            <Span color="Comment" multiline="true" begin="&lt;#" end="#&gt;"/>
            <Span color="Comment" begin="#"/>
            <Span color="String" multiline="true" begin="&quot;" end="&quot;"/>
            <Span color="String" multiline="true" begin="'" end="'"/>
            <Rule color="Keyword">\b(?:param|begin|process|end|function|filter|workflow|class|enum|using|namespace|if|elseif|else|switch|foreach|for|while|do|until|break|continue|return|throw|try|catch|finally|trap|in|where|select|new|exit)\b</Rule>
            <Rule color="Variable">\$[A-Za-z_][A-Za-z0-9_:]*</Rule>
            <Rule color="Variable">(?:\$\{[^}]+\}|\$\?|\$\^|\$\$)</Rule>
            <Rule color="Keyword">-(?:eq|ne|lt|le|gt|ge|like|notlike|match|notmatch|contains|notcontains|in|notin|is|isnot|and|or|not|xor|band|bor|bnot)\b</Rule>
            <Rule color="Number">\b(?:0[xX][0-9A-Fa-f]+|\d+(?:\.\d+)?)\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
