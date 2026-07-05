namespace VoiceTypePL.App.Overlay;

/// <summary>Parametry dymka (§5.4). Na tym etapie z configu aplikacji; pełne UI ustawień w Etapie 7.</summary>
public sealed class OverlayOptions
{
    /// <summary>Szerokość dymka w DIP-ach.</summary>
    public double WidthDip { get; set; } = 380;

    /// <summary>Wysokość dymka w DIP-ach (SizeToContent i tak dostroi, to wartość do pozycjonowania).</summary>
    public double HeightDip { get; set; } = 150;

    /// <summary>
    /// Po ilu sekundach bezczynności dymek sam znika. 0 = auto-ukrywanie wyłączone (§5.4).
    /// </summary>
    public double AutoHideSeconds { get; set; } = 12;
}
