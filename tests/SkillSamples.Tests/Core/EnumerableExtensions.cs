namespace SkillSamples.Core;

// C# 14 extension blocks: extension properties as well as methods. A constraint goes on the block,
// not on a member.
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public bool IsEmpty => !source.Any();
    }

    extension<T>(IEnumerable<T?> source) where T : class
    {
        public IEnumerable<T> WhereNotNull() => source.OfType<T>();
    }
}
