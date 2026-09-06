// Oblivionis — 关机/重启确认对话框（立即执行，无缓冲无撤销）
// .NET 8 WinForms 单文件发布：dotnet publish -c Release
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShutdownForm());
    }
}

public class ShutdownForm : Form
{
    // 配色（Ave Mujica《Oblivionis》深海夜色）
    static readonly Color ColBg1 = Color.FromArgb(26, 35, 64),      // #1A2340
                          ColBg2 = Color.FromArgb(18, 26, 48),      // #121A30
                          ColBorder = Color.FromArgb(46, 59, 96),   // #2E3B60
                          ColInk = Color.FromArgb(232, 236, 244),   // 主文字
                          ColDim = Color.FromArgb(154, 167, 194),   // 辅助文字
                          ColCyan = Color.FromArgb(94, 234, 212),   // 强调青
                          ColRose = Color.FromArgb(225, 29, 72),    // 关机钮
                          ColRoseHi = Color.FromArgb(244, 63, 94),  // 关机钮悬停
                          ColTrack = Color.FromArgb(42, 53, 84),    // 内嵌卡描边
                          ColBtnLn = Color.FromArgb(58, 71, 112),   // 幽灵钮描边
                          ColMoonA = Color.FromArgb(254, 243, 199), // 月亮亮面
                          ColMoonB = Color.FromArgb(234, 179, 8);   // 月亮暗面
    // 像素布局的缩放系数（设计基准 96 DPI）：高分屏上月亮、箭头随系统缩放等比放大
    float S => DeviceDpi / 96f;
    Rectangle MoonRect => new Rectangle((int)(28 * S), (int)(24 * S), (int)(42 * S), (int)(42 * S)); // 月牙热区

    bool confirmed;                        // 已投出关机/重启命令，间隔内禁止再投
    bool scheduled;                        // 已投出定时关机，可在「晚点睡」菜单取消
    bool dropdown = true, arrowHit;        // 忘却钮右半 ▼ 下拉区
    int menuClosedAt = -10000;             // 菜单刚关闭的时间戳，防点 ▼ 收起后瞬间重开
    Timer countdown;                       // 定时关机倒计时（1s tick）
    Timer stateTimer;                      // 瞬时状态文案：10s 后自动消失
    DateTime target;                       // 计划触发时刻
    Label lblState;
    Image sakiko;                          // Q版丰川祥子贴纸（嵌入资源）
    System.IO.Stream sakikoStream, voiceStream; // Image 依赖流存活，须保持引用
    SoundPlayer voice;                     // 祥子语音彩蛋

