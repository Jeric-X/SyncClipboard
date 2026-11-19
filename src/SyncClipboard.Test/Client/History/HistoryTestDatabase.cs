using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Test.Client.History;

internal static class HistoryTestDatabase
{
    public static Func<HistoryDbContext> CreateFactory(HistoryDbContext database) => () =>
    {
        var context = new HistoryDbContext();
        context.Database.SetDbConnection(database.Database.GetDbConnection(), contextOwnsConnection: false);
        return context;
    };
}
