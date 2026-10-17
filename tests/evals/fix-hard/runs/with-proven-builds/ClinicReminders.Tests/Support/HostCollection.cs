namespace ClinicReminders.Tests.Support;

// Quartz keeps the first logger factory it sees in a static; hosts built in parallel trip over each other.
[CollectionDefinition(nameof(HostCollection), DisableParallelization = true)]
public sealed class HostCollection;