    public ShutdownForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        Text = "Oblivionis · 忘却时刻";   // 任务栏/Alt-Tab 标题（无边框不显示标题栏）
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);   // 任务栏/Alt-Tab 图标与 exe 一致
        Size = new Size(420, 318);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;   // 高分屏：像素布局随系统缩放等比放大
        BackColor = ColBg1;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Paint += OnPaint;
        Load += delegate { Region = new Region(Rounded(new Rectangle(0, 0, Width, Height), 14)); };
        MouseDown += OnFormMouseDown;

        var title = Label("Oblivionis · 忘却时刻", 12f, FontStyle.Bold, ColInk, 84, 22);
        var sub = Label("SHUTDOWN CONFIRMATION", 7.5f, FontStyle.Regular, ColDim, 86, 50);
        sub.Font = new Font("Segoe UI", 7.5f);
        title.MouseDown += Drag;
        sub.MouseDown += Drag;
        Label("确定让这台电脑进入忘却吗？", 10f, FontStyle.Bold, ColInk, 28, 84);
        Label("未保存的工作将会丢失。", 9f, FontStyle.Regular, ColDim, 28, 110);

        var info = new Panel { Bounds = new Rectangle(28, 138, 364, 76) };
        info.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var card = new Rectangle(0, 0, info.Width - 1, info.Height - 1);
            using (var path = Rounded(card, 10))
            using (var fill = new SolidBrush(Color.FromArgb(15, 22, 40)))
            using (var pen = new Pen(ColTrack))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            using (var b = new SolidBrush(ColInk))
                e.Graphics.DrawString("さあ始めよう、今宵のマスカレイド。",
                    new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), b, 20, 28);
        };

        lblState = Label("", 9f, FontStyle.Regular, ColCyan, 28, 288);
        lblState.TextAlign = ContentAlignment.MiddleCenter;
        lblState.Size = new Size(364, 20);
        stateTimer = new Timer { Interval = 10000 };
        stateTimer.Tick += delegate
        {
            stateTimer.Stop();
            lblState.Text = "";
        };

        var cancel = Ghost("再等等", 28, 1, ColInk, ColBtnLn);
        cancel.Click += delegate { Close(); };

        var restart = Ghost("重返舞会", 152, 2, ColCyan, ColCyan, true);
        restart.Click += delegate { FireShutdown("/r /t 0", "已执行立即重启（shutdown /r /t 0）"); };

        var confirm = Solid("忘却", 276, 0);
        var arrowFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        confirm.Paint += (s, e) =>
        {
            if (!dropdown) return;
            int ax = (int)(75 * S);
            TextRenderer.DrawText(e.Graphics, "▼", arrowFont, new Rectangle(ax, 0, (int)(39 * S), confirm.Height),
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        confirm.MouseMove += (s, e) => confirm.Cursor = dropdown && e.X >= (int)(75 * S) ? Cursors.Hand : Cursors.Default;
        confirm.MouseLeave += (s, e) => confirm.Cursor = Cursors.Default;
        confirm.MouseDown += (s, e) => arrowHit = dropdown && e.X >= (int)(75 * S);
        confirm.MouseUp += (s, e) => { if (!confirm.ClientRectangle.Contains(e.Location)) arrowHit = false; };
        confirm.Click += delegate
        {
            if (arrowHit) { arrowHit = false; ShowSleepMenu(confirm); return; }
            if (FireShutdown("/s /t 0", "已执行立即关机（shutdown /s /t 0）"))
            {
                dropdown = false;
                confirm.Text = "忘却进行中…";
            }
        };

        Controls.AddRange(new Control[] { title, sub, info, lblState, cancel, restart, confirm });

        // 右上角 Q版祥子贴纸；点击播放语音彩蛋
        var asm = GetType().Assembly;
        sakikoStream = asm.GetManifestResourceStream("sakiko.png");
        sakiko = Image.FromStream(sakikoStream);
        voiceStream = asm.GetManifestResourceStream("cn019.wav");
        voice = new SoundPlayer(voiceStream);
        voice.Load();
        var sticker = new PictureBox
        {
            Image = sakiko, Bounds = new Rectangle(314, 16, 98, 96),
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        sticker.Click += delegate { voice.Play(); };
        Controls.Add(sticker);
        sticker.BringToFront();

        AcceptButton = confirm;  // Enter = 关机
        CancelButton = cancel;   // Esc  = 关闭（安全默认）
        ActiveControl = cancel;  // 默认焦点防误触
        Shown += delegate { cancel.Focus(); };
    }

    // 幽灵钮（描边）
    static Button Ghost(string text, int x, int tab, Color fg, Color border, bool bold = false)
    {
        var b = new Button
        {
            Text = text,
            ForeColor = fg,
            BackColor = ColBg1,
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(x, 234, 114, 42),
            TabIndex = tab,
            Font = new Font("Microsoft YaHei UI", 9.5f, bold ? FontStyle.Bold : FontStyle.Regular)
        };
        b.FlatAppearance.BorderColor = border;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 46, 78);
        return b;
    }

    // 主行动钮（玫红实心）
    static Button Solid(string text, int x, int tab)
    {
        var b = new Button
        {
            Text = text,
            ForeColor = Color.White,
            BackColor = ColRose,
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(x, 234, 114, 42),
            TabIndex = tab,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ColRoseHi;
        return b;
    }

    // 投出立即类命令：仅在成功发出后锁定；失败保持界面可用并提示（常驻不消失）
    bool FireShutdown(string args, string okState)
    {
        if (confirmed) return false;
        if (!RunShutdown(args))
        {
            StickyText("执行失败：无法调用系统关机命令");
            return false;
        }
        confirmed = true;
        StateText(okState);
        return true;
    }

    // 瞬时状态：显示 10 秒后自动消失，不常驻影响观看
    void StateText(string text)
    {
        stateTimer.Stop();
        lblState.Text = text;
        stateTimer.Start();
    }

    // 常驻状态：错误信息不自动消失，保持可读
    void StickyText(string text)
    {
        stateTimer.Stop();
        lblState.Text = text;
    }

    // 覆盖式投递：先取消在途计划（无在途计划时的 1116 错误忽略），等其退出后再投新命令，
    // 避免新计划刚发出就被在途的 /a 取消
    bool ReplaceShutdown(string args)
    {
        try
        {
            using (var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/a")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                p?.WaitForExit(2000);
            }
        }
        catch { /* 无法启动时继续尝试投递新命令 */ }
        return RunShutdown(args);
    }

    // 「晚点睡」下拉菜单：定时关机选项（重选时长直接覆盖旧计划）+ 取消
    void ShowSleepMenu(Control anchor)
    {
        if (Environment.TickCount - menuClosedAt < 250) return;   // 刚点 ▼ 收起的不立刻重开
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Renderer = new ToolStripProfessionalRenderer(new MenuTheme()),
            BackColor = Color.FromArgb(15, 22, 40)
        };
        menu.Closed += delegate { menuClosedAt = Environment.TickCount; };

        var sleep = new ToolStripMenuItem("晚点睡") { ForeColor = ColInk };
        foreach (var (text, secs) in new[] { ("15 分钟后", 900), ("30 分钟后", 1800), ("1 小时后", 3600), ("2 小时后", 7200) })
        {
            var item = new ToolStripMenuItem(text) { ForeColor = ColInk };
            item.Click += delegate { Schedule(secs, text); };
            sleep.DropDownItems.Add(item);
        }
        sleep.DropDown.Renderer = menu.Renderer;
        sleep.DropDown.Font = menu.Font;
        ((ToolStripDropDownMenu)sleep.DropDown).ShowImageMargin = false;
        ((ToolStripDropDown)sleep.DropDown).BackColor = Color.FromArgb(15, 22, 40);

        var cancel = new ToolStripMenuItem("取消已定的关机") { ForeColor = ColInk, Enabled = scheduled };
        cancel.Click += delegate { CancelSchedule(); };

        menu.Items.Add(sleep);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(cancel);
        menu.Show(anchor, new Point(anchor.Width - 39, anchor.Height));
    }

    // 定时关机：计划可互相覆盖（先 /a 取消在途，再投新命令），重选时长一步完成；
    // 「忘却」「重返舞会」两个立即类按钮保持需先取消（防误触减速带）
    void Schedule(int secs, string text)
    {
        if (!ReplaceShutdown("/s /t " + secs))
        {
            StickyText("执行失败：无法投递定时关机");
            return;
        }
        scheduled = true;
        confirmed = true;
        target = DateTime.Now.AddSeconds(secs);
        stateTimer.Stop();                       // 倒计时接管状态栏，停掉瞬时清除
        lblState.Text = CountText(target - DateTime.Now);   // 立即反馈，不等首个 tick
        StartCountdown();
    }

    void CancelSchedule()
    {
        RunShutdown("/a");
        scheduled = false;
        confirmed = false;   // 解锁各按钮，可重新选择
        StopCountdown();
        StateText("回想.....");
    }

    // 氛围文案：只报剩余时间，别的不说
    static string CountText(TimeSpan left)
    {
        var fmt = left.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
        return "距忘却还剩 " + left.ToString(fmt);
    }

    void StartCountdown()
    {
        StopCountdown();
        countdown = new Timer { Interval = 1000 };
        countdown.Tick += delegate
        {
            var left = target - DateTime.Now;
            if (left <= TimeSpan.Zero) return;   // 到点由系统接管执行，界面倒计时自然结束
            lblState.Text = CountText(left);
        };
        countdown.Start();
    }

    void StopCountdown()
    {
        if (countdown == null) return;
        countdown.Stop();
        countdown.Dispose();
        countdown = null;
    }

    // 下拉菜单深色主题
    sealed class MenuTheme : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(15, 22, 40);
        public override Color MenuBorder => Color.FromArgb(46, 59, 96);
        public override Color MenuItemSelected => Color.FromArgb(35, 46, 78);
        public override Color MenuItemBorder => Color.FromArgb(58, 71, 112);
        public override Color SeparatorDark => Color.FromArgb(46, 59, 96);
        public override Color SeparatorLight => Color.FromArgb(46, 59, 96);
    }

    Label Label(string text, float size, FontStyle style, Color color, int x, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(x, y, 364, 24)
        };
    }

    void OnPaint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(ClientRectangle, ColBg1, ColBg2, 105f))
            g.FillRectangle(bg, ClientRectangle);

        // 顶部青色氛围光
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(Width - 220, -120, 380, 200);
            using (var pg = new PathGradientBrush(glow))
            {
                pg.CenterColor = Color.FromArgb(28, ColCyan);
                pg.SurroundColors = new[] { Color.FromArgb(0, ColCyan) };
                g.FillPath(pg, glow);
            }
        }

        // 月牙（忘却之月）
        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(MoonRect.X - 6, MoonRect.Y - 6, MoonRect.Width + 12, MoonRect.Height + 12);
            using (var pg = new PathGradientBrush(halo))
            {
                pg.CenterColor = Color.FromArgb(70, ColMoonB);
                pg.SurroundColors = new[] { Color.FromArgb(0, ColMoonB) };
                g.FillPath(pg, halo);
            }
        }
        using (var brush = new LinearGradientBrush(MoonRect, ColMoonA, ColMoonB, 45f))
            g.FillEllipse(brush, MoonRect);
        using (var cut = new SolidBrush(ColBg1))
            g.FillEllipse(cut, MoonRect.X + 10, MoonRect.Y - 4, MoonRect.Width, MoonRect.Height);

        using (var pen = new Pen(ColBorder))
            g.DrawPath(pen, Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 14));
    }

    GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // 无边框拖动
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    void Drag(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }
    }

    // 窗体空白处按下：点月牙最小化到任务栏，否则无边框拖动
    void OnFormMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (MoonRect.Contains(e.Location))
        {
            WindowState = FormWindowState.Minimized;
            return;
        }
        Drag(sender, e);
    }

    bool RunShutdown(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("shutdown.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch
        {
            return false;   // shutdown 不可用时保持界面可用，由调用方提示
        }
    }
}
