using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forma.Core;

public sealed class JobStore
{
    private readonly string ConnectionString;
    public JobStore(string Folder)
    {
        Directory.CreateDirectory(Folder);
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Folder, "history.db") }.ToString();
        using var Connection = Open();
        using var Command = Connection.CreateCommand();
        Command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS Jobs (Id TEXT PRIMARY KEY, Body TEXT NOT NULL);";
        Command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var Connection = new SqliteConnection(ConnectionString); Connection.Open(); return Connection; }
    public void Save(MediaJob Job)
    {
        using var Connection = Open();
        using var Command = Connection.CreateCommand();
        Command.CommandText = "INSERT INTO Jobs(Id,Body) VALUES($Id,$Body) ON CONFLICT(Id) DO UPDATE SET Body=$Body";
        Command.Parameters.AddWithValue("$Id", Job.Id);
        Command.Parameters.AddWithValue("$Body", JsonSerializer.Serialize(Job));
        Command.ExecuteNonQuery();
    }
    public IReadOnlyList<MediaJob> Load()
    {
        using var Connection = Open();
        using var Command = Connection.CreateCommand();
        Command.CommandText = "SELECT Body FROM Jobs ORDER BY rowid DESC";
        using var Reader = Command.ExecuteReader();
        var Jobs = new List<MediaJob>();
        while (Reader.Read())
        {
            try { var Job = JsonSerializer.Deserialize<MediaJob>(Reader.GetString(0)); if (Job is not null) Jobs.Add(Job); }
            catch (JsonException) { }
        }
        return Jobs;
    }
}
