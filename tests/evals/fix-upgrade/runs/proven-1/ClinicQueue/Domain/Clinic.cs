namespace ClinicQueue.Domain;

public sealed class Clinic(string name)
{
    public int Id { get; private set; }
    public string Name { get; private set; } = name;
}
