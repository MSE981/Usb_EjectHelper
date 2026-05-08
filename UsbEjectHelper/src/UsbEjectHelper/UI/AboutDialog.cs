// UsbEjectHelper - Windows tray utility for safely ejecting USB removable storage devices.
// Copyright (C) 2026  Jin Bohan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using System.Reflection;

namespace UsbEjectHelper.UI;

/// <summary>
/// 关于对话框 —— 满足 GPL v3 §5(d) Appropriate Legal Notices 建议：
/// 显示版权、warranty 免责声明、获取源码与 LICENSE 的入口。
/// </summary>
public sealed class AboutDialog : Form
{
    private const string GnuGplUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    public AboutDialog()
    {
        Text = "关于 USB Eject Helper";
        Size = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var asm = Assembly.GetExecutingAssembly();
        var product = GetMeta<AssemblyProductAttribute>(asm)?.Product ?? "USB Eject Helper";
        var version = asm.GetName().Version?.ToString(3) ?? "0.0.0";
        var copyright = GetMeta<AssemblyCopyrightAttribute>(asm)?.Copyright ?? "Copyright (C) 2026 Jin Bohan";
        var description = GetMeta<AssemblyDescriptionAttribute>(asm)?.Description ?? string.Empty;

        var titleLabel = new Label
        {
            Text = product,
            Location = new Point(20, 20),
            AutoSize = true,
            Font = new Font(SystemFonts.CaptionFont!.FontFamily, 16, FontStyle.Bold)
        };

        var versionLabel = new Label
        {
            Text = $"版本 {version}",
            Location = new Point(20, 60),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        var copyrightLabel = new Label
        {
            Text = copyright,
            Location = new Point(20, 85),
            AutoSize = true
        };

        var descriptionLabel = new Label
        {
            Text = description,
            Location = new Point(20, 115),
            Size = new Size(510, 40),
            ForeColor = SystemColors.GrayText
        };

        var licenseHeader = new Label
        {
            Text = "—— 许可证 ——",
            Location = new Point(20, 165),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        var licenseBody = new Label
        {
            Text =
                "本程序是自由软件：您可依据自由软件基金会发布的 GNU 通用公共许可证（GPL）v3 或" +
                "（按您的选择）任何更新版本的条款，再发布及/或修改它。\n\n" +
                "本程序在希望它有用的前提下分发，但不附带任何担保；甚至没有适销性或对特定用途适用性的暗示担保。" +
                "详细信息请参阅 GNU 通用公共许可证。",
            Location = new Point(20, 185),
            Size = new Size(510, 110)
        };

        var viewLicenseButton = new Button
        {
            Text = "查看 LICENSE 文件",
            Location = new Point(20, 305),
            Size = new Size(150, 28)
        };
        viewLicenseButton.Click += OnViewLicense;

        var gnuLinkLabel = new LinkLabel
        {
            Text = "GPL v3 全文 (gnu.org)",
            Location = new Point(180, 311),
            AutoSize = true
        };
        gnuLinkLabel.LinkClicked += (_, _) => OpenUrl(GnuGplUrl);

        var okButton = new Button
        {
            Text = "确定",
            Location = new Point(440, 380),
            Size = new Size(90, 28),
            DialogResult = DialogResult.OK
        };
        okButton.Click += (_, _) => Close();

        AcceptButton = okButton;
        CancelButton = okButton;

        Controls.AddRange(new Control[]
        {
            titleLabel, versionLabel, copyrightLabel, descriptionLabel,
            licenseHeader, licenseBody,
            viewLicenseButton, gnuLinkLabel,
            okButton
        });
    }

    private static T? GetMeta<T>(Assembly asm) where T : Attribute
        => asm.GetCustomAttribute<T>();

    private void OnViewLicense(object? sender, EventArgs e)
    {
        var path = TryFindLicenseFile();
        if (path is null)
        {
            MessageBox.Show(
                "未找到 LICENSE 文件。请访问 " + GnuGplUrl + " 查看 GPL v3 全文。",
                "LICENSE 未找到",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法打开 LICENSE 文件：{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // 静默 —— LinkLabel 是用户可选辅助路径，不应抛错
        }
    }

    /// <summary>
    /// 在 exe 同目录及若干常见上层路径里查找 LICENSE 文件。
    /// 兼容开发期（项目子目录）与发布后（exe 同目录）两种布局。
    /// </summary>
    private static string? TryFindLicenseFile()
    {
        var probe = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(probe); i++)
        {
            var candidate = Path.Combine(probe, "LICENSE");
            if (File.Exists(candidate)) return candidate;
            probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
