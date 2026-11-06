// The core test assembly has no Dalamud/SQLite reference. This test-only type
// exercises the same exception name and message as the SQLite provider in game.
namespace SQLite;

internal sealed class SQLiteException(string message) : Exception(message)
{
}
