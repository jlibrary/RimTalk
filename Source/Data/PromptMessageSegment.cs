using RimTalk.Source.Data;

namespace RimTalk.Data;

public class PromptMessageSegment
{
    public string EntryId { get; set; }
    public string EntryName { get; set; }
    public Role Role { get; set; }
    public string Content { get; set; }

    public PromptMessageSegment()
    {
    }

    public PromptMessageSegment(string entryId, string entryName, Role role, string content)
    {
        EntryId = entryId;
        EntryName = entryName;
        Role = role;
        Content = content;
    }
}
