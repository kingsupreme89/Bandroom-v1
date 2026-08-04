using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Team background image slot behind the header strip -- very low opacity (~10%),
/// dark scrim on top, per the design handoff. Images are picked up by convention: drop a
/// file named exactly after the team (e.g. "Florida.jpg") into ConfigStore.TeamBackgroundsFolder
/// and it's used automatically the moment that team is selected. No image present = just the
/// flat panel background underneath, nothing breaks.</summary>
internal sealed class TeamBackdrop : Panel
{
    Image? _image;
    string? _loadedForTeam;

    static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp" };

    /// <summary>Folder of generic (non-team-specific) stadium images used as a fallback for
    /// any team without a dedicated background -- most of the ~148-team roster won't have
    /// one, so this keeps the backdrop from just going flat for them.</summary>
    static string GenericFolder => Path.Combine(ConfigStore.TeamBackgroundsFolder, "_generic");

    public TeamBackdrop()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    public static string? FindImagePath(string teamName)
    {
        foreach (var ext in Extensions)
        {
            string candidate = Path.Combine(ConfigStore.TeamBackgroundsFolder, teamName + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return FindGenericImagePath(teamName);
    }

    /// <summary>Deterministic pick from the generic pool, keyed by team name so the same team
    /// always gets the same generic image (not random per launch).</summary>
    static string? FindGenericImagePath(string teamName)
    {
        if (!Directory.Exists(GenericFolder)) return null;
        var files = Extensions.SelectMany(ext => Directory.GetFiles(GenericFolder, "*" + ext)).OrderBy(f => f).ToArray();
        if (files.Length == 0) return null;
        int index = (teamName.GetHashCode() & int.MaxValue) % files.Length;
        return files[index];
    }

    public void RefreshForTeam(string teamName)
    {
        if (teamName == _loadedForTeam) return;
        _loadedForTeam = teamName;

        _image?.Dispose();
        _image = null;

        string? path = FindImagePath(teamName);
        if (path != null)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                _image = Image.FromStream(fs); // copy off the stream so the file isn't held locked
            }
            catch (Exception ex)
            {
                CrashLog.Write($"Failed to load team background for \"{teamName}\"", ex);
            }
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Parent != null)
            using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        if (_image == null) return;

        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Cover-fit into this control's rect.
        float scale = Math.Max((float)Width / _image.Width, (float)Height / _image.Height);
        int drawW = (int)(_image.Width * scale), drawH = (int)(_image.Height * scale);
        int drawX = (Width - drawW) / 2, drawY = (Height - drawH) / 2;

        using var attrs = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = 0.10f }; // ~10% opacity per design spec
        attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(_image, new Rectangle(drawX, drawY, drawW, drawH), 0, 0, _image.Width, _image.Height, GraphicsUnit.Pixel, attrs);

        // Dark scrim on top so foreground text stays readable.
        using var scrim = new SolidBrush(Color.FromArgb(140, Theme.PanelBg));
        g.FillRectangle(scrim, ClientRectangle);
    }
}
