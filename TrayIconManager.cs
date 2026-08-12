using System.Drawing;
using System.Windows.Forms;

namespace GuguPet;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _roamItem;
    private readonly ToolStripMenuItem _startupItem;

    public TrayIconManager(
        Action openControls,
        Action feedCookie,
        Action<bool> setRoaming,
        Action<bool> setStartup,
        Action exit,
        bool roaming,
        bool startup)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.T("打开控制台"), null, (_, _) => openControls());
        menu.Items.Add(LocalizationService.T("投喂饼干"), null, (_, _) => feedCookie());
        menu.Items.Add(new ToolStripSeparator());
        _roamItem = new ToolStripMenuItem(LocalizationService.T("桌面随机走动")) { Checked = roaming, CheckOnClick = true };
        _roamItem.CheckedChanged += (_, _) => setRoaming(_roamItem.Checked);
        menu.Items.Add(_roamItem);
        _startupItem = new ToolStripMenuItem(LocalizationService.T("开机自动启动")) { Checked = startup, CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => setStartup(_startupItem.Checked);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(LocalizationService.T("退出咕嘎"), null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = LocalizationService.T("咕嘎桌宠"),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => openControls();
    }

    public void SetRoaming(bool value)
    {
        if (_roamItem.Checked != value) _roamItem.Checked = value;
    }

    public void SetStartup(bool value)
    {
        if (_startupItem.Checked != value) _startupItem.Checked = value;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
