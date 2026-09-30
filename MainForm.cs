namespace CodexSelfCheck;

internal sealed class MainForm : Form
{
    private readonly ReportService _reports = new();
    private readonly SelfCheckService _service;
    private readonly Label _statusLabel = new();
    private readonly TextBox _details = new();
    private readonly ListBox _checks = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _scanButton = new();
    private readonly Button _repairButton = new();
    private readonly Button _launchButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _cancelButton = new();
    private DiagnosticReport? _report;
    private CancellationTokenSource? _cancellation;

    internal MainForm()
    {
        _service = new SelfCheckService(_reports);
        Text = "Codex 桌面版自检启动器";
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Codex 桌面版自检启动器",
            Dock = DockStyle.Top,
            Height = 54,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Padding = new Padding(16, 12, 0, 0)
        };
        var introductionPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = Color.FromArgb(242, 247, 252),
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "自检启动器自我介绍"
        };
        var introductionTitle = new Label
        {
            Text = "检测范围",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 67, 112)
        };
        var introductionBody = new Label
        {
            Text = "Microsoft Store 包、签名、CODEX_CLI_PATH、cua_node 运行时、磁盘与启动状态。\r\n" +
                   "可修复的问题会先显示变更计划，修复后执行两次启动验证；安全策略和系统级变量只提供人工处理建议。",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(45, 55, 65),
            AccessibleDescription = "说明自检启动器的具体功能和解决的问题"
        };
        introductionPanel.Controls.Add(introductionBody);
        introductionPanel.Controls.Add(introductionTitle);

        _statusLabel.Text = "状态：尚未检测";
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 40;
        _statusLabel.Padding = new Padding(18, 8, 0, 0);
        _statusLabel.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);

        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Dock = DockStyle.Fill;
        _details.BackColor = SystemColors.Window;
        _details.Font = new Font("Consolas", 9F);

        _checks.Dock = DockStyle.Top;
        _checks.Height = 132;
        _checks.Font = new Font(Font.FontFamily, 9F);
        _checks.HorizontalScrollbar = true;
        _checks.IntegralHeight = false;
        _checks.AccessibleName = "检测项列表";

        _progress.Dock = DockStyle.Bottom;
        _progress.Height = 20;
        _progress.Style = ProgressBarStyle.Continuous;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(12, 10, 12, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ConfigureButton(_scanButton, "开始检测", async (_, _) => await ScanAsync());
        ConfigureButton(_repairButton, "修复并验证", async (_, _) => await RepairAsync());
        ConfigureButton(_launchButton, "启动 Codex", (_, _) => LaunchCodex());
        ConfigureButton(_exportButton, "导出报告", (_, _) => ExportReport());
        ConfigureButton(_cancelButton, "取消", (_, _) => _cancellation?.Cancel());
        _repairButton.Enabled = false;
        _launchButton.Enabled = false;
        _exportButton.Enabled = false;
        _cancelButton.Enabled = false;
        buttons.Controls.AddRange([_scanButton, _repairButton, _launchButton, _exportButton, _cancelButton]);

        Controls.Add(_details);
        Controls.Add(_checks);
        Controls.Add(_progress);
        Controls.Add(buttons);
        Controls.Add(_statusLabel);
        Controls.Add(introductionPanel);
        Controls.Add(title);
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.MinimumSize = new Size(112, 32);
        button.Margin = new Padding(5, 0, 5, 0);
        button.Click += handler;
    }

    private async Task ScanAsync()
    {
        SetBusy(true);
        _details.Text = "正在检测，请稍候……";
        _cancellation = new CancellationTokenSource();
        try
        {
            var progress = CreateProgress();
            _report = await _service.ScanAsync(true, progress, _cancellation.Token);
            ShowReport();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "状态：检测已取消";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RepairAsync()
    {
        if (_report is null || _report.Status != HealthStatus.Repairable) return;
        var changes = new List<string>();
        if (_report.Runtime?.MatchingRuntimeId is null)
            changes.Add($"• 安装运行时 {_report.ExpectedRuntimeId}");
        if (_report.CodexCliPathNeedsAttention)
            changes.Add("• 清除失效的用户级 CODEX_CLI_PATH");
        if (changes.Count == 0)
        {
            changes.Add("• 按检测结果执行受控运行时修复并验证");
        }
        var answer = MessageBox.Show(
            "将执行以下操作：\n\n" + string.Join("\n", changes) +
            "\n\n不会修改 .codex、登录信息或 VS Code 扩展。是否继续？",
            "确认修复",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        try
        {
            var result = await _service.RepairAsync(_report, CreateProgress(), _cancellation.Token);
            MessageBox.Show(result.Message, result.Success ? "修复完成" : "修复结果",
                MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (result.Success && result.BackupPaths.Count > 0)
            {
                var cleanup = MessageBox.Show(
                    "修复已验证成功。是否删除本次失败运行时备份？\n\n默认建议先保留；选择“否”不会影响 Codex。",
                    "清理备份",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (cleanup == DialogResult.Yes) RuntimeManager.DeleteBackups(result.BackupPaths);
            }

            _report = await _service.ScanAsync(false, CreateProgress(), CancellationToken.None);
            ShowReport();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LaunchCodex()
    {
        if (_report?.Package is null) return;
        try { _service.Launch(_report.Package); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportReport()
    {
        if (_report is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出 Codex 自检报告",
            Filter = "文本报告 (*.txt)|*.txt",
            FileName = $"Codex自检报告-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var paths = _reports.Export(_report, dialog.FileName);
            MessageBox.Show($"已生成：\n{paths.TextPath}\n{paths.JsonPath}", "导出完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _reports.Log($"导出报告失败：{exception}");
            MessageBox.Show($"报告导出失败：\n{exception.Message}", "导出失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Progress<OperationProgress> CreateProgress() => new(update =>
    {
        _statusLabel.Text = $"状态：{update.Stage} — {update.Message}";
        if (update.Percent >= 0)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Clamp(update.Percent, 0, 100);
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
        }
    });

    private void ShowReport()
    {
        if (_report is null) return;
        _statusLabel.Text = $"状态：{ReportService.StatusText(_report.Status)} — {_report.Summary}";
        _statusLabel.ForeColor = _report.Status switch
        {
            HealthStatus.Healthy => Color.DarkGreen,
            HealthStatus.Repairable => Color.DarkOrange,
            HealthStatus.ManualAction or HealthStatus.VerificationFailed => Color.Firebrick,
            _ => Color.FromArgb(28, 67, 112)
        };
        _details.Text = ReportService.BuildText(_report);
        _checks.BeginUpdate();
        _checks.Items.Clear();
        foreach (var check in _report.Checks)
        {
            _checks.Items.Add($"[{check.Status}] {check.Id} - {check.Summary}");
        }
        _checks.EndUpdate();
        _repairButton.Enabled = _report.Status == HealthStatus.Repairable;
        _launchButton.Enabled = _report.Package is not null;
        _exportButton.Enabled = true;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 100;
    }

    private void SetBusy(bool busy)
    {
        _scanButton.Enabled = !busy;
        _repairButton.Enabled = !busy && _report?.Status == HealthStatus.Repairable;
        _launchButton.Enabled = !busy && _report?.Package is not null;
        _exportButton.Enabled = !busy && _report is not null;
        _cancelButton.Enabled = busy;
        if (!busy) _cancellation?.Dispose();
    }
}
