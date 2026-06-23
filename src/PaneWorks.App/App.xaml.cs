using PaneWorks.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;

namespace PaneWorks.App;

public partial class App : Wpf.Application
{
    // Tray colors are centralized here so the editor and tray stay visually consistent.
    private static readonly Drawing.Color TrayBackColor = Drawing.Color.FromArgb(21, 27, 42);
    private static readonly Drawing.Color TrayBackHoverColor = Drawing.Color.FromArgb(47, 128, 237);
    private static readonly Drawing.Color TrayBackCheckedColor = Drawing.Color.FromArgb(47, 163, 107);
    private static readonly Drawing.Color TrayBorderColor = Drawing.Color.FromArgb(63, 255, 255, 255);
    private static readonly Drawing.Color TrayTextColor = Drawing.Color.FromArgb(248, 251, 255);
    private static readonly Drawing.Color TrayMutedTextColor = Drawing.Color.FromArgb(205, 239, 244, 255);

    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private bool _isExitRequested;

    protected override void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown;
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        InitializeNotifyIcon();
        _mainWindow.Show();
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        base.OnExit(e);
    }

    public bool IsExitRequested => _isExitRequested;

    public void CancelExitRequest()
    {
        _isExitRequested = false;
    }

    public void MinimizeMainWindowToTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.WindowState = Wpf.WindowState.Minimized;
        _mainWindow.Hide();
    }

    public void RestoreMainWindowFromTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Show();
        _mainWindow.WindowState = Wpf.WindowState.Maximized;
        _mainWindow.Activate();
    }

    private void InitializeNotifyIcon()
    {
        var menu = CreateTrayMenu();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "PaneWorks",
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreMainWindowFromTray();
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Drawing.Font("Microsoft YaHei UI", 10F),
            Renderer = new TrayMenuRenderer()
        };

        menu.BackColor = TrayBackColor;
        menu.ForeColor = TrayTextColor;
        menu.Padding = new Forms.Padding(8);
        menu.Opening += TrayMenu_Opening;
        return menu;
    }

    private void TrayMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not Forms.ContextMenuStrip menu)
        {
            return;
        }

        RebuildTrayMenu(menu);
    }

    private void RebuildTrayMenu(Forms.ContextMenuStrip menu)
    {
        menu.SuspendLayout();
        menu.Items.Clear();

        menu.Items.Add(CreateMenuItem("打开 PaneWorks", (_, _) => RestoreMainWindowFromTray(), bold: true));
        menu.Items.Add(CreateLayoutsMenuItem());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("退出", (_, _) => ExitFromTray()));

        menu.ResumeLayout();
    }

    private Forms.ToolStripMenuItem CreateLayoutsMenuItem()
    {
        var item = CreateMenuItem("快速切换吸附布局");
        item.ForeColor = TrayTextColor;
        item.DropDown.BackColor = TrayBackColor;

        if (_mainWindow is null)
        {
            item.Enabled = false;
            return item;
        }

        var layouts = _mainWindow.GetTrayLayoutItems();
        var activeLayoutId = _mainWindow.GetActiveSnapLayoutId();

        if (layouts.Count == 0)
        {
            var emptyItem = CreateMenuItem("暂无已保存布局");
            emptyItem.Enabled = false;
            emptyItem.ForeColor = TrayMutedTextColor;
            item.DropDownItems.Add(emptyItem);
            return item;
        }

        foreach (var layout in layouts)
        {
            var layoutItem = CreateMenuItem(layout.Name, (_, _) => _mainWindow.SwitchSnapLayoutFromTray(layout.Id));
            layoutItem.Checked = string.Equals(layout.Id, activeLayoutId, StringComparison.OrdinalIgnoreCase);
            layoutItem.ToolTipText = layout.Description;
            item.DropDownItems.Add(layoutItem);
        }

        return item;
    }

    private static Forms.ToolStripMenuItem CreateMenuItem(
        string text,
        EventHandler? onClick = null,
        bool bold = false)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            AutoSize = false,
            Height = 34,
            Width = 230,
            Padding = new Forms.Padding(12, 6, 12, 6),
            Margin = new Forms.Padding(0, 2, 0, 2),
            ForeColor = TrayTextColor,
            BackColor = TrayBackColor,
            Font = new Drawing.Font("Microsoft YaHei UI", bold ? 10F : 9.5F, bold ? Drawing.FontStyle.Bold : Drawing.FontStyle.Regular)
        };

        if (onClick is not null)
        {
            item.Click += onClick;
        }

        return item;
    }

    private void ExitFromTray()
    {
        if (_mainWindow is null)
        {
            Shutdown();
            return;
        }

        _isExitRequested = true;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Show();
        _mainWindow.WindowState = Wpf.WindowState.Maximized;
        _mainWindow.Close();

        if (_isExitRequested)
        {
            Shutdown();
        }
    }

    private sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer()
            : base(new TrayMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(TrayBorderColor);
            var bounds = new Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(TrayBorderColor);
            var y = e.Item.ContentRectangle.Top + (e.Item.ContentRectangle.Height / 2);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? TrayTextColor
                : TrayMutedTextColor;

            e.TextFont = e.Item.Font;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = TrayTextColor;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(TrayTextColor);
            e.Graphics.FillEllipse(brush, new Drawing.Rectangle(10, 10, 8, 8));
        }
    }

    private sealed class TrayMenuColorTable : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => TrayBackColor;
        public override Drawing.Color ImageMarginGradientBegin => TrayBackColor;
        public override Drawing.Color ImageMarginGradientMiddle => TrayBackColor;
        public override Drawing.Color ImageMarginGradientEnd => TrayBackColor;
        public override Drawing.Color MenuBorder => TrayBorderColor;
        public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
        public override Drawing.Color MenuItemSelected => TrayBackHoverColor;
        public override Drawing.Color MenuItemSelectedGradientBegin => TrayBackHoverColor;
        public override Drawing.Color MenuItemSelectedGradientEnd => TrayBackHoverColor;
        public override Drawing.Color MenuItemPressedGradientBegin => TrayBackColor;
        public override Drawing.Color MenuItemPressedGradientMiddle => TrayBackColor;
        public override Drawing.Color MenuItemPressedGradientEnd => TrayBackColor;
        public override Drawing.Color ButtonSelectedBorder => Drawing.Color.Transparent;
        public override Drawing.Color CheckBackground => TrayBackCheckedColor;
        public override Drawing.Color CheckPressedBackground => TrayBackCheckedColor;
        public override Drawing.Color CheckSelectedBackground => TrayBackCheckedColor;
        public override Drawing.Color SeparatorDark => TrayBorderColor;
        public override Drawing.Color SeparatorLight => TrayBorderColor;
    }
}
