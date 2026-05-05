using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using Sentry;

namespace BuzzKeepr.API.GraphQL;

/// <summary>
/// Hot Chocolate fires <c>POST /graphql</c> as the only HTTP route, so by default Sentry's
/// Performance dashboard groups every GraphQL operation into one transaction. This listener
/// renames the active Sentry transaction to <c>graphql {operationName}</c> as soon as the
/// operation name is known — so each query/mutation shows up separately and you can compare
/// (e.g.) <c>requestEmailSignIn</c> latency to <c>startInstantCriminalCheck</c>.
/// </summary>
public sealed class SentryGraphQLDiagnosticListener : ExecutionDiagnosticEventListener
{
    public override IDisposable ExecuteOperation(RequestContext context)
    {
        var operationName = context.Request.OperationName ?? "anonymous";

        SentrySdk.ConfigureScope(scope =>
        {
            if (scope.Transaction is { } transaction)
                transaction.Name = $"graphql {operationName}";
        });

        return base.ExecuteOperation(context);
    }
}
