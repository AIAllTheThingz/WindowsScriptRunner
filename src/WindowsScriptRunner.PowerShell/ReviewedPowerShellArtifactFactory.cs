namespace WindowsScriptRunner.PowerShell;

internal sealed record ReviewedPowerShellArtifact(
    string ArtifactName,
    string RelativePath,
    string Sha256,
    IReadOnlyCollection<string> AllowedParameterNames);

internal interface IReviewedPowerShellArtifactFactory
{
    TrustedPowerShellScript Resolve(ReviewedPowerShellArtifact artifact);
}

internal sealed class ReviewedPowerShellArtifactFactory(
    Microsoft.Extensions.Options.IOptions<PowerShellExecutionOptions> options,
    IPowerShellScriptTrustValidator trustValidator) : IReviewedPowerShellArtifactFactory
{
    private readonly string _allowedRoot = Path.GetFullPath(
        options.Value.AllowedScriptRoot ??
        throw new PowerShellScriptTrustException(
            "The trusted script root is not configured."));

    public TrustedPowerShellScript Resolve(ReviewedPowerShellArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var relativePath = artifact.RelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(relativePath) ||
            relativePath.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new PowerShellScriptTrustException(
                "The reviewed artifact path is invalid.");
        }

        var trusted = new TrustedPowerShellScript(
            artifact.ArtifactName,
            Path.GetFullPath(Path.Combine(_allowedRoot, relativePath)),
            artifact.Sha256,
            artifact.AllowedParameterNames);
        _ = trustValidator.ValidateImmediatelyBeforeLaunch(trusted);
        return trusted;
    }
}
