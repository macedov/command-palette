using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CommandPalette;

internal sealed class SystemTrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _applicationIcon;

    public SystemTrayService(
        Action openPalette,
        Action openSettings,
        Action openPresets,
        Action openHelp,
        Action exit)
    {
        var renderer = new DarkMenuRenderer();

        _menu = new Forms.ContextMenuStrip
        {
            BackColor = Drawing.Color.FromArgb(31, 31, 31),
            ForeColor = Drawing.Color.FromArgb(226, 226, 226),
            Renderer = renderer,
            ShowImageMargin = false,
            Padding = new Forms.Padding(2)
        };
        _menu.Items.Add(CreateItem("Open Command Palette", openPalette));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("Settings", openSettings));
        _menu.Items.Add(CreateItem("Presets", openPresets));
        _menu.Items.Add(CreateItem("Help", openHelp));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("Exit", exit));

        _applicationIcon = ApplicationIconProvider.CreateIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "Command Palette",
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => openPalette();
    }

    private static Forms.ToolStripMenuItem CreateItem(
        string text,
        Action action)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            BackColor = Drawing.Color.FromArgb(31, 31, 31),
            ForeColor = Drawing.Color.FromArgb(226, 226, 226),
            Padding = new Forms.Padding(8, 4, 8, 4)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class DarkMenuColorTable :
        Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Surface =
            Drawing.Color.FromArgb(31, 31, 31);

        private static readonly Drawing.Color Hover =
            Drawing.Color.FromArgb(52, 52, 52);

        private static readonly Drawing.Color Border =
            Drawing.Color.FromArgb(72, 72, 72);

        public DarkMenuColorTable()
        {
            UseSystemColors = false;
        }

        public override Drawing.Color ToolStripDropDownBackground =>
            Surface;

        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Border;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin =>
            Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd =>
            Hover;
        public override Drawing.Color MenuItemPressedGradientBegin =>
            Hover;
        public override Drawing.Color MenuItemPressedGradientMiddle =>
            Hover;
        public override Drawing.Color MenuItemPressedGradientEnd =>
            Hover;
        public override Drawing.Color ImageMarginGradientBegin =>
            Surface;
        public override Drawing.Color ImageMarginGradientMiddle =>
            Surface;
        public override Drawing.Color ImageMarginGradientEnd =>
            Surface;
        public override Drawing.Color SeparatorDark =>
            Drawing.Color.FromArgb(67, 67, 67);
        public override Drawing.Color SeparatorLight => Surface;
    }

    private sealed class DarkMenuRenderer :
        Forms.ToolStripProfessionalRenderer
    {
        private static readonly Drawing.Color Surface =
            Drawing.Color.FromArgb(31, 31, 31);

        private static readonly Drawing.Color Hover =
            Drawing.Color.FromArgb(52, 52, 52);

        private static readonly Drawing.Color Border =
            Drawing.Color.FromArgb(72, 72, 72);

        private static readonly Drawing.Color Text =
            Drawing.Color.FromArgb(232, 232, 232);

        public DarkMenuRenderer()
            : base(new DarkMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(
            Forms.ToolStripItemRenderEventArgs e)
        {
            var bounds = new Drawing.Rectangle(
                Drawing.Point.Empty,
                e.Item.Size
            );

            using var background = new Drawing.SolidBrush(
                e.Item.Selected ? Hover : Surface
            );
            e.Graphics.FillRectangle(background, bounds);

            if (!e.Item.Selected)
                return;

            using var border = new Drawing.Pen(Border);
            e.Graphics.DrawRectangle(
                border,
                0,
                0,
                Math.Max(0, bounds.Width - 1),
                Math.Max(0, bounds.Height - 1)
            );
        }

        protected override void OnRenderItemText(
            Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? Text
                : Drawing.Color.FromArgb(120, 120, 120);
            base.OnRenderItemText(e);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _applicationIcon.Dispose();
    }
}
