namespace WindowsScriptRunner.Automation;

public sealed class LocalHostInventoryPackageOptions
{
    public const string SectionName = "Automation:LocalHostInventory";

    public bool Enabled { get; set; }

    public bool RegisterOnStartup { get; set; }
}
