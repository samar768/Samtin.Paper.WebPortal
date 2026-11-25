namespace Techno.Paper.Api.Services;

public interface IWriteCheckpoint
{
    void Reached(string stage);
}

public sealed class WriteCheckpoint : IWriteCheckpoint
{
    public void Reached(string stage) { }
}
