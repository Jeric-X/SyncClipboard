namespace SyncClipboard.Core.Models;

public record class UniqueCommandCollection
{
    public string Name { get; init; }
    public string FontIcon { get; init; }
    public List<UniqueCommand> Commands { get; init; }

    public UniqueCommandCollection(string name, string fontIcon, IEnumerable<UniqueCommand> commands)
    {
        Name = name;
        FontIcon = fontIcon;
        Commands = commands.ToList();
    }

    public UniqueCommandCollection(string name, string fontIcon, List<UniqueCommand> commands)
    {
        Name = name;
        FontIcon = fontIcon;
        Commands = commands;
    }

    public UniqueCommandCollection(string name, string fontIcon, UniqueCommand command)
    {
        Name = name;
        FontIcon = fontIcon;
        Commands = [command];
    }

    public UniqueCommandCollection(string name, string fontIcon)
    {
        Name = name;
        FontIcon = fontIcon;
        Commands = [];
    }
}
