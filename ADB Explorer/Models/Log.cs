namespace ADB_Explorer.Models;

internal class Log
{
    public string Content { get; set; }

    public DateTime TimeStamp { get; set; }

    public Log(string content, DateTime? timeStamp = null)
    {
        Content = content;
        TimeStamp = timeStamp is null ? DateTime.Now : timeStamp.Value;
    }

    public override string ToString()
    {
        return $"{TimeStamp:HH:mm:ss:fff} ⁞ {Content}";
    }
}
