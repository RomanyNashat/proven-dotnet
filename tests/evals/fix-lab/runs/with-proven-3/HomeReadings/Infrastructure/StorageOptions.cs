namespace HomeReadings.Infrastructure;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string PhotoRoot { get; set; } = "";
}
