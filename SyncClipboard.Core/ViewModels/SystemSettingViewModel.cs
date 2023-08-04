using SyncClipboard.Core.Commons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.Core.ViewModels;

public class SystemSettingViewModel
{
    public string Version => "v" + Env.VERSION;
}
