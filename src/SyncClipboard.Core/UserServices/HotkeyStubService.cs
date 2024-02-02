using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.UserServices;

internal class HotkeyStubService : Service
{
    private readonly HotkeyManager _hotkeyManager;
    private readonly INotification _notifyer;
    private UniqueCommandCollection CommandCollection => new UniqueCommandCollection("快捷键测试1", "\uEBD3")
    {
        Commands = new List<UniqueCommand> { Command1, Command2 }
    };

    private static readonly Guid Guid1 = Guid.Parse("66015F94-715E-40CA-B55B-7479A5D7FC23");
    UniqueCommand Command1 => new UniqueCommand("快捷键1", Guid1, () => _notifyer.SendText("快捷键1", ""));
    private static readonly Guid Guid2 = Guid.Parse("66025F94-725E-40CA-B55B-7479A5D7FC23");
    UniqueCommand Command2 => new UniqueCommand("快捷键2", Guid2, () => _notifyer.SendText("快捷键2", ""));

    private UniqueCommandCollection CommandCollection2 => new UniqueCommandCollection("快捷键测试2", "\uF406")
    {
        Commands = new List<UniqueCommand> { Command3, Command4 }
    };

    private static readonly Guid Guid3 = Guid.Parse("66035F94-735E-40CA-B55B-7479A5D7FC23");
    UniqueCommand Command3 => new UniqueCommand("快捷键3", Guid3, () => _notifyer.SendText("快捷键3", ""));
    private static readonly Guid Guid4 = Guid.Parse("66045F94-745E-40CA-B55B-7479A5D7FC43");
    UniqueCommand Command4 => new UniqueCommand("快捷键4", Guid4, () => _notifyer.SendText("快捷键4", ""));

    public HotkeyStubService(HotkeyManager hotkeyManger, INotification notifyer)
    {
        _hotkeyManager = hotkeyManger;
        _notifyer = notifyer;
    }

    protected override void StartService()
    {
        _hotkeyManager.RegisterCommands(CommandCollection);
        _hotkeyManager.RegisterCommands(CommandCollection2);
    }

    protected override void StopSerivce()
    { }
}
