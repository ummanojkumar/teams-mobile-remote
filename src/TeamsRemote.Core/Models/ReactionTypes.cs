namespace TeamsRemote.Core.Models;

public static class ReactionTypes
{
    public const string Like = "like";
    public const string Love = "love";
    public const string Applause = "applause";
    public const string Laugh = "laugh";
    public const string Surprise = "surprise";

    public static readonly IReadOnlySet<string> ValidReactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Like,
        Love,
        Applause,
        Laugh,
        Surprise
    };

    public static bool IsValid(string? reaction)
    {
        return !string.IsNullOrWhiteSpace(reaction) && ValidReactions.Contains(reaction.Trim());
    }
}
