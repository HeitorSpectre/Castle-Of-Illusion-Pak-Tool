using System.Reflection;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Main window: two buttons - Extract and Reinsert - with DW9E-style artwork.
/// The UI is built in code (no designer) to keep the project simple.
/// </summary>
public sealed class MainForm : Form
{
    private readonly Button _btnExtract;
    private readonly Button _btnReinsert;
    private readonly ProgressBar _progress;
    private readonly Label _statusLabel;
    private readonly Label _footerLabel;
    private readonly Image? _banner;

    public MainForm()
    {
        Text = "Castle Of Illusion - Pak Tool";
        ClientSize = new Size(492, 300);
        MinimumSize = Size;
        MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        DoubleBuffered = true;

        _banner = LoadEmbeddedImage("Banner.png");
        Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

        _btnExtract = new Button
        {
            Text = "Extract",
            Font = new Font(Font.FontFamily, 9f),
        };
        _btnExtract.Click += OnExtractClick;

        _btnReinsert = new Button
        {
            Text = "Reinsert",
            Font = new Font(Font.FontFamily, 9f),
        };
        _btnReinsert.Click += OnReinsertClick;

        _progress = new ProgressBar
        {
            Visible = false,
        };

        _statusLabel = new Label
        {
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "",
        };
        _statusLabel.Paint += (_, e) => DrawOutlinedText(
            e.Graphics,
            _statusLabel.ClientRectangle,
            _statusLabel.Text,
            _statusLabel.Font,
            Color.White,
            Color.Black);

        _footerLabel = new Label
        {
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Tool by Heitor Spectre - 2026",
        };
        _footerLabel.Paint += (_, e) => DrawOutlinedText(
            e.Graphics,
            _footerLabel.ClientRectangle,
            _footerLabel.Text,
            _footerLabel.Font,
            Color.White,
            Color.Black);

        _btnExtract.SetBounds(34, 236, 82, 30);
        _btnReinsert.SetBounds(ClientSize.Width - 116, 236, 82, 30);
        _progress.SetBounds(140, 238, 212, 18);
        _statusLabel.SetBounds(112, 210, 268, 24);
        _footerLabel.SetBounds(140, 268, 212, 24);

        Controls.Add(_btnExtract);
        Controls.Add(_btnReinsert);
        Controls.Add(_progress);
        Controls.Add(_statusLabel);
        Controls.Add(_footerLabel);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_banner is not null)
        {
            e.Graphics.DrawImage(_banner, ClientRectangle);
            return;
        }

        using System.Drawing.Drawing2D.LinearGradientBrush brush = new(
            ClientRectangle,
            Color.FromArgb(45, 65, 78),
            Color.FromArgb(20, 25, 30),
            0f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _banner?.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- Extract

    private async void OnExtractClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select the .pak file",
            Filter = "PAK files (*.pak)|*.pak|All files (*.*)|*.*",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        string pakPath = ofd.FileName;

        SetBusy(true);
        Log($"Extracting: {pakPath}");

        try
        {
            // Extraction creates a folder named after the file, next to the .pak.
            string outDir = await Task.Run(() =>
            {
                var extractor = new PakExtractor
                {
                    Progress = (done, total, name) => ReportProgress(done, total, name),
                };
                return extractor.Extract(pakPath);
            });

            Log($"Done. Folder created: {outDir}");
            MessageBox.Show(this,
                $"Extraction completed successfully!\n\nFolder: {outDir}\n\n" +
                "Files were organized into folders by type (Textures, Models, Animations, " +
                "Audio, Materials, Shaders, etc.) and given readable names recovered from the game data.",
                "Castle Of Illusion - Pak Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Extraction error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------- Reinsert

    private async void OnReinsertClick(object? sender, EventArgs e)
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "Select the extracted folder (the one containing manifest.json)",
            UseDescriptionForTitle = true,
        };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;
        string extractedDir = fbd.SelectedPath;

        if (!File.Exists(Path.Combine(extractedDir, PakExtractor.ManifestFileName)))
        {
            MessageBox.Show(this, "The selected folder has no manifest.json. Pick a folder produced by Extract.",
                "Reinsert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title = "Save the rebuilt .pak as",
            Filter = "PAK files (*.pak)|*.pak|All files (*.*)|*.*",
            FileName = "data.pak",
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string outPath = sfd.FileName;

        SetBusy(true);
        Log($"Reinserting from: {extractedDir}");
        Log($"Output: {outPath}");

        try
        {
            await Task.Run(() =>
            {
                var builder = new PakBuilder
                {
                    Progress = (done, total, msg) => ReportProgress(done, total, msg),
                };
                builder.Build(extractedDir, outPath);
            });

            Log("Reinsertion completed.");
            MessageBox.Show(this, $"Reinsertion completed successfully!\n\nFile: {outPath}",
                "Castle Of Illusion - Pak Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Reinsertion error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ----------------------------------------------------------------- Util

    private void SetBusy(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _btnExtract.Enabled = !busy;
        _btnReinsert.Enabled = !busy;
        _progress.Visible = busy;
        if (!busy)
        {
            _progress.Value = 0;
            _progress.Style = ProgressBarStyle.Blocks;
            _statusLabel.Text = "";
            _statusLabel.Invalidate();
        }
    }

    private void ReportProgress(int done, int total, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ReportProgress(done, total, message));
            return;
        }
        int maximum = Math.Max(1, total);
        _progress.Maximum = maximum;
        _progress.Value = Math.Min(done, maximum);
        // Avoid flooding the UI: write every 250 files (or the last one).
        if (done == total || done % 250 == 0)
            Log(message);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        _statusLabel.Text = message;
        _statusLabel.Invalidate();
    }

    private static Image? LoadEmbeddedImage(string fileName)
    {
        Assembly assembly = typeof(MainForm).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        return stream is null ? null : Image.FromStream(stream);
    }

    private static void DrawOutlinedText(Graphics graphics, Rectangle bounds, string text, Font font, Color fill, Color outline)
    {
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(graphics, text, font, new Rectangle(bounds.X - 1, bounds.Y, bounds.Width, bounds.Height), outline, flags);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(bounds.X + 1, bounds.Y, bounds.Width, bounds.Height), outline, flags);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height), outline, flags);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height), outline, flags);
        TextRenderer.DrawText(graphics, text, font, bounds, fill, flags);
    }
}
