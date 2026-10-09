using System.Linq.Expressions;

namespace SkillSamples.Patterns;

public sealed record AppointmentRow(int Id, int ClinicId, DateTimeOffset StartsAt, AppointmentStatus Status);

// Named, reusable query rules. They are expressions, so EF Core turns them into SQL.
public static class AppointmentSpecs
{
    public static readonly Expression<Func<AppointmentRow, bool>> NotCancelled =
        a => a.Status != AppointmentStatus.Cancelled;

    public static Expression<Func<AppointmentRow, bool>> ForClinic(int clinicId) => a => a.ClinicId == clinicId;

    public static Expression<Func<AppointmentRow, bool>> StartingFrom(DateTimeOffset from) => a => a.StartsAt >= from;
}

public static class ExpressionComposition
{
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> rule) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(rule.Body), rule.Parameters);

    // Rebind the right side to the left side's parameter. Expression.Invoke would also compile, but EF
    // Core can't translate an invocation to SQL, so the query fails at runtime.
    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right, Func<Expression, Expression, BinaryExpression> op)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(op(left.Body, rightBody), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
